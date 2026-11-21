using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using ApprovalAdministrationTool.Services;

namespace ApprovalAdministrationTool.Controls
{
    /// <summary>
    /// The query panel: the FetchXML the approvals grid runs, with the fixed start and end shown read-only and only the
    /// &lt;filter&gt; element editable. The filter is syntax-coloured and checked as you type (<see cref="FetchFilterValidator"/>);
    /// Run (or Ctrl+Enter) is enabled when it has no errors and raises <see cref="RunRequested"/>.
    /// </summary>
    public sealed class QueryEditorControl : UserControl
    {
        private const int WM_SETREDRAW = 0x000B;
        private const int EM_GETSCROLLPOS = 0x04DD;
        private const int EM_SETSCROLLPOS = 0x04DE;
        private const int MaxUndoSteps = 200;

        /// <summary>Start of the comment that marks where the grid's filters and search begin in the editable filter.</summary>
        private const string GridFiltersMarker = "Grid filters and search";

        private static readonly Color ElementColor = Color.FromArgb(163, 21, 21);
        private static readonly Color DelimiterColor = Color.Blue;
        private static readonly Color AttributeNameColor = Color.Red;
        private static readonly Color AttributeValueColor = Color.Blue;
        private static readonly Color CommentColor = Color.Green;
        private static readonly Color ErrorLineColor = Color.FromArgb(255, 226, 226);

        private static readonly Regex TagPattern = new Regex(@"(</?)([A-Za-z_][\w\-.:]*)|(/?>)");
        private static readonly Regex AttributePattern = new Regex("([A-Za-z_][\\w\\-.:]*)\\s*=\\s*(\"[^\"]*\"?|'[^']*'?)");
        private static readonly Regex CommentPattern = new Regex(@"<!--[\s\S]*?(-->|$)");

        private readonly RichTextBox prefixBox;
        private readonly RichTextBox filterBox;
        private readonly RichTextBox suffixBox;
        private readonly Label status;
        private readonly Button runButton;
        private readonly Timer lintDelay;
        private readonly Stack<string> undoSteps = new Stack<string>();
        private readonly Stack<string> redoSteps = new Stack<string>();
        private string lastText = string.Empty;
        private ICollection<string> tableColumns;
        private bool changingText;
        private IReadOnlyList<FetchFilterValidator.Problem> problems = new FetchFilterValidator.Problem[0];

        public QueryEditorControl()
        {
            var code = new Font("Consolas", 9.75f);
            Size = new Size(600, 320);
            Padding = new Padding(6, 4, 6, 4);

            var heading = new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4),
                Text = "Query (FetchXML). Only the <filter> can be edited; the rest is fixed. Press Run (or Ctrl+Enter) to load the approvals it returns.",
            };

            prefixBox = CodeBox(code, readOnly: true);
            filterBox = CodeBox(code, readOnly: false);
            suffixBox = CodeBox(code, readOnly: true);
            filterBox.AcceptsTab = true;
            filterBox.TextChanged += FilterBox_TextChanged;
            filterBox.KeyDown += FilterBox_KeyDown;

            status = new Label { AutoSize = true, Margin = new Padding(0, 6, 6, 0), MaximumSize = new Size(2000, 0) };
            runButton = new Button { Text = "Run", AutoSize = true, Margin = new Padding(0, 4, 0, 0), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            runButton.Click += (sender, e) => Run();

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(heading, 0, 0);
            layout.SetColumnSpan(heading, 2);
            layout.Controls.Add(prefixBox, 0, 1);
            layout.SetColumnSpan(prefixBox, 2);
            layout.Controls.Add(filterBox, 0, 2);
            layout.SetColumnSpan(filterBox, 2);
            layout.Controls.Add(suffixBox, 0, 3);
            layout.SetColumnSpan(suffixBox, 2);
            layout.Controls.Add(status, 0, 4);
            layout.Controls.Add(runButton, 1, 4);
            Controls.Add(layout);

            lintDelay = new Timer { Interval = 300 };
            lintDelay.Tick += (sender, e) => LintNow();
            Disposed += (sender, e) =>
            {
                lintDelay.Dispose();
                code.Dispose();
            };

            Lint();
        }

        /// <summary>Raised with the filter XML when Run is pressed (only when the filter has no errors).</summary>
        public event EventHandler<string> RunRequested;

        /// <summary>The editable &lt;filter&gt; part. Setting it clears the undo history.</summary>
        public string FilterXml
        {
            get => filterBox.Text;
            set
            {
                SetFilterText((value ?? string.Empty).Replace("\r\n", "\n"));
                undoSteps.Clear();
                redoSteps.Clear();
                LintNow();
            }
        }

        /// <summary>The problems found in the filter the last time it was checked.</summary>
        internal IReadOnlyList<FetchFilterValidator.Problem> Problems => problems;

        /// <summary>Whether Run is enabled (the filter has no errors).</summary>
        internal bool CanRun => runButton.Enabled;

        /// <summary>Shows the fixed start and end of the query around the filter.</summary>
        public void SetQueryParts(string prefix, string suffix)
        {
            prefixBox.Text = (prefix ?? string.Empty).Replace("\r\n", "\n");
            suffixBox.Text = (suffix ?? string.Empty).Replace("\r\n", "\n");
            Highlight(prefixBox, 0);
            Highlight(suffixBox, 0);
        }

        /// <summary>
        /// Puts the grid's filters and search into the editable filter, below a marker comment, replacing whatever was
        /// below the marker before (the user's own conditions above it are kept). The grid's conditions are AND'd with the
        /// user's: an "or" filter is wrapped in an "and" filter first. Nothing changes while the filter has XML errors.
        /// The change is one undo step.
        /// </summary>
        public void SetGridFilters(IReadOnlyList<XNode> gridFilters)
        {
            XElement root;
            try
            {
                root = string.IsNullOrWhiteSpace(filterBox.Text)
                    ? new XElement("filter", new XAttribute("type", "and"))
                    : XElement.Parse(filterBox.Text);
            }
            catch (XmlException)
            {
                return; // the filter is being edited and is not valid XML; its errors are already shown
            }

            if (root.Name.LocalName != "filter")
            {
                return;
            }

            var marker = root.Nodes().OfType<XComment>().FirstOrDefault(comment => comment.Value.Trim().StartsWith(GridFiltersMarker, StringComparison.Ordinal));
            if (marker != null)
            {
                marker.NodesAfterSelf().Remove();
                marker.Remove();
            }

            if (gridFilters != null && gridFilters.Count > 0)
            {
                if ((string)root.Attribute("type") == "or")
                {
                    root = root.HasElements
                        ? new XElement("filter", new XAttribute("type", "and"), root)
                        : new XElement("filter", new XAttribute("type", "and"), root.Nodes());
                }

                root.Add(new XComment(" " + GridFiltersMarker + " (updated automatically; edits below this line are replaced) "));
                root.Add(gridFilters);
            }

            var text = root.ToString().Replace("\r\n", "\n");
            if (text == filterBox.Text)
            {
                return;
            }

            undoSteps.Push(filterBox.Text);
            redoSteps.Clear();
            SetFilterText(text);
            LintNow();
        }

        /// <summary>The approval table's column names, so conditions naming other columns are reported.</summary>
        public void SetTableColumns(IEnumerable<string> columns)
        {
            tableColumns = columns == null ? null : new HashSet<string>(columns);
            LintNow();
        }

        /// <summary>Checks the filter now instead of waiting for the typing pause.</summary>
        internal void LintNow()
        {
            lintDelay.Stop();
            Lint();
        }

        private static RichTextBox CodeBox(Font font, bool readOnly)
        {
            var box = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = font,
                Multiline = true,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                DetectUrls = false,
                HideSelection = false,
                BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = readOnly,
                Margin = new Padding(0, 0, 0, 2),
            };

            if (readOnly)
            {
                box.BackColor = SystemColors.Control;
                box.TabStop = false;
            }

            return box;
        }

        private void Run()
        {
            LintNow();
            if (runButton.Enabled)
            {
                KeepGridFilters();
                RunRequested?.Invoke(this, FilterXml);
            }
        }

        /// <summary>
        /// Turns the grid's section into ordinary conditions by removing its marker comment: the query being run includes
        /// them, the grid's own filters are then cleared, and later grid filters start a new section below them.
        /// </summary>
        private void KeepGridFilters()
        {
            XElement root;
            try
            {
                root = XElement.Parse(filterBox.Text);
            }
            catch (XmlException)
            {
                return;
            }

            var marker = root.Nodes().OfType<XComment>().FirstOrDefault(comment => comment.Value.Trim().StartsWith(GridFiltersMarker, StringComparison.Ordinal));
            if (marker == null)
            {
                return;
            }

            marker.Remove();
            undoSteps.Push(filterBox.Text);
            redoSteps.Clear();
            SetFilterText(root.ToString().Replace("\r\n", "\n"));
            LintNow();
        }

        private void Lint()
        {
            problems = FetchFilterValidator.Validate(filterBox.Text, tableColumns);
            var errors = problems.Where(problem => problem.IsError).ToList();
            runButton.Enabled = errors.Count == 0;

            if (problems.Count == 0)
            {
                status.ForeColor = Color.ForestGreen;
                status.Text = string.IsNullOrWhiteSpace(filterBox.Text) ? "\u2713 No filter: every approval is returned." : "\u2713 The filter is valid.";
            }
            else
            {
                status.ForeColor = errors.Count > 0 ? Color.Firebrick : Color.DarkOrange;
                status.Text = string.Join("\n", problems.Take(3).Select(problem => (problem.IsError ? "Error: " : "Warning: ") + problem))
                    + (problems.Count > 3 ? "\n\u2026and " + (problems.Count - 3) + " more." : string.Empty);
            }

            Highlight(filterBox, errors.FirstOrDefault()?.Line ?? 0);
        }

        // ---------------------------------------------------------------- editing: undo, redo, plain paste

        private void FilterBox_TextChanged(object sender, EventArgs e)
        {
            if (changingText)
            {
                return;
            }

            if (filterBox.Text != lastText)
            {
                undoSteps.Push(lastText);
                if (undoSteps.Count > MaxUndoSteps)
                {
                    var keep = undoSteps.Take(MaxUndoSteps).Reverse().ToList();
                    undoSteps.Clear();
                    keep.ForEach(undoSteps.Push);
                }

                redoSteps.Clear();
                lastText = filterBox.Text;
            }

            lintDelay.Stop();
            lintDelay.Start();
        }

        private void FilterBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                Run();
            }
            else if (e.Control && !e.Shift && e.KeyCode == Keys.Z)
            {
                Step(undoSteps, redoSteps);
            }
            else if (e.Control && (e.KeyCode == Keys.Y || (e.Shift && e.KeyCode == Keys.Z)))
            {
                Step(redoSteps, undoSteps);
            }
            else if ((e.Control && e.KeyCode == Keys.V) || (e.Shift && e.KeyCode == Keys.Insert))
            {
                if (Clipboard.ContainsText())
                {
                    filterBox.SelectedText = Clipboard.GetText().Replace("\r\n", "\n"); // plain text only
                }
            }
            else
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void Step(Stack<string> from, Stack<string> to)
        {
            if (from.Count == 0)
            {
                return;
            }

            to.Push(filterBox.Text);
            var caret = filterBox.SelectionStart;
            SetFilterText(from.Pop());
            filterBox.SelectionStart = Math.Min(caret, filterBox.TextLength);
            LintNow();
        }

        private void SetFilterText(string text)
        {
            changingText = true;
            filterBox.Text = text;
            changingText = false;
            lastText = filterBox.Text;
        }

        // ---------------------------------------------------------------- syntax colouring

        /// <summary>Colours the XML in a box and tints <paramref name="errorLine"/> (1-based, 0 for none).</summary>
        private void Highlight(RichTextBox box, int errorLine)
        {
            changingText = true;
            var scroll = Point.Empty;
            SendMessage(box.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref scroll);
            SendMessage(box.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            var selectionStart = box.SelectionStart;
            var selectionLength = box.SelectionLength;

            box.SelectAll();
            box.SelectionColor = box.ForeColor;
            box.SelectionBackColor = box.BackColor;

            var text = box.Text;
            if (errorLine > 0 && errorLine <= box.Lines.Length)
            {
                var lineStart = box.GetFirstCharIndexFromLine(errorLine - 1);
                var lineEnd = errorLine < box.Lines.Length ? box.GetFirstCharIndexFromLine(errorLine) : text.Length;
                box.Select(lineStart, Math.Max(0, lineEnd - lineStart));
                box.SelectionBackColor = ErrorLineColor;
            }

            foreach (Match tag in TagPattern.Matches(text))
            {
                if (tag.Groups[2].Success)
                {
                    Colour(box, tag.Groups[1], DelimiterColor);
                    Colour(box, tag.Groups[2], ElementColor);
                }
                else
                {
                    Colour(box, tag.Groups[3], DelimiterColor);
                }
            }

            foreach (Match attribute in AttributePattern.Matches(text))
            {
                Colour(box, attribute.Groups[1], AttributeNameColor);
                Colour(box, attribute.Groups[2], AttributeValueColor);
            }

            foreach (Match comment in CommentPattern.Matches(text))
            {
                Colour(box, comment, CommentColor);
            }

            box.Select(selectionStart, selectionLength);
            SendMessage(box.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref scroll);
            SendMessage(box.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            box.Invalidate();
            changingText = false;
        }

        private static void Colour(RichTextBox box, Group part, Color color)
        {
            box.Select(part.Index, part.Length);
            box.SelectionColor = color;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Point lParam);
    }
}

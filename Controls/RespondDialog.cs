using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ApprovalAdministrationTool.Controls
{
    /// <summary>
    /// The Approve, Reject, Cancel and Remind window: a comment box at the top, then what the action applies to (tick to
    /// include). Approvals with custom responses get a dropdown each to pick the response. The button names the action;
    /// Enter does not press it, because these actions cannot be undone.
    /// </summary>
    internal sealed class RespondDialog : Form
    {
        private readonly TextBox commentsBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Height = 80 };
        private readonly CheckedListBox list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, Height = 110 };
        private readonly Button okButton = new Button { DialogResult = DialogResult.OK, AutoSize = true };
        private readonly Dictionary<Item, ComboBox> responseBoxes = new Dictionary<Item, ComboBox>();
        private readonly string buttonText;
        private bool buttonNamesResponse;

        /// <param name="title">The window's title.</param>
        /// <param name="button">The text of the button that does it, such as "Approve".</param>
        /// <param name="explanation">What will happen, in a sentence or two.</param>
        /// <param name="listHeading">What the list shows.</param>
        /// <param name="items">What the action can apply to.</param>
        /// <param name="skipped">Chosen approvals left out, with the reason.</param>
        public RespondDialog(string title, string button, string explanation, string listHeading, IEnumerable<Item> items, IReadOnlyList<string> skipped)
            : this(title, button, explanation, listHeading, items, skipped, null)
        {
        }

        /// <param name="notes">More to know, one line each (e.g. who has responded to an approval that waits for everyone).</param>
        public RespondDialog(string title, string button, string explanation, string listHeading, IEnumerable<Item> items, IReadOnlyList<string> skipped, IReadOnlyList<string> notes)
        {
            var itemList = items.ToList();
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = new TableLayoutPanel { AutoSize = true, Padding = new Padding(12), ColumnCount = 1, Width = 520 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500));
            layout.Controls.Add(Heading("Comment"));
            layout.Controls.Add(commentsBox);
            layout.Controls.Add(Heading(listHeading));
            layout.Controls.Add(list);

            var custom = itemList.Where(item => item.Choices != null && item.Choices.Count > 0).ToList();
            if (custom.Count > 0)
            {
                var responseHeading = Heading("Response (custom responses)");
                responseHeading.Name = "responseHeading";
                layout.Controls.Add(responseHeading);
                var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0) };
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 194));
                foreach (var item in custom)
                {
                    var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Name = "response" + responseBoxes.Count };
                    box.Items.AddRange(item.Choices.Cast<object>().ToArray());
                    var preselect = item.Choices.FirstOrDefault(choice => string.Equals(choice, item.Preselect, StringComparison.OrdinalIgnoreCase));
                    if (preselect != null)
                    {
                        box.SelectedItem = preselect;
                    }

                    box.SelectedIndexChanged += (sender, e) => UpdateOk(-1, CheckState.Indeterminate);
                    responseBoxes[item] = box;
                    grid.Controls.Add(new Label { Text = item.Text, AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                    grid.Controls.Add(box);
                }

                layout.Controls.Add(grid);
            }

            foreach (var note in notes ?? Array.Empty<string>())
            {
                layout.Controls.Add(Note(note));
            }

            layout.Controls.Add(Note(explanation));
            if (skipped.Count > 0)
            {
                layout.Controls.Add(Note("Skipped: " + string.Join("; ", skipped)));
            }

            buttonText = button;
            okButton.Text = button;
            okButton.AutoEllipsis = true;
            okButton.MaximumSize = new Size(320, 0);
            var closeButton = new Button { Text = "Close", DialogResult = DialogResult.Cancel, AutoSize = true };
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(closeButton);
            buttons.Controls.Add(okButton);
            layout.Controls.Add(buttons);
            Controls.Add(layout);
            CancelButton = closeButton;

            foreach (var item in itemList)
            {
                list.Items.Add(item, item.Ticked);
            }

            list.ItemCheck += (sender, e) => UpdateOk(e.Index, e.NewValue);
            UpdateOk(-1, CheckState.Indeterminate);
            Shown += (sender, e) =>
            {
                commentsBox.Focus();
                commentsBox.SelectionStart = commentsBox.TextLength;
            };
        }

        /// <summary>The comment typed (starts with any text set before the window is shown).</summary>
        public string Comments
        {
            get => commentsBox.Text;
            set => commentsBox.Text = value ?? string.Empty;
        }

        /// <summary>
        /// Whether the button shows the response picked (e.g. "Book it") instead of its own text: for the Action window,
        /// where Approve and Reject mean nothing. Different responses picked show "Send responses".
        /// </summary>
        public bool ButtonNamesResponse
        {
            get => buttonNamesResponse;
            set
            {
                buttonNamesResponse = value;
                UpdateOk(-1, CheckState.Indeterminate);
            }
        }

        /// <summary>The heading over the dropdowns.</summary>
        public string ResponseHeading
        {
            set
            {
                var heading = Controls.Count > 0 ? Controls[0].Controls.OfType<Label>().FirstOrDefault(label => label.Name == "responseHeading") : null;
                if (heading != null)
                {
                    heading.Text = value;
                }
            }
        }

        /// <summary>Why the button is off (nothing ticked, or a ticked approval has no response picked), or null.</summary>
        public string WhyNotReady { get; private set; }

        /// <summary>The response picked for an item with custom responses (by its <see cref="Item.Value"/>), or null.</summary>
        public string Response(object value)
        {
            var entry = responseBoxes.FirstOrDefault(pair => Equals(pair.Key.Value, value));
            return entry.Value?.SelectedItem as string;
        }

        /// <param name="changing">The item whose tick is changing (ItemCheck fires before the change), or -1.</param>
        private void UpdateOk(int changing, CheckState newValue)
        {
            var ticked = list.Items.Cast<Item>()
                .Where((item, index) => index == changing ? newValue == CheckState.Checked : list.GetItemChecked(index))
                .ToList();
            var missing = ticked.FirstOrDefault(item => responseBoxes.TryGetValue(item, out var box) && box.SelectedItem == null);
            WhyNotReady = ticked.Count == 0 ? "Tick at least one."
                : missing != null ? "Pick a response for " + missing.Text + "."
                : null;
            okButton.Enabled = WhyNotReady == null;
            if (buttonNamesResponse)
            {
                var picked = ticked.Select(item => responseBoxes.TryGetValue(item, out var box) ? box.SelectedItem as string : null)
                    .Where(response => response != null)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                okButton.Text = WhyNotReady == null && picked.Count == 1 ? picked[0].Replace("&", "&&")
                    : WhyNotReady == null && picked.Count > 1 ? "Send responses"
                    : buttonText;
            }
        }

        /// <summary>The <see cref="Item.Value"/> of each ticked item.</summary>
        public List<T> Selected<T>() => list.CheckedItems.Cast<Item>().Select(item => (T)item.Value).ToList();

        private static Label Heading(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold), Margin = new Padding(3, 8, 3, 3) };
        }

        private static Label Note(string text)
        {
            return new Label { Text = text, AutoSize = true, MaximumSize = new Size(500, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 8, 3, 0), UseMnemonic = false };
        }

        /// <summary>Something the action can apply to.</summary>
        internal sealed class Item
        {
            public Item(object value, string text, bool ticked)
                : this(value, text, ticked, null, null)
            {
            }

            /// <param name="choices">The custom responses to pick from in a dropdown, or null for none.</param>
            /// <param name="preselect">The response picked to start with, if it is one of <paramref name="choices"/>.</param>
            public Item(object value, string text, bool ticked, IReadOnlyList<string> choices, string preselect)
            {
                Value = value;
                Text = text;
                Ticked = ticked;
                Choices = choices;
                Preselect = preselect;
            }

            public object Value { get; }
            public string Text { get; }
            public bool Ticked { get; }
            public IReadOnlyList<string> Choices { get; }
            public string Preselect { get; }

            public override string ToString() => Text;
        }
    }
}

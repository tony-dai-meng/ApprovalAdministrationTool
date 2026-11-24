using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ApprovalAdministrationTool.Models;

namespace ApprovalAdministrationTool.Controls
{
    /// <summary>
    /// Shows the approvals grid. Display only: it never calls Dataverse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Column headers are drawn here so that text never gets cut off: it wraps onto extra lines (preferring to break after
    /// underscores and spaces, so logical names like msdyn_flow_approval_title wrap too) and the header row grows to fit.
    /// </para>
    /// <para>
    /// A column whose table also has a friendly-values column (logical name + <see cref="ApprovalColumnValues.FriendlySuffix"/>)
    /// shows the friendly values by default, with a switch icon in its header that swaps that column to the values stored in
    /// Dataverse. The visible column is bound to either the friendly or the stored table column, so sorting follows what is
    /// shown. Each grid column's Name is its logical name; its DataPropertyName can point at the friendly-values column.
    /// </para>
    /// </remarks>
    public partial class DataGridControl : UserControl
    {
        private const TextFormatFlags HeaderTextFlags =
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;

        private const int HeaderHorizontalPadding = 4; // each side
        private const int HeaderVerticalPadding = 5;   // each side
        private const int SortGlyphSpace = 16;         // room kept on the right for the sort arrow

        // An empty column after the last real one, so the last column's right border always has space to be dragged into.
        private const string SpacerColumnName = "spacer";
        private const int SpacerMinimumWidth = 80;

        private const int ToolTipLineLength = 80;      // header hover text wraps at this many characters

        private const int ValueToggleIconSize = 16;
        private const int ValueToggleIconSpace = ValueToggleIconSize + 4;

        private readonly HashSet<string> columnsShowingStoredValues = new HashSet<string>();
        private bool useLogicalNames;
        private bool headerFitPending;
        private bool handCursorShown;
        private Image valueToggleIcon;         // drawn while a column shows friendly values
        private Image valueToggleIconActive;   // drawn (highlighted) while a column shows stored values

        public DataGridControl()
        {
            InitializeComponent();
            Disposed += (sender, e) =>
            {
                valueToggleIcon?.Dispose();
                valueToggleIconActive?.Dispose();
                filterIcon?.Dispose();
                filterIconActive?.Dispose();
            };

            // Tick-box column (DataGridControl.Selection.cs).
            ApprovalGridView.CellFormatting += ApprovalGridView_CellFormatting;
            ApprovalGridView.CellMouseClick += ApprovalGridView_CellMouseClick;
            ApprovalGridView.KeyDown += ApprovalGridView_KeyDown;
            ApprovalGridView.CurrentCellChanged += (sender, e) => RaisePreviewIfChanged();
            ApprovalGridView.Sorted += (sender, e) =>
            {
                ApprovalGridView.InvalidateCell(-1, -1);
                RaisePreviewIfChanged();
            };
        }

        /// <summary>
        /// Shows the approvals and hides the ID column and the friendly-values columns. Column widths are fitted to the data
        /// and the friendly names once, here, so they stay the same when headers or values are switched.
        /// </summary>
        public void ShowApprovals(DataTable approvals)
        {
            if (approvals == null)
            {
                throw new ArgumentNullException(nameof(approvals));
            }

            // Start from no columns: the switchable columns are re-pointed at their friendly-values columns, so the grid no
            // longer treats them as its own auto-generated columns and would otherwise keep them alongside new ones.
            ApprovalGridView.DataSource = null;
            ApprovalGridView.Columns.Clear();
            ApprovalGridView.DataSource = approvals;
            EnsureSpacerColumn();
            SetUpSelectColumn(approvals);

            foreach (DataGridViewColumn column in ApprovalGridView.Columns)
            {
                if (column.Name == ApprovalColumns.Id || ApprovalColumns.Hidden.Contains(column.Name) || IsFriendlyValuesColumn(column))
                {
                    column.Visible = false;
                }
                else if (IsDataColumn(column))
                {
                    // Header clicks are handled here, so clicking the filter or switch icon does not also sort.
                    column.SortMode = DataGridViewColumnSortMode.Programmatic;
                    if (HasFriendlyValues(column))
                    {
                        column.DataPropertyName = ValuesPropertyName(column);
                    }
                }
            }

            // Fit each column to its data, widened if needed so its friendly name fits on one line.
            SetHeaderTexts(logicalNames: false);
            foreach (var column in DataColumns())
            {
                ApprovalGridView.AutoResizeColumn(column.Index, DataGridViewAutoSizeColumnMode.AllCellsExceptHeader);
                column.Width = Math.Max(column.Width, SingleLineHeaderWidth(column));
                column.ToolTipText = HeaderToolTip(column.HeaderText, column.Name, HasFriendlyValues(column));
            }

            SetHeaderTexts(useLogicalNames);
            FitHeaderHeight();
            ApplyFilters();

            // Always tell the preview after a load: the same approval may have new content.
            lastPreviewId = null;
            RaisePreviewIfChanged();
        }

        /// <summary>
        /// Switches the column headers between friendly names (the Dataverse display names) and logical names,
        /// keeping the current column widths.
        /// </summary>
        public void UseLogicalNameHeaders(bool logicalNames)
        {
            useLogicalNames = logicalNames;
            SetHeaderTexts(useLogicalNames);
            FitHeaderHeight();
            ApprovalGridView.Invalidate();
        }

        private void SetHeaderTexts(bool logicalNames)
        {
            if (!(ApprovalGridView.DataSource is DataTable table))
            {
                return;
            }

            foreach (var column in DataColumns())
            {
                column.HeaderText = logicalNames ? column.Name : table.Columns[column.Name].Caption;
            }
        }

        /// <summary>
        /// Hover text for a column header: its friendly and logical names, then its definition (if there is one) and, for
        /// columns with a value switch, how to use it. Wrapped to <see cref="ToolTipLineLength"/> characters so the tooltip
        /// does not stretch across the screen.
        /// </summary>
        internal static string HeaderToolTip(string friendlyName, string logicalName, bool hasValueSwitch)
        {
            var text = friendlyName == logicalName ? logicalName : friendlyName + " (" + logicalName + ")";
            if (ApprovalColumnDefinitions.ByLogicalName.TryGetValue(logicalName, out var definition))
            {
                text += "\n\n" + WrapWords(definition, ToolTipLineLength);
            }

            if (hasValueSwitch)
            {
                text += "\n\n" + WrapWords(
                    "Click the ⇄ icon in this header to switch this column between friendly values and the values stored in Dataverse.",
                    ToolTipLineLength);
            }

            text += "\n\n" + WrapWords(
                "Click the filter icon in this header to show only rows with the values you tick. Filters on different columns " +
                "apply together: a row is shown only if it matches every one.",
                ToolTipLineLength);

            return text;
        }

        private static string WrapWords(string text, int maxLineLength)
        {
            var lines = new List<string>();
            var line = string.Empty;
            foreach (var word in text.Split(' '))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > maxLineLength)
                {
                    lines.Add(line);
                    line = string.Empty;
                }

                line = line.Length == 0 ? word : line + " " + word;
            }

            if (line.Length > 0)
            {
                lines.Add(line);
            }

            return string.Join("\n", lines);
        }

        /// <summary>The columns showing approval data (not the spacer or the hidden friendly-values columns).</summary>
        private IEnumerable<DataGridViewColumn> DataColumns()
        {
            return ApprovalGridView.Columns.Cast<DataGridViewColumn>().Where(IsDataColumn);
        }

        /// <summary>
        /// Adds the spacer column once and keeps it last. It fills any spare width and never gets narrower than
        /// <see cref="SpacerMinimumWidth"/>, so there is always room to drag the last real column wider.
        /// </summary>
        private void EnsureSpacerColumn()
        {
            if (!ApprovalGridView.Columns.Contains(SpacerColumnName))
            {
                ApprovalGridView.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = SpacerColumnName,
                    HeaderText = string.Empty,
                    ReadOnly = true,
                    SortMode = DataGridViewColumnSortMode.NotSortable,
                    Resizable = DataGridViewTriState.False,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                    MinimumWidth = SpacerMinimumWidth,
                });
            }

            ApprovalGridView.Columns[SpacerColumnName].DisplayIndex = ApprovalGridView.Columns.Count - 1;
        }

        // ---------------------------------------------------------------- friendly values

        /// <summary>Whether the bound table has friendly values for this column (so its header gets a switch icon).</summary>
        private bool HasFriendlyValues(DataGridViewColumn column)
        {
            return ApprovalGridView.DataSource is DataTable table
                && table.Columns.Contains(column.Name + ApprovalColumnValues.FriendlySuffix);
        }

        private static bool IsFriendlyValuesColumn(DataGridViewColumn column)
        {
            return column.Name.EndsWith(ApprovalColumnValues.FriendlySuffix, StringComparison.Ordinal);
        }

        /// <summary>The table column a switchable column is bound to: the friendly values unless it was switched.</summary>
        private string ValuesPropertyName(DataGridViewColumn column)
        {
            return columnsShowingStoredValues.Contains(column.Name)
                ? column.Name
                : column.Name + ApprovalColumnValues.FriendlySuffix;
        }

        /// <summary>
        /// Swaps one column between friendly values and stored values, keeping its width and, if it is the sorted column,
        /// re-applying the sort so it follows the values now shown.
        /// </summary>
        private void ToggleFriendlyValues(DataGridViewColumn column)
        {
            if (!columnsShowingStoredValues.Remove(column.Name))
            {
                columnsShowingStoredValues.Add(column.Name);
            }

            var sortedColumn = ApprovalGridView.SortedColumn;
            var sortOrder = ApprovalGridView.SortOrder;
            var fromProperty = column.DataPropertyName;
            var toProperty = ValuesPropertyName(column);
            TranslateFilter(column, fromProperty, toProperty);
            column.DataPropertyName = toProperty;
            ApplyFilters();

            if (sortedColumn == column && sortOrder != SortOrder.None)
            {
                ApprovalGridView.Sort(column, sortOrder == SortOrder.Ascending ? ListSortDirection.Ascending : ListSortDirection.Descending);
            }

            ApprovalGridView.InvalidateColumn(column.Index);
        }

        /// <summary>
        /// Header clicks on data columns: the filter icon opens the column's filter menu, the switch icon swaps its values,
        /// and anywhere else sorts the column (ascending, then descending).
        /// </summary>
        private void ApprovalGridView_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.ColumnIndex < 0)
            {
                return;
            }

            var column = ApprovalGridView.Columns[e.ColumnIndex];
            if (!IsDataColumn(column))
            {
                return;
            }

            if (IsOverHeaderIcon(FilterIconBounds(column, HeaderCell(column)), e.Location))
            {
                ShowFilterMenu(column);
                return;
            }

            if (HasFriendlyValues(column) && IsOverHeaderIcon(ValueToggleIconBounds(column, HeaderCell(column)), e.Location))
            {
                ToggleFriendlyValues(column);
                return;
            }

            var direction = ApprovalGridView.SortedColumn == column && ApprovalGridView.SortOrder == SortOrder.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
            ApprovalGridView.Sort(column, direction);
        }

        /// <summary>Shows a hand pointer over a header's filter or switch icon.</summary>
        private void ApprovalGridView_CellMouseMove(object sender, DataGridViewCellMouseEventArgs e)
        {
            var overIcon = false;
            if (e.RowIndex == -1 && e.ColumnIndex >= 0 && IsDataColumn(ApprovalGridView.Columns[e.ColumnIndex]))
            {
                var column = ApprovalGridView.Columns[e.ColumnIndex];
                overIcon = IsOverHeaderIcon(FilterIconBounds(column, HeaderCell(column)), e.Location)
                    || (HasFriendlyValues(column) && IsOverHeaderIcon(ValueToggleIconBounds(column, HeaderCell(column)), e.Location));
            }

            ShowHandCursor(overIcon);
        }

        private void ApprovalGridView_CellMouseLeave(object sender, DataGridViewCellEventArgs e)
        {
            ShowHandCursor(false);
        }

        private void ShowHandCursor(bool show)
        {
            if (show == handCursorShown)
            {
                return;
            }

            handCursorShown = show;
            ApprovalGridView.Cursor = show ? Cursors.Hand : Cursors.Default;
        }

        /// <summary>A column's header cell with its top-left corner at (0, 0), matching header mouse-event coordinates.</summary>
        private Rectangle HeaderCell(DataGridViewColumn column)
        {
            return new Rectangle(0, 0, column.Width, ApprovalGridView.ColumnHeadersHeight);
        }

        /// <param name="icon">The icon's bounds within the header cell.</param>
        /// <param name="location">A point relative to the header cell's top-left corner.</param>
        private static bool IsOverHeaderIcon(Rectangle icon, Point location)
        {
            icon.Inflate(2, 2);
            return icon.Contains(location);
        }

        /// <summary>The switch icon sits just left of the filter icon.</summary>
        private static Rectangle ValueToggleIconBounds(DataGridViewColumn column, Rectangle cellBounds)
        {
            var right = cellBounds.Right - HeaderHorizontalPadding - SortGlyphSpace - FilterIconSpace;
            return new Rectangle(
                right - ValueToggleIconSize,
                cellBounds.Y + (cellBounds.Height - ValueToggleIconSize) / 2,
                ValueToggleIconSize,
                ValueToggleIconSize);
        }

        private void DrawValueToggle(Graphics graphics, DataGridViewColumn column, Rectangle cellBounds, Color foreColor)
        {
            var bounds = ValueToggleIconBounds(column, cellBounds);
            var showingStored = columnsShowingStoredValues.Contains(column.Name);
            var color = showingStored ? SystemColors.Highlight : foreColor;
            var icon = showingStored
                ? valueToggleIconActive ?? (valueToggleIconActive = SymbolIcons.Create(SymbolIcons.Switch, ValueToggleIconSize, color))
                : valueToggleIcon ?? (valueToggleIcon = SymbolIcons.Create(SymbolIcons.Switch, ValueToggleIconSize, color));

            if (icon != null)
            {
                graphics.DrawImage(icon, bounds);
            }
            else
            {
                TextRenderer.DrawText(graphics, "⇄", column.HeaderCell.InheritedStyle.Font, bounds, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        // ---------------------------------------------------------------- header layout and painting

        /// <summary>
        /// Re-fits the header height after a column is resized. This is deferred until the grid has finished its own layout:
        /// the grid does not allow the header height to change while it is re-fitting the spacer column.
        /// </summary>
        private void ApprovalGridView_ColumnWidthChanged(object sender, DataGridViewColumnEventArgs e)
        {
            if (e.Column.Name == SpacerColumnName || headerFitPending || !IsHandleCreated)
            {
                return;
            }

            headerFitPending = true;
            BeginInvoke(new Action(() =>
            {
                headerFitPending = false;
                FitHeaderHeight();
            }));
        }

        /// <summary>
        /// Draws the header text wrapped to the column width instead of letting the grid cut it off, plus the switch icon
        /// on switchable columns.
        /// </summary>
        private void ApprovalGridView_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex < 0 || PaintSelectColumn(e))
            {
                return;
            }

            // Spacer cells are drawn as plain background, so the spacer looks like empty space rather than a column.
            if (e.RowIndex >= 0 && ApprovalGridView.Columns[e.ColumnIndex].Name == SpacerColumnName)
            {
                using (var background = new SolidBrush(ApprovalGridView.BackgroundColor))
                {
                    e.Graphics.FillRectangle(background, e.CellBounds);
                }

                e.Handled = true;
                return;
            }

            if (e.RowIndex != -1)
            {
                return;
            }

            var column = ApprovalGridView.Columns[e.ColumnIndex];
            var textBounds = HeaderTextBounds(column, e.CellBounds);

            e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
            TextRenderer.DrawText(e.Graphics, WrapHeaderText(column.HeaderText, e.CellStyle.Font, textBounds.Width),
                e.CellStyle.Font, textBounds, e.CellStyle.ForeColor, HeaderTextFlags);
            if (IsDataColumn(column))
            {
                DrawFilterIcon(e.Graphics, column, e.CellBounds, e.CellStyle.ForeColor);
            }

            if (HasFriendlyValues(column))
            {
                DrawValueToggle(e.Graphics, column, e.CellBounds, e.CellStyle.ForeColor);
            }

            e.Handled = true;
        }

        /// <summary>
        /// Makes the header row tall enough for the longest wrapped header, and no shorter than a single line.
        /// </summary>
        private void FitHeaderHeight()
        {
            var tallestText = ApprovalGridView.Columns.Cast<DataGridViewColumn>()
                .Where(column => column.Visible && column.Name != SelectColumnName)
                .Select(column =>
                {
                    var font = column.HeaderCell.InheritedStyle.Font;
                    var width = HeaderTextBounds(column, new Rectangle(0, 0, column.Width, 0)).Width;
                    return TextRenderer.MeasureText(WrapHeaderText(column.HeaderText, font, width), font, Size.Empty, HeaderTextFlags).Height;
                })
                .DefaultIfEmpty(0)
                .Max();

            var height = Math.Max(MinimumHeaderHeight(), tallestText + 2 * HeaderVerticalPadding);
            if (ApprovalGridView.ColumnHeadersHeight != height)
            {
                ApprovalGridView.ColumnHeadersHeight = height;
            }
        }

        private int MinimumHeaderHeight()
        {
            var font = ApprovalGridView.ColumnHeadersDefaultCellStyle.Font ?? ApprovalGridView.Font;
            return Math.Max(font.Height, ValueToggleIconSize) + 2 * HeaderVerticalPadding;
        }

        /// <summary>The column width at which the column's current header text fits on one line.</summary>
        private int SingleLineHeaderWidth(DataGridViewColumn column)
        {
            var textWidth = TextRenderer.MeasureText(column.HeaderText ?? string.Empty, column.HeaderCell.InheritedStyle.Font, Size.Empty, HeaderTextFlags).Width;
            return textWidth + HeaderNonTextWidth(column) + 1;
        }

        /// <summary>
        /// Horizontal space in a header cell that is not available for text: padding, the sort arrow, the filter icon and,
        /// on switchable columns, the switch icon.
        /// </summary>
        private int HeaderNonTextWidth(DataGridViewColumn column)
        {
            var sortGlyph = column.SortMode == DataGridViewColumnSortMode.NotSortable ? 0 : SortGlyphSpace;
            var filter = IsDataColumn(column) ? FilterIconSpace : 0;
            var valueToggle = HasFriendlyValues(column) ? ValueToggleIconSpace : 0;
            return 2 * HeaderHorizontalPadding + column.HeaderCell.InheritedStyle.Padding.Horizontal + sortGlyph + filter + valueToggle;
        }

        private Rectangle HeaderTextBounds(DataGridViewColumn column, Rectangle cellBounds)
        {
            var padding = column.HeaderCell.InheritedStyle.Padding;
            return new Rectangle(
                cellBounds.X + HeaderHorizontalPadding + padding.Left,
                cellBounds.Y + HeaderVerticalPadding + padding.Top,
                Math.Max(1, cellBounds.Width - HeaderNonTextWidth(column)),
                Math.Max(1, cellBounds.Height - 2 * HeaderVerticalPadding - padding.Vertical));
        }

        /// <summary>
        /// Splits <paramref name="text"/> into lines no wider than <paramref name="maxWidth"/>, breaking after underscores and
        /// spaces where possible, and inside a word only when that word alone is wider than the column.
        /// </summary>
        internal static string WrapHeaderText(string text, Font font, int maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0)
            {
                return text;
            }

            int Measure(string line) => TextRenderer.MeasureText(line, font, Size.Empty, HeaderTextFlags).Width;

            var lines = new List<string>();
            var current = string.Empty;
            foreach (var piece in Regex.Split(text, "(?<=[_ ])").Where(piece => piece.Length > 0))
            {
                if (current.Length > 0 && Measure((current + piece).TrimEnd()) > maxWidth)
                {
                    lines.Add(current.TrimEnd());
                    current = string.Empty;
                }

                current += piece;

                // A single piece wider than the column: break it between characters.
                while (current.Length > 1 && Measure(current.TrimEnd()) > maxWidth)
                {
                    var fit = current.Length - 1;
                    while (fit > 1 && Measure(current.Substring(0, fit)) > maxWidth)
                    {
                        fit--;
                    }

                    lines.Add(current.Substring(0, fit));
                    current = current.Substring(fit);
                }
            }

            if (current.Trim().Length > 0)
            {
                lines.Add(current.TrimEnd());
            }

            return string.Join("\n", lines);
        }
    }
}

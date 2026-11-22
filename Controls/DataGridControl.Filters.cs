using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using ApprovalAdministrationTool.Models;

namespace ApprovalAdministrationTool.Controls
{
    /// <summary>
    /// Column filters: each data column's header has a filter icon that opens a <see cref="ColumnFilterPopup"/> listing the
    /// column's values. A row is shown when, for every filtered column, its value is one of that column's ticked values
    /// (values within a column are OR'd, columns are AND'd). Filters hold the values as displayed, so on a column with a
    /// value switch they follow the switch, and they are kept across Refresh.
    /// </summary>
    public partial class DataGridControl
    {
        private const int FilterIconSize = 16;
        private const int FilterIconSpace = FilterIconSize + 4;

        /// <summary>Ticked values per filtered column (by column name), as displayed; "" stands for empty cells.</summary>
        private readonly Dictionary<string, HashSet<string>> filters = new Dictionary<string, HashSet<string>>();
        private string searchText = string.Empty;
        private Image filterIcon;         // drawn on columns without a filter
        private Image filterIconActive;   // drawn (highlighted) on filtered columns

        /// <summary>Raised when the user changes a column filter or the search (see <see cref="FetchXmlForGridFilters"/>).</summary>
        public event EventHandler FiltersChanged;

        /// <summary>Whether a column shows approval data, and so can be sorted and filtered (not the spacer or hidden helpers).</summary>
        private static bool IsDataColumn(DataGridViewColumn column)
        {
            return !string.IsNullOrEmpty(column.DataPropertyName) && !IsFriendlyValuesColumn(column);
        }

        /// <summary>
        /// Keeps only rows whose value in <paramref name="columnName"/> is one of <paramref name="keptValues"/>
        /// (as displayed; "" for empty cells), or removes the column's filter when <paramref name="keptValues"/> is null.
        /// </summary>
        internal void SetColumnFilter(string columnName, IEnumerable<string> keptValues)
        {
            if (keptValues == null)
            {
                filters.Remove(columnName);
            }
            else
            {
                filters[columnName] = new HashSet<string>(keptValues);
            }

            ApplyFilters();
            ApprovalGridView.InvalidateColumn(ApprovalGridView.Columns.Contains(columnName) ? ApprovalGridView.Columns[columnName].Index : 0);
            FiltersChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Shows only rows where at least one visible column contains <paramref name="text"/> (as displayed, ignoring case),
        /// on top of the column filters. Empty text removes the search.
        /// </summary>
        public void SetSearchText(string text)
        {
            var trimmed = (text ?? string.Empty).Trim();
            if (trimmed == searchText)
            {
                return;
            }

            searchText = trimmed;
            ApplyFilters();
            FiltersChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Removes every column filter and the search, without raising <see cref="FiltersChanged"/> (used when a custom
        /// query is run: its filter already holds the grid's conditions, so the grid must not filter the new rows again).
        /// </summary>
        public void ClearFilters()
        {
            if (filters.Count == 0 && searchText.Length == 0)
            {
                return;
            }

            filters.Clear();
            searchText = string.Empty;
            ApplyFilters();
            ApprovalGridView.Invalidate();
        }

        // ---------------------------------------------------------------- grid filters as FetchXML

        /// <summary>
        /// The grid's column filters and search as FetchXML nodes for the approval table, to be AND'd together: each filtered
        /// column becomes a condition on its stored values (a choice's numbers, a person's ID, Yes/No as 1/0, text as is;
        /// "(Blanks)" as null), and the search becomes an "or" filter (text columns "like" the text; other columns the stored
        /// values whose displayed text contains it). Currently With is calculated by this tool, so filtering or searching it
        /// only adds a comment saying it applies in the grid only.
        /// </summary>
        internal IReadOnlyList<XNode> FetchXmlForGridFilters()
        {
            var nodes = new List<XNode>();
            if (!(ApprovalGridView.DataSource is DataTable table))
            {
                return nodes;
            }

            foreach (var filter in filters.Where(filter => ApprovalGridView.Columns.Contains(filter.Key)))
            {
                var column = ApprovalGridView.Columns[filter.Key];
                if (ApprovalColumns.Calculated.ContainsKey(column.Name))
                {
                    nodes.Add(new XComment(" " + Caption(table, column) + " filter: applied in the grid only (calculated by this tool, so not part of the query) "));
                    continue;
                }

                nodes.Add(ConditionForFilter(table, column, filter.Value));
            }

            if (searchText.Length > 0)
            {
                nodes.AddRange(FetchXmlForSearch(table));
            }

            return nodes;
        }

        private XNode ConditionForFilter(DataTable table, DataGridViewColumn column, HashSet<string> keptValues)
        {
            var shown = keptValues.Where(value => value.Length > 0).ToList();
            var stored = (StoredValuesWhere(table, column, shown.Contains) ?? shown).Distinct().ToList();

            var parts = new List<XElement>();
            if (stored.Count == 1)
            {
                parts.Add(FetchCondition(column.Name, "eq", stored[0]));
            }
            else if (stored.Count > 1)
            {
                parts.Add(new XElement("condition",
                    new XAttribute("attribute", column.Name),
                    new XAttribute("operator", "in"),
                    stored.Select(value => new XElement("value", value))));
            }

            if (keptValues.Contains(ColumnFilterPopup.BlankValue))
            {
                parts.Add(FetchCondition(column.Name, "null", null));
            }

            if (parts.Count == 0)
            {
                return new XComment(" " + Caption(table, column) + " filter: none of its values could be matched to stored values ");
            }

            return parts.Count == 1 ? (XNode)parts[0] : new XElement("filter", new XAttribute("type", "or"), parts);
        }

        private IEnumerable<XNode> FetchXmlForSearch(DataTable table)
        {
            var conditions = new List<XElement>();
            var gridOnly = new List<string>();
            foreach (var column in DataColumns().Where(column => column.Visible && column.ValueType != typeof(Guid)))
            {
                if (ApprovalColumns.Calculated.ContainsKey(column.Name))
                {
                    gridOnly.Add(Caption(table, column));
                    continue;
                }

                var stored = StoredValuesWhere(table, column, value => value.IndexOf(searchText, StringComparison.CurrentCultureIgnoreCase) >= 0);
                if (stored == null)
                {
                    conditions.Add(FetchCondition(column.Name, "like", "%" + EscapeFetchLike(searchText) + "%"));
                    continue;
                }

                var values = stored.Distinct().ToList();
                if (values.Count == 1)
                {
                    conditions.Add(FetchCondition(column.Name, "eq", values[0]));
                }
                else if (values.Count > 1)
                {
                    conditions.Add(new XElement("condition",
                        new XAttribute("attribute", column.Name),
                        new XAttribute("operator", "in"),
                        values.Select(value => new XElement("value", value))));
                }
            }

            if (gridOnly.Count > 0)
            {
                yield return new XComment(" Search \"" + searchText.Replace("--", "- -") + "\" also checks " + string.Join(", ", gridOnly) + " in the grid only (calculated by this tool, so not part of the query) ");
            }

            yield return new XElement("filter", new XAttribute("type", "or"), conditions);
        }

        /// <summary>
        /// The stored values whose displayed value (as the grid currently shows it) matches <paramref name="shownMatches"/>:
        /// for switchable columns from the rows' stored/friendly pairs, for choice and Yes/No columns from their option
        /// labels. Null for plain text columns, where the shown value is the stored value.
        /// </summary>
        private IEnumerable<string> StoredValuesWhere(DataTable table, DataGridViewColumn column, Func<string, bool> shownMatches)
        {
            if (HasFriendlyValues(column))
            {
                return table.Rows.Cast<DataRow>()
                    .Where(row => shownMatches(CellText(row, column.DataPropertyName)))
                    .Select(row => CellText(row, column.Name))
                    .Where(value => value.Length > 0);
            }

            if (table.Columns[column.Name].ExtendedProperties[ApprovalColumnValues.OptionValuesProperty] is IDictionary<string, string> options)
            {
                return options.Where(option => shownMatches(option.Key)).Select(option => option.Value);
            }

            return null;
        }

        private static XElement FetchCondition(string attribute, string op, string value)
        {
            var condition = new XElement("condition", new XAttribute("attribute", attribute), new XAttribute("operator", op));
            if (value != null)
            {
                condition.Add(new XAttribute("value", value));
            }

            return condition;
        }

        /// <summary>Escapes text for a FetchXML "like" value: the SQL wildcards %, _ and [ are wrapped in brackets.</summary>
        private static string EscapeFetchLike(string text)
        {
            return text.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
        }

        private static string Caption(DataTable table, DataGridViewColumn column)
        {
            return table.Columns.Contains(column.Name) ? table.Columns[column.Name].Caption : column.Name;
        }

        private void ApplyFilters()
        {
            if (ApprovalGridView.DataSource is DataTable table)
            {
                table.DefaultView.RowFilter = FilterExpression(exceptColumn: null);
                ApprovalGridView.InvalidateCell(-1, -1);
                RaisePreviewIfChanged();
            }
        }

        /// <summary>
        /// The DataView filter for the search text and every column filter except <paramref name="exceptColumn"/>: the values
        /// of one column are OR'd and everything else is AND'd, e.g. ([statuscode__friendly] = 'Pending' OR
        /// [statuscode__friendly] = 'Completed') AND ([msdyn_flow_approval_priority__friendly] = 'Urgent').
        /// </summary>
        private string FilterExpression(string exceptColumn)
        {
            var clauses = filters
                .Where(filter => filter.Key != exceptColumn && ApprovalGridView.Columns.Contains(filter.Key))
                .Select(filter =>
                {
                    var property = Bracketed(ApprovalGridView.Columns[filter.Key].DataPropertyName);
                    return filter.Value.Count == 0
                        ? "(1 = 0)"
                        : "(" + string.Join(" OR ", filter.Value.Select(value => value.Length == 0
                            ? property + " IS NULL OR " + property + " = ''"
                            : property + " = '" + value.Replace("'", "''") + "'")) + ")";
                })
                .ToList();

            var searched = DataColumns().Where(column => column.Visible && column.ValueType != typeof(Guid)).ToList();
            if (searchText.Length > 0 && searched.Count > 0)
            {
                var pattern = "'%" + EscapeLikeText(searchText) + "%'";
                clauses.Add("(" + string.Join(" OR ", searched.Select(column => Bracketed(column.DataPropertyName) + " LIKE " + pattern)) + ")");
            }

            return string.Join(" AND ", clauses);
        }

        private static string Bracketed(string columnName)
        {
            return "[" + columnName.Replace("]", "\\]") + "]";
        }

        /// <summary>Escapes text for a DataView LIKE pattern: wildcards and brackets are wrapped in brackets, quotes doubled.</summary>
        private static string EscapeLikeText(string text)
        {
            var escaped = new System.Text.StringBuilder();
            foreach (var character in text)
            {
                switch (character)
                {
                    case '*':
                    case '%':
                    case '[':
                    case ']':
                        escaped.Append('[').Append(character).Append(']');
                        break;
                    case '\'':
                        escaped.Append("''");
                        break;
                    default:
                        escaped.Append(character);
                        break;
                }
            }

            return escaped.ToString();
        }

        /// <summary>
        /// The values to offer in a column's filter menu: the distinct values (as displayed) of the rows that pass every
        /// other column's filter, sorted, with "" for empty cells.
        /// </summary>
        private List<string> AvailableFilterValues(DataGridViewColumn column)
        {
            if (!(ApprovalGridView.DataSource is DataTable table))
            {
                return new List<string>();
            }

            return table.Select(FilterExpression(exceptColumn: column.Name))
                .Select(row => CellText(row, column.DataPropertyName))
                .Distinct()
                .OrderBy(value => value.Length == 0 ? 0 : 1)
                .ThenBy(value => value, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static string CellText(DataRow row, string property)
        {
            var value = row[property];
            return value == DBNull.Value || value == null ? ColumnFilterPopup.BlankValue : value.ToString();
        }

        /// <summary>
        /// Before a column switches between friendly and stored values, converts its filter to the other set of values
        /// (each kept value becomes the corresponding value of the same rows).
        /// </summary>
        private void TranslateFilter(DataGridViewColumn column, string fromProperty, string toProperty)
        {
            if (!filters.TryGetValue(column.Name, out var kept) || !(ApprovalGridView.DataSource is DataTable table))
            {
                return;
            }

            filters[column.Name] = new HashSet<string>(table.Rows.Cast<DataRow>()
                .Where(row => kept.Contains(CellText(row, fromProperty)))
                .Select(row => CellText(row, toProperty)));
        }

        /// <summary>Opens the filter menu under a column's header.</summary>
        private void ShowFilterMenu(DataGridViewColumn column)
        {
            var popup = new ColumnFilterPopup(AvailableFilterValues(column), filters.TryGetValue(column.Name, out var kept) ? kept : null);
            var host = new ToolStripControlHost(popup) { AutoSize = false, Size = popup.Size, Margin = Padding.Empty, Padding = Padding.Empty };
            var menu = new ToolStripDropDown { Padding = Padding.Empty };
            menu.Items.Add(host);

            popup.Applied += (sender, values) =>
            {
                SetColumnFilter(column.Name, values);
                menu.Close();
            };
            popup.Cleared += (sender, e) =>
            {
                SetColumnFilter(column.Name, null);
                menu.Close();
            };
            menu.Closed += (sender, e) => BeginInvoke(new Action(menu.Dispose));

            var header = ApprovalGridView.GetCellDisplayRectangle(column.Index, -1, false);
            menu.Show(ApprovalGridView, new Point(Math.Max(0, header.Left), header.Bottom));
            popup.FocusSearch();
        }

        private static Rectangle FilterIconBounds(DataGridViewColumn column, Rectangle cellBounds)
        {
            var right = cellBounds.Right - HeaderHorizontalPadding - SortGlyphSpace;
            return new Rectangle(
                right - FilterIconSize,
                cellBounds.Y + (cellBounds.Height - FilterIconSize) / 2,
                FilterIconSize,
                FilterIconSize);
        }

        private void DrawFilterIcon(Graphics graphics, DataGridViewColumn column, Rectangle cellBounds, Color foreColor)
        {
            var bounds = FilterIconBounds(column, cellBounds);
            var filtered = filters.ContainsKey(column.Name);
            var color = filtered ? SystemColors.Highlight : foreColor;
            var icon = filtered
                ? filterIconActive ?? (filterIconActive = SymbolIcons.Create(SymbolIcons.Filter, FilterIconSize, color))
                : filterIcon ?? (filterIcon = SymbolIcons.Create(SymbolIcons.Filter, FilterIconSize, color));

            if (icon != null)
            {
                graphics.DrawImage(icon, bounds);
            }
            else
            {
                TextRenderer.DrawText(graphics, "▾", column.HeaderCell.InheritedStyle.Font, bounds, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}

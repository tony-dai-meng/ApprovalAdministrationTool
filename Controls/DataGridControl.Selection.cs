using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using ApprovalAdministrationTool.Models;

namespace ApprovalAdministrationTool.Controls
{
    /// <summary>
    /// The tick-box column for choosing several approvals. Ticks are kept by approval ID, so they survive sorting,
    /// filtering and reloads (approvals that no longer load are dropped). Clicking the column's header ticks or clears
    /// every row shown; Space ticks the current row.
    /// </summary>
    public partial class DataGridControl
    {
        private const string SelectColumnName = "select";
        private const int SelectColumnWidth = 28;

        private readonly HashSet<Guid> checkedIds = new HashSet<Guid>();
        private Guid? lastPreviewId;
        private bool previewCheckPending;

        /// <summary>Raised when the ticked approvals change.</summary>
        public event EventHandler CheckedApprovalsChanged;

        /// <summary>
        /// Raised when the approval to preview changes: the first ticked approval in the grid's order, or, with nothing
        /// ticked, the current row's (see <see cref="PreviewApprovalId"/>).
        /// </summary>
        public event EventHandler PreviewApprovalChanged;

        /// <summary>The ticked approvals: those shown, in the grid's order, then any hidden by filters.</summary>
        public IReadOnlyList<Guid> CheckedApprovalIds
        {
            get
            {
                var shown = RowIds().Where(checkedIds.Contains).ToList();
                return shown.Concat(checkedIds.Except(shown)).ToList();
            }
        }

        /// <summary>The approval to preview: the first ticked one shown, else the current row's; null if neither.</summary>
        public Guid? PreviewApprovalId
        {
            get
            {
                foreach (var id in RowIds())
                {
                    if (checkedIds.Contains(id))
                    {
                        return id;
                    }
                }

                return ApprovalGridView.CurrentRow == null ? null : RowId(ApprovalGridView.CurrentRow);
            }
        }

        /// <summary>The loaded approval with this ID (all its columns, including hidden ones), or null.</summary>
        public DataRow FindApproval(Guid id)
        {
            return ApprovalGridView.DataSource is DataTable table
                ? table.AsEnumerable().FirstOrDefault(row => row[ApprovalColumns.Id] is Guid rowId && rowId == id)
                : null;
        }

        /// <summary>Adds the tick-box column (first, frozen) and drops ticks for approvals that are no longer loaded.</summary>
        private void SetUpSelectColumn(DataTable approvals)
        {
            if (!ApprovalGridView.Columns.Contains(SelectColumnName))
            {
                ApprovalGridView.Columns.Insert(0, new DataGridViewCheckBoxColumn
                {
                    Name = SelectColumnName,
                    HeaderText = string.Empty,
                    ToolTipText = "Tick approvals to act on several at once. Click here to tick or clear every row shown.",
                    Width = SelectColumnWidth,
                    Resizable = DataGridViewTriState.False,
                    SortMode = DataGridViewColumnSortMode.NotSortable,
                    ReadOnly = true, // ticks are kept in checkedIds and toggled by the click handlers below
                });
            }

            var select = ApprovalGridView.Columns[SelectColumnName];
            select.DisplayIndex = 0;
            select.Frozen = true;

            var loaded = new HashSet<Guid>(approvals.AsEnumerable()
                .Where(row => row[ApprovalColumns.Id] is Guid)
                .Select(row => (Guid)row[ApprovalColumns.Id]));
            if (checkedIds.RemoveWhere(id => !loaded.Contains(id)) > 0)
            {
                CheckedApprovalsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private static Guid? RowId(DataGridViewRow row)
        {
            try
            {
                return row?.DataBoundItem is DataRowView view && view[ApprovalColumns.Id] is Guid id ? id : (Guid?)null;
            }
            catch (IndexOutOfRangeException)
            {
                return null; // the row is being removed by a filter or rebind
            }
        }

        /// <summary>The IDs of the rows shown, in the grid's order.</summary>
        private IEnumerable<Guid> RowIds()
        {
            return ApprovalGridView.Rows.Cast<DataGridViewRow>().Select(RowId).Where(id => id.HasValue).Select(id => id.Value);
        }

        private void ApprovalGridView_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && ApprovalGridView.Columns[e.ColumnIndex].Name == SelectColumnName)
            {
                var id = RowId(ApprovalGridView.Rows[e.RowIndex]);
                e.Value = id.HasValue && checkedIds.Contains(id.Value);
                e.FormattingApplied = true;
            }
        }

        private void ApprovalGridView_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.ColumnIndex < 0 || ApprovalGridView.Columns[e.ColumnIndex].Name != SelectColumnName)
            {
                return;
            }

            if (e.RowIndex == -1)
            {
                ToggleAllShown();
            }
            else
            {
                ToggleRow(ApprovalGridView.Rows[e.RowIndex]);
            }
        }

        private void ApprovalGridView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && e.Modifiers == Keys.None && ApprovalGridView.CurrentRow != null)
            {
                ToggleRow(ApprovalGridView.CurrentRow);
                e.Handled = true;
            }
        }

        private void ToggleRow(DataGridViewRow row)
        {
            var id = RowId(row);
            if (!id.HasValue)
            {
                return;
            }

            if (!checkedIds.Remove(id.Value))
            {
                checkedIds.Add(id.Value);
            }

            ApprovalGridView.InvalidateRow(row.Index);
            OnTicksChanged();
        }

        /// <summary>Ticks every row shown, or clears them if they are all ticked already.</summary>
        private void ToggleAllShown()
        {
            var shown = RowIds().ToList();
            if (shown.Count == 0)
            {
                return;
            }

            if (shown.All(checkedIds.Contains))
            {
                checkedIds.ExceptWith(shown);
            }
            else
            {
                checkedIds.UnionWith(shown);
            }

            ApprovalGridView.Invalidate();
            OnTicksChanged();
        }

        private void OnTicksChanged()
        {
            CheckedApprovalsChanged?.Invoke(this, EventArgs.Empty);
            RaisePreviewIfChanged();
        }

        /// <summary>
        /// Raises <see cref="PreviewApprovalChanged"/> if the approval to preview is now a different one. Checked once the
        /// grid has settled: while a filter, sort or rebind is being applied its rows can be out of step with the data.
        /// </summary>
        private void RaisePreviewIfChanged()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            if (previewCheckPending)
            {
                return;
            }

            previewCheckPending = true;
            BeginInvoke(new Action(() =>
            {
                previewCheckPending = false;
                CheckPreview();
            }));
        }

        private void CheckPreview()
        {
            var preview = PreviewApprovalId;
            if (preview != lastPreviewId)
            {
                lastPreviewId = preview;
                PreviewApprovalChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Draws the tick-box column's header as a tick box: ticked when every row shown is ticked.</summary>
        private bool PaintSelectHeader(DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex != -1 || ApprovalGridView.Columns[e.ColumnIndex].Name != SelectColumnName)
            {
                return false;
            }

            e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
            var shown = RowIds().ToList();
            var state = shown.Count > 0 && shown.All(checkedIds.Contains)
                ? CheckBoxState.CheckedNormal
                : shown.Any(checkedIds.Contains) ? CheckBoxState.MixedNormal : CheckBoxState.UncheckedNormal;
            var size = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
            var location = new Point(
                e.CellBounds.X + (e.CellBounds.Width - size.Width) / 2,
                e.CellBounds.Y + (e.CellBounds.Height - size.Height) / 2);
            CheckBoxRenderer.DrawCheckBox(e.Graphics, location, state);
            e.Handled = true;
            return true;
        }
    }
}

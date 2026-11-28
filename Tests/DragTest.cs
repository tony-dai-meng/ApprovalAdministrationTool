using System;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

// Checks there is always empty space after the last data column to drag its border into:
// with columns that fit, with columns that overflow (scrolled fully right), and after the last column was widened.
static class DragTest
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }

    [STAThread]
    static int Main(string[] args)
    {
        var plugin = Assembly.LoadFrom(args[0]);
        var gridType = plugin.GetType("ApprovalAdministrationTool.Controls.DataGridControl", true);

        foreach (var formWidth in new[] { 1100, 450 })
        {
            var t = new DataTable();
            t.Columns.Add("msdyn_flow_approvalid", typeof(Guid)).Caption = "Approval";
            t.Columns.Add("msdyn_flow_approval_title").Caption = "Title";
            t.Columns.Add("msdyn_flow_approval_modeltype").Caption = "Approval Model Type";
            t.Columns.Add("msdyn_flow_approval_itemlink").Caption = "Item Link";
            t.Columns.Add("msdyn_flow_approval_itemlinkdescription").Caption = "Item Link Description";
            t.Rows.Add(Guid.NewGuid(), "Hello hello", "msdyn_flow_awaitallapprovalmodel", "https://google.com", "Example Link");

            var form = new Form { Width = formWidth, Height = 220, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false };
            var control = (Control)Activator.CreateInstance(gridType);
            control.Dock = DockStyle.Fill;
            form.Controls.Add(control);
            form.Show();
            var showApprovals = gridType.GetMethod("ShowApprovals");
            showApprovals.Invoke(control, new object[] { t });
            Application.DoEvents();
            var grid = (DataGridView)control.Controls[0];

            Func<DataGridViewColumn> lastData = () => grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible && !string.IsNullOrEmpty(c.DataPropertyName)).OrderBy(c => c.DisplayIndex).Last();
            Func<int> gapAfterLast = () =>
            {
                grid.HorizontalScrollingOffset = Math.Max(0, grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible) + grid.RowHeadersWidth - grid.DisplayRectangle.Width + 50);
                Application.DoEvents();
                var rect = grid.GetColumnDisplayRectangle(lastData().Index, false);
                var dataRight = grid.DisplayRectangle.Right - (grid.Controls.OfType<VScrollBar>().Any(s => s.Visible) ? SystemInformation.VerticalScrollBarWidth : 0);
                return dataRight - rect.Right;
            };

            var overflow = grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible) + grid.RowHeadersWidth > grid.DisplayRectangle.Width;
            var label = overflow ? "columns overflow" : "columns fit";
            var spacer = grid.Columns["spacer"];
            Check(spacer != null && spacer.DisplayIndex == grid.Columns.Count - 1, label + ": spacer column is last");
            Check(gapAfterLast() >= 80, label + ": empty space after the last column, scrolled fully right (" + gapAfterLast() + "px)");

            lastData().Width += 300;   // as if the user dragged it wider
            Application.DoEvents();
            Check(spacer.Width >= 80 && gapAfterLast() >= 80, label + ": after widening the last column by 300px there is still space to keep dragging (" + gapAfterLast() + "px, spacer " + spacer.Width + "px)");

            showApprovals.Invoke(control, new object[] { t });   // refresh
            Application.DoEvents();
            Check(grid.Columns.Cast<DataGridViewColumn>().Count(c => c.Name == "spacer") == 1 && grid.Columns["spacer"].DisplayIndex == grid.Columns.Count - 1, label + ": refresh keeps exactly one spacer, still last");

            var bmp = new Bitmap(grid.Width, grid.Height);
            grid.DrawToBitmap(bmp, new Rectangle(Point.Empty, grid.Size));
            bmp.Save(args[1] + "\\spacer_" + formWidth + ".png");
            form.Close();
        }

        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }
}

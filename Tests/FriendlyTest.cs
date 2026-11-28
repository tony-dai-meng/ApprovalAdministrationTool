using System;
using System.Collections;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

// End to end: ApprovalService (against FakeService) -> DataGridControl. Checks friendly values by default, the per-column
// switch icons, sorting, widths and refresh. Header clicks are delivered straight to the control's header-click handler with
// the coordinates a real click on the icon / elsewhere in the header would have.
static class FriendlyTest
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }

    [STAThread]
    static int Main(string[] args)
    {
        var plugin = Assembly.LoadFrom(args[0]);
        var all = ((IEnumerable)plugin.GetType("ApprovalAdministrationTool.Models.ApprovalColumns", true).GetField("All").GetValue(null)).Cast<string>().ToArray();
        var serviceType = plugin.GetType("ApprovalAdministrationTool.Services.ApprovalService", true);
        Func<DataTable> load = () =>
        {
            var service = Activator.CreateInstance(serviceType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { new FakeService { ToolColumns = all } }, null);
            return FakeService.Approvals(serviceType.GetMethod("GetApprovals").Invoke(service, new object[] { null }));
        };

        var gridType = plugin.GetType("ApprovalAdministrationTool.Controls.DataGridControl", true);
        var form = new Form { Width = 1600, Height = 300, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false };
        var control = (Control)Activator.CreateInstance(gridType);
        control.Dock = DockStyle.Fill;
        form.Controls.Add(control);
        form.Show();
        var show = gridType.GetMethod("ShowApprovals");
        show.Invoke(control, new object[] { load() });
        Application.DoEvents();
        var grid = (DataGridView)control.Controls[0];

        Func<string, string, string> value = (title, column) => grid.Rows.Cast<DataGridViewRow>()
            .First(r => (string)r.Cells["msdyn_flow_approval_title"].Value == title).Cells[column].FormattedValue as string;
        Func<string> order = () => string.Join(" | ", grid.Rows.Cast<DataGridViewRow>().Select(r => (string)r.Cells["msdyn_flow_approval_title"].Value));
        var handler = gridType.GetMethod("ApprovalGridView_ColumnHeaderMouseClick", BindingFlags.NonPublic | BindingFlags.Instance);
        Action<string, bool> click = (column, onIcon) =>
        {
            var c = grid.Columns[column];
            // switch icon centre: right padding 4, sort space 16, filter icon space 20, then the 16px switch icon
            int x = onIcon ? c.Width - 4 - 16 - 20 - 8 : 10, y = grid.ColumnHeadersHeight / 2;
            handler.Invoke(control, new object[] { grid, new DataGridViewCellMouseEventArgs(c.Index, -1, x, y, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0)) });
            Application.DoEvents();
        };

        const string Model = "msdyn_flow_approval_modeltype", Status = "statuscode", RequestType = "msdyn_flow_approval_requesttype", Result = "msdyn_flow_approval_result";
        Check(value("Expense claim", Model) == "Approve/Reject – First to respond", "Approval Model Type friendly by default");
        Check(value("Expense claim", Status) == "Pending" && value("Budget", Status) == "Completed", "Status Reason friendly by default (Pending, Completed)");
        Check(value("Budget", RequestType) == "Basic", "Request Type friendly by default");
        Check(value("Leave request", Result) == "Reject", "Result column shows the outcome");
        Check(value("Leave request", "msdyn_flow_approval_priority") == "Important", "Priority shows its choice label (" + value("Leave request", "msdyn_flow_approval_priority") + ")");
        Check(grid.Columns[Status].HeaderText == "Status Reason" && grid.Columns[Result].HeaderText == "Result", "new columns use their Dataverse display names");
        Check(new[] { Model, Status, RequestType }.All(c => grid.Columns[c].ToolTipText.Contains("Click the ⇄ icon")), "switchable headers' tooltips explain the icon");
        Check(!grid.Columns[Result].ToolTipText.Contains("⇄") && grid.Columns[Result].ToolTipText.Contains("final outcome"), "Result has its definition and no switch");
        Check(grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Name.EndsWith("__friendly")).All(c => !c.Visible), "friendly-values columns are hidden");
        Check(!grid.Columns.Contains("msdyn_flow_approval_stage"), "the Stage column is not shown");

        var widths = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).ToDictionary(c => c.Name, c => c.Width);
        click(Model, true);
        Check(value("Expense claim", Model) == "msdyn_flow_basicapprovalmodel", "model type icon shows stored table names");
        Check(value("Expense claim", Status) == "Pending", "...and leaves Status Reason on friendly values (per column)");
        click(Status, true);
        Check(value("Expense claim", Status) == "192350001" && value("Budget", Status) == "192350004", "Status Reason icon shows stored numbers");
        Check(grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).All(c => c.Width == widths[c.Name]), "switching keeps every column width");

        click(Status, true);   // back to friendly
        click(Status, false);  // sort by Status Reason label
        Check(grid.SortedColumn == grid.Columns[Status] && grid.SortOrder == SortOrder.Ascending, "header click sorts Status Reason ascending");
        var byLabel = order();
        Check(byLabel == "Leave request | Budget | Expense claim", "sorted by label: Canceled, Completed, Pending (" + byLabel + ")");
        click(Status, true);
        Check(order() == "Expense claim | Budget | Leave request" && grid.SortOrder == SortOrder.Ascending, "switching re-sorts by stored number: 192350001, ...4, ...6 (" + order() + ")");
        click(Status, false);
        Check(grid.SortOrder == SortOrder.Descending, "second header click sorts descending");

        var columnCount = grid.Columns.Count;
        show.Invoke(control, new object[] { load() });   // Refresh
        Application.DoEvents();
        Check(grid.Columns.Count == columnCount && grid.Columns.Cast<DataGridViewColumn>().Select(c => c.Name).Distinct().Count() == columnCount,
            "refresh leaves no duplicate columns (" + grid.Columns.Count + " columns, was " + columnCount + ")");
        show.Invoke(control, new object[] { load() });   // and again
        Application.DoEvents();
        Check(grid.Columns.Count == columnCount, "a second refresh still has " + columnCount + " columns");
        Check(grid.Columns["spacer"].DisplayIndex == grid.Columns.Count - 1, "spacer is still last after refreshes");
        Check(value("Expense claim", Status) == "192350001" && value("Expense claim", Model) == "msdyn_flow_basicapprovalmodel" && value("Budget", RequestType) == "Basic",
            "refresh keeps each column's choice (Status and Model stored, Request Type friendly)");

        var bmp = new Bitmap(grid.Width, grid.Height);
        grid.DrawToBitmap(bmp, new Rectangle(Point.Empty, grid.Size));
        bmp.Save(args[1] + "\\friendly_header.png");

        click(Model, true); click(Status, true);
        Check(value("Expense claim", Model) == "Approve/Reject – First to respond" && value("Expense claim", Status) == "Pending", "icons switch both back to friendly values");

        form.Close();
        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }
}

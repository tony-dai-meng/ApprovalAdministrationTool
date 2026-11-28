using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

// End to end (ApprovalService against FakeService -> DataGridControl): column filters (AND across columns, OR within one),
// filter menu values, blanks, value-switch translation, refresh, search across columns, and the filter menu popup itself.
static class FilterTest
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }

    [STAThread]
    static int Main(string[] args)
    {
        var plugin = Assembly.LoadFrom(args[0]);
        var all = ((IEnumerable)plugin.GetType("ApprovalAdministrationTool.Models.ApprovalColumns", true).GetField("All").GetValue(null)).Cast<string>().ToArray();
        var serviceType = plugin.GetType("ApprovalAdministrationTool.Services.ApprovalService", true);
        Func<DataTable> load = () => FakeService.Approvals(serviceType.GetMethod("GetApprovals").Invoke(
            Activator.CreateInstance(serviceType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { new FakeService { ToolColumns = all } }, null), new object[] { null }));

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
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        Action<string, string[]> filter = (column, values) => { gridType.GetMethod("SetColumnFilter", flags).Invoke(control, new object[] { column, values }); Application.DoEvents(); };
        Func<string, List<string>> available = column => (List<string>)gridType.GetMethod("AvailableFilterValues", flags).Invoke(control, new object[] { grid.Columns[column] });
        Action<string> search = text => { gridType.GetMethod("SetSearchText").Invoke(control, new object[] { text }); Application.DoEvents(); };
        Func<string> shown = () => string.Join(" | ", grid.Rows.Cast<DataGridViewRow>().Select(r => (string)r.Cells["msdyn_flow_approval_title"].Value).OrderBy(t => t));
        const string Status = "statuscode", Model = "msdyn_flow_approval_modeltype", Result = "msdyn_flow_approval_result";

        Check(shown() == "Budget | Expense claim | Leave request", "no filter: all 3 approvals");
        Check(string.Join(",", available(Status)) == "Canceled,Completed,Pending", "Status Reason menu lists its displayed values (" + string.Join(",", available(Status)) + ")");

        filter(Status, new[] { "Pending", "Completed" });
        Check(shown() == "Budget | Expense claim", "Status Reason = Pending OR Completed (" + shown() + ")");
        Check(available(Model).Count == 2 && !available(Model).Contains("msdyn_flow_somefuturemodel"), "Model Type menu only lists values from rows passing the other filters (" + string.Join(", ", available(Model)) + ")");
        Check(available(Status).Count == 3, "a column's own menu still lists all its values");

        filter(Model, new[] { "Approve/Reject – First to respond" });
        Check(shown() == "Expense claim", "AND across columns: Status in (Pending, Completed) AND Model Type = First to respond (" + shown() + ")");

        filter(Status, null);
        Check(shown() == "Expense claim", "clearing Status Reason leaves the Model Type filter");
        filter(Model, null);

        filter(Result, new[] { "" });
        Check(shown() == "Budget | Expense claim", "(Blanks) keeps rows with an empty Result (" + shown() + ")");
        Check(available(Result).First() == "", "(Blanks) is listed first in the Result menu");
        filter(Result, null);

        filter(Status, new[] { "Pending" });
        var toggle = gridType.GetMethod("ToggleFriendlyValues", flags);
        toggle.Invoke(control, new object[] { grid.Columns[Status] }); Application.DoEvents();
        Check(shown() == "Expense claim" && available(Status).Contains("192350001"), "switching Status Reason to stored values keeps the filter (now 192350001)");
        toggle.Invoke(control, new object[] { grid.Columns[Status] }); Application.DoEvents();
        Check(shown() == "Expense claim", "...and back to friendly values");

        show.Invoke(control, new object[] { load() }); Application.DoEvents();
        Check(shown() == "Expense claim", "Refresh keeps the filters");
        filter(Status, null);

        search("budget");
        Check(shown() == "Budget", "search 'budget' matches the Title, ignoring case");
        search("sam");
        Check(shown() == "Expense claim", "search 'sam' matches Currently With (Alex Doe; Sam Lee)");
        search("canceled");
        Check(shown() == "Leave request", "search matches displayed choice labels (Canceled)");
        filter(Status, new[] { "Pending", "Canceled" });
        search("e");
        Check(shown() == "Expense claim | Leave request", "search AND column filter combine");
        search("50% [x] it's *");
        Check(shown() == "", "search text with wildcards, brackets and quotes is matched literally (no error)");
        search("");
        Check(shown() == "Expense claim | Leave request", "clearing the search keeps the column filter");
        filter(Status, null);

        var bmp = new Bitmap(grid.Width, grid.Height);
        filter(Model, new[] { "Approve/Reject – First to respond" });
        grid.DrawToBitmap(bmp, new Rectangle(Point.Empty, grid.Size));
        bmp.Save(args[1] + "\\filter_header.png");
        filter(Model, null);

        // The filter menu popup.
        var popupType = plugin.GetType("ApprovalAdministrationTool.Controls.ColumnFilterPopup", true);
        var popup = (UserControl)Activator.CreateInstance(popupType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new object[] { new[] { "", "Canceled", "Completed", "Pending" }, null }, null);
        IReadOnlyCollection<string> applied = new[] { "unset" }; var cleared = false;
        popupType.GetEvent("Applied").AddEventHandler(popup, new EventHandler<IReadOnlyCollection<string>>((s, v) => applied = v));
        popupType.GetEvent("Cleared").AddEventHandler(popup, new EventHandler((s, e) => cleared = true));
        var popupForm = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false, ClientSize = popup.Size };
        popupForm.Controls.Add(popup); popupForm.Show(); Application.DoEvents();
        var list = popup.Controls.OfType<CheckedListBox>().Single();
        var ok = popup.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(b => b.Text == "OK");
        var clear = popup.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(b => b.Text == "Clear filter");
        var searchBox = popup.Controls.OfType<TextBox>().Single();

        Check(list.Items.Count == 5 && (string)list.Items[0] == "(Select all)" && (string)list.Items[1] == "(Blanks)", "menu lists (Select all), (Blanks) and the values");
        Check(Enumerable.Range(0, 5).All(list.GetItemChecked), "no filter: everything ticked");
        ok.PerformClick(); Application.DoEvents();
        Check(applied == null, "OK with everything ticked means no filter");

        list.SetItemChecked(2, false); Application.DoEvents();   // untick Canceled
        Check(list.GetItemCheckState(0) == CheckState.Indeterminate, "(Select all) shows partly ticked");
        ok.PerformClick(); Application.DoEvents();
        Check(applied != null && string.Join(",", applied.OrderBy(v => v)) == ",Completed,Pending", "OK returns the ticked values (" + (applied == null ? "null" : string.Join(",", applied)) + ")");

        list.SetItemCheckState(0, CheckState.Unchecked); Application.DoEvents();   // click on a partly ticked Select all -> ticks everything
        Check(Enumerable.Range(1, 4).All(list.GetItemChecked), "clicking a partly ticked (Select all) ticks everything");
        list.SetItemChecked(0, false); Application.DoEvents();
        Check(Enumerable.Range(1, 4).All(i => !list.GetItemChecked(i)) && !ok.Enabled, "unticking (Select all) unticks everything and disables OK");

        searchBox.Text = "comp"; Application.DoEvents();
        Check(list.Items.Count == 2 && (string)list.Items[1] == "Completed", "search narrows the list (" + list.Items.Count + " items)");
        list.SetItemChecked(0, true); Application.DoEvents();
        searchBox.Text = ""; Application.DoEvents();
        ok.PerformClick(); Application.DoEvents();
        Check(applied != null && string.Join(",", applied) == "Completed", "Select all while searching ticks only the listed values");

        clear.PerformClick(); Application.DoEvents();
        Check(cleared, "Clear filter raises Cleared");

        popupForm.Close();
        form.Close();
        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }
}

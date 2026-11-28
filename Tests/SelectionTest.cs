using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

static class SelectionTest
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

    [STAThread]
    static int Main(string[] args)
    {
        var plugin = Assembly.LoadFrom(args[0]);
        var cols = plugin.GetType("ApprovalAdministrationTool.Models.ApprovalColumns", true);
        var all = ((IEnumerable)cols.GetField("All").GetValue(null)).Cast<string>().ToArray();
        var details = (string)cols.GetField("Details").GetValue(null);
        var serviceType = plugin.GetType("ApprovalAdministrationTool.Services.ApprovalService", true);
        Func<DataTable> load = () =>
        {
            var result = serviceType.GetMethod("GetApprovals").Invoke(
                Activator.CreateInstance(serviceType, F, null, new object[] { new FakeService { ToolColumns = all } }, null), new object[] { null });
            var t = FakeService.Approvals(result);
            if (!t.Columns.Contains(details)) t.Columns.Add(details, typeof(string));
            foreach (DataRow r in t.Rows) r["statuscode"] = "192350001"; foreach (DataRow r in t.Rows) r[details] = "**Details for** " + r["msdyn_flow_approval_title"];
            return t;
        };

        var gridType = plugin.GetType("ApprovalAdministrationTool.Controls.DataGridControl", true);
        var form = new Form { Width = 1400, Height = 400, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false };
        var grid = (Control)Activator.CreateInstance(gridType);
        grid.Dock = DockStyle.Fill; form.Controls.Add(grid); form.Show();
        var view = (DataGridView)grid.Controls[0];
        var table = load();
        gridType.GetMethod("ShowApprovals").Invoke(grid, new object[] { table });
        Application.DoEvents();

        int tickEvents = 0, previewEvents = 0;
        gridType.GetEvent("CheckedApprovalsChanged").AddEventHandler(grid, new EventHandler((s, e) => tickEvents++));
        gridType.GetEvent("PreviewApprovalChanged").AddEventHandler(grid, new EventHandler((s, e) => previewEvents++));
        Func<List<Guid>> ticked = () => ((IEnumerable<Guid>)gridType.GetProperty("CheckedApprovalIds").GetValue(grid)).ToList();
        Func<Guid?> preview = () => (Guid?)gridType.GetProperty("PreviewApprovalId").GetValue(grid);
        Func<int, Guid> idAt = i => (Guid)((DataRowView)view.Rows[i].DataBoundItem)["msdyn_flow_approvalid"];
        var select = view.Columns["select"];
        Action<int> click = row =>
        {
            gridType.GetMethod("ApprovalGridView_CellMouseClick", F).Invoke(grid, new object[] { view, new DataGridViewCellMouseEventArgs(select.Index, row, 5, 5, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0)) });
            Application.DoEvents();
        };

        Check(select != null && select.DisplayIndex == 0 && select.Frozen && select.Visible, "tick-box column is first, frozen and visible");
        Check(!view.Columns[details].Visible, "details column is loaded but hidden");
        Check(view.Rows.Count >= 3, "fake data has at least 3 rows: " + view.Rows.Count);

        view.CurrentCell = view.Rows[1].Cells["msdyn_flow_approval_title"]; Application.DoEvents();
        Check(preview() == idAt(1), "nothing ticked: preview is the current row");

        click(2);
        Check(ticked().SequenceEqual(new[] { idAt(2) }) && tickEvents == 1, "ticking row 3 ticks it and raises CheckedApprovalsChanged");
        Check(preview() == idAt(2), "preview switches to the first ticked approval");
        click(0);
        Check(ticked().SequenceEqual(new[] { idAt(0), idAt(2) }), "ticked approvals come back in grid order");
        Check(preview() == idAt(0), "preview is the first ticked in grid order");
        var fmt = new DataGridViewCellFormattingEventArgs(select.Index, 0, null, typeof(bool), new DataGridViewCellStyle());
        gridType.GetMethod("ApprovalGridView_CellFormatting", F).Invoke(grid, new object[] { view, fmt });
        Check(fmt.Value is bool b && b, "a ticked row's box shows ticked");

        view.Sort(view.Columns["msdyn_flow_approval_title"], System.ComponentModel.ListSortDirection.Descending); Application.DoEvents();
        var stillTicked = new HashSet<Guid>(ticked());
        Check(stillTicked.Count == 2, "ticks survive sorting");
        Check(preview() == Enumerable.Range(0, view.Rows.Count).Select(idAt).First(stillTicked.Contains), "after sorting, preview is the first ticked in the new order");

        click(-1);
        Check(ticked().Count == view.Rows.Count, "header click ticks every row shown");
        click(-1);
        Check(ticked().Count == 0, "second header click clears them");

        click(0);
        var first = idAt(0);
        var title = (string)((DataRowView)view.Rows[0].DataBoundItem)["msdyn_flow_approval_title"];
        gridType.GetMethod("SetColumnFilter", F).Invoke(grid, new object[] { "msdyn_flow_approval_title", new[] { (string)((DataRowView)view.Rows[1].DataBoundItem)["msdyn_flow_approval_title"] } });
        Application.DoEvents();
        Check(ticked().Contains(first), "a ticked row hidden by a filter stays ticked");
        gridType.GetMethod("SetColumnFilter", F).Invoke(grid, new object[] { "msdyn_flow_approval_title", null });

        var reload = load();
        foreach (var r in reload.AsEnumerable().Where(r => (Guid)r["msdyn_flow_approvalid"] == first).ToList()) reload.Rows.Remove(r);
        // fake data gets new IDs each load? keep the original table but drop the ticked row instead
        var keep = table.Copy();
        foreach (var r in keep.AsEnumerable().Where(r => (Guid)r["msdyn_flow_approvalid"] == first).ToList()) keep.Rows.Remove(r);
        tickEvents = 0;
        gridType.GetMethod("ShowApprovals").Invoke(grid, new object[] { keep }); Application.DoEvents();
        Check(ticked().Count == 0 && tickEvents == 1, "reload drops ticks for approvals no longer loaded");
        var row = (DataRow)gridType.GetMethod("FindApproval").Invoke(grid, new object[] { idAt(0) });
        Check(row != null && ((string)row[details]).StartsWith("**Details for**"), "FindApproval returns the row with its hidden details");
        form.Close();

        // ---- HTML pane
        var webType = plugin.GetType("ApprovalAdministrationTool.Controls.WebContentControl", true);
        var webForm = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false, ClientSize = new Size(500, 400) };
        var web = (Control)Activator.CreateInstance(webType); web.Dock = DockStyle.Fill; webForm.Controls.Add(web); webForm.Show();
 var browser = web.Controls.OfType<WebBrowser>().Single(); var loaded = false; browser.DocumentCompleted += (s2, e2) => loaded = true; var deadline0 = DateTime.Now.AddSeconds(10); while (!loaded && DateTime.Now < deadline0) Application.DoEvents(); loaded = false;
        webType.GetMethod("ShowApproval").Invoke(web, new object[] { "Budget <b>", "Requested by Tony", "# Heading\n\n**bold** and [a link](https://example.com)\n\n<script>alert(1)</script>\n\n| a | b |\n|---|---|\n| 1 | 2 |" });

        var deadline = DateTime.Now.AddSeconds(10);
        while (!loaded && DateTime.Now < deadline) Application.DoEvents(); Console.WriteLine("loaded=" + loaded + " doc=" + (browser.Document != null) + " body=" + (browser.Document?.Body != null));
        var html = browser.Document.Body.InnerHtml;
        Check(html.Contains("<STRONG>bold</STRONG>") || html.Contains("<strong>bold</strong>"), "Markdown bold renders as <strong>");
        Check(html.ToLower().Contains("href=\"https://example.com\""), "Markdown link renders as a link");
        Check(html.ToLower().Contains("<table"), "Markdown table renders as a table");
        Check(!html.ToLower().Contains("<script") && html.Contains("&lt;script&gt;"), "raw HTML/script in the details is shown as text, not run");
        Check(html.Contains("Budget &lt;b&gt;") && html.Contains("Requested by Tony"), "title is HTML-encoded and subtitle shown");
        webForm.Close();

        // ---- main control: preview follows the grid
        var main = (Control)Activator.CreateInstance(plugin.GetType("ApprovalAdministrationTool.ApprovalAdminControl", true));
        var mainForm = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false, ClientSize = new Size(1200, 700) };
        main.Dock = DockStyle.Fill; mainForm.Controls.Add(main); mainForm.Show(); Application.DoEvents();
        var mainGrid = (Control)main.GetType().GetField("dataGridControl1", F).GetValue(main);
        var mainWeb = (Control)main.GetType().GetField("webContentControl1", F).GetValue(main);
        var mainActions = (Control)main.GetType().GetField("actionPanel1", F).GetValue(main);
        gridType.GetMethod("ShowApprovals").Invoke(mainGrid, new object[] { table }); Application.DoEvents();
        var mainView = (DataGridView)mainGrid.Controls[0];
        var mainBrowser = mainWeb.Controls.OfType<WebBrowser>().Single();
        var mainLoaded = false; mainBrowser.DocumentCompleted += (s3, e3) => mainLoaded = true; Func<string> mainHtml = () => { var d = DateTime.Now.AddSeconds(10); while (!mainLoaded && DateTime.Now < d) Application.DoEvents(); mainLoaded = false; return mainBrowser.Document?.Body?.InnerHtml ?? "(no document)"; };
        mainHtml(); var t2 = (string)((DataRowView)mainView.Rows[2].DataBoundItem)["msdyn_flow_approval_title"];
        gridType.GetMethod("ApprovalGridView_CellMouseClick", F).Invoke(mainGrid, new object[] { mainView, new DataGridViewCellMouseEventArgs(mainView.Columns["select"].Index, 2, 5, 5, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0)) });
        var h2 = mainHtml();
        Check(h2.Contains("Details for") && h2.Contains(t2), "main control: ticking an approval renders its details in the pane: " + t2);
        Check(mainActions.Visible && mainActions.Dock == DockStyle.Bottom && mainWeb.Dock == DockStyle.Fill, "action panel docked under the HTML pane");
        mainForm.Close();

        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }
}



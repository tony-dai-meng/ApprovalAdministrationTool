using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Linq;

// Grid filters and search -> FetchXML in the query panel.
static class SyncTest
{
    static int failures;
    static object activeFilter(object main) { return main.GetType().GetField("activeFilterXml", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(main); }
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }

    [STAThread]
    static int Main(string[] args)
    {
        var plugin = Assembly.LoadFrom(args[0]);
        var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        var all = ((IEnumerable)plugin.GetType("ApprovalAdministrationTool.Models.ApprovalColumns", true).GetField("All").GetValue(null)).Cast<string>().ToArray();
        var serviceType = plugin.GetType("ApprovalAdministrationTool.Services.ApprovalService", true);
        object lastResult = null;
        Func<DataTable> load = () =>
        {
            lastResult = serviceType.GetMethod("GetApprovals").Invoke(
                Activator.CreateInstance(serviceType, flags, null, new object[] { new FakeService { ToolColumns = all } }, null), new object[] { null });
            return FakeService.Approvals(lastResult);
        };
        load();
        var tableColumns = new HashSet<string>((IEnumerable<string>)lastResult.GetType().GetProperty("TableColumns").GetValue(lastResult));

        // ---- grid -> FetchXML
        var gridType = plugin.GetType("ApprovalAdministrationTool.Controls.DataGridControl", true);
        var form = new Form { Width = 1600, Height = 300, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false };
        var grid = (Control)Activator.CreateInstance(gridType);
        grid.Dock = DockStyle.Fill;
        form.Controls.Add(grid); form.Show();
        gridType.GetMethod("ShowApprovals").Invoke(grid, new object[] { load() });
        Application.DoEvents();
        var changes = 0;
        gridType.GetEvent("FiltersChanged").AddEventHandler(grid, new EventHandler((s, e) => changes++));
        Action<string, string[]> filter = (column, values) => { gridType.GetMethod("SetColumnFilter", flags).Invoke(grid, new object[] { column, values }); Application.DoEvents(); };
        Action<string> search = text => { gridType.GetMethod("SetSearchText").Invoke(grid, new object[] { text }); Application.DoEvents(); };
        Func<List<XNode>> fetch = () => ((IEnumerable<XNode>)gridType.GetMethod("FetchXmlForGridFilters", flags).Invoke(grid, null)).ToList();
        Func<List<XNode>, string> show = nodes => string.Join(" ", nodes.Select(n => n.ToString(SaveOptions.DisableFormatting)));

        filter("statuscode", new[] { "Pending", "Completed" });
        var nodes = fetch();
        Check(changes == 1, "changing a column filter raises FiltersChanged");
        Check(show(nodes) == "<condition attribute=\"statuscode\" operator=\"in\"><value>192350001</value><value>192350004</value></condition>",
            "Status Reason Pending, Completed -> in (192350001, 192350004): " + show(nodes));
        filter("statuscode", null);

        filter("msdyn_flow_approval_allowreassign", new[] { "Yes" });
        Check(show(fetch()) == "<condition attribute=\"msdyn_flow_approval_allowreassign\" operator=\"eq\" value=\"1\" />", "Yes/No: Yes -> eq 1: " + show(fetch()));
        filter("msdyn_flow_approval_allowreassign", null);

        filter("msdyn_flow_approval_result", new[] { "" });
        Check(show(fetch()) == "<condition attribute=\"msdyn_flow_approval_result\" operator=\"null\" />", "(Blanks) -> null: " + show(fetch()));
        filter("msdyn_flow_approval_result", new[] { "", "Reject" });
        Check(show(fetch()) == "<filter type=\"or\"><condition attribute=\"msdyn_flow_approval_result\" operator=\"eq\" value=\"Reject\" /><condition attribute=\"msdyn_flow_approval_result\" operator=\"null\" /></filter>",
            "a value plus (Blanks) -> or(eq, null): " + show(fetch()));
        filter("msdyn_flow_approval_result", null);

        filter("msdyn_flow_approval_title", new[] { "Budget" });
        Check(show(fetch()) == "<condition attribute=\"msdyn_flow_approval_title\" operator=\"eq\" value=\"Budget\" />", "text column -> eq text");
        filter("msdyn_flow_approval_title", null);

        filter("msdyn_flow_approval_modeltype", new[] { "Approve/Reject – First to respond" });
        Check(show(fetch()) == "<condition attribute=\"msdyn_flow_approval_modeltype\" operator=\"eq\" value=\"msdyn_flow_basicapprovalmodel\" />", "friendly approval type -> stored table name");
        filter("msdyn_flow_approval_modeltype", null);

        filter("ownerid", new[] { "Owner of Budget" });
        var ownerCondition = fetch().OfType<XElement>().Single();
        Check((string)ownerCondition.Attribute("attribute") == "ownerid" && Guid.TryParse((string)ownerCondition.Attribute("value"), out _), "person name -> their user ID: " + show(fetch()));
        filter("ownerid", null);

        filter("currentlywith", new[] { "Alex Doe; Sam Lee" });
        Check(fetch().Count == 1 && fetch()[0] is XComment && fetch()[0].ToString().Contains("grid only"), "Currently With filter -> comment only: " + show(fetch()));
        filter("currentlywith", null);

        filter("statuscode", new[] { "Pending" });
        gridType.GetMethod("ToggleFriendlyValues", flags).Invoke(grid, new object[] { ((DataGridView)grid.Controls[0]).Columns["statuscode"] });
        Check(show(fetch()) == "<condition attribute=\"statuscode\" operator=\"eq\" value=\"192350001\" />", "switched to stored values: same condition");
        gridType.GetMethod("ToggleFriendlyValues", flags).Invoke(grid, new object[] { ((DataGridView)grid.Controls[0]).Columns["statuscode"] });
        filter("statuscode", null);

        changes = 0;
        search("pend");
        var searchNodes = fetch();
        var orFilter = searchNodes.OfType<XElement>().Single();
        Check(changes == 1, "changing the search raises FiltersChanged");
        Check((string)orFilter.Attribute("type") == "or" && orFilter.Elements().Any(c => (string)c.Attribute("attribute") == "msdyn_flow_approval_title" && (string)c.Attribute("operator") == "like" && (string)c.Attribute("value") == "%pend%"),
            "search -> or-filter with like '%pend%' on text columns");
        Check(orFilter.Elements().Any(c => (string)c.Attribute("attribute") == "statuscode" && (string)c.Attribute("value") == "192350001"),
            "search also matches choice labels -> stored value (Pending -> 192350001)");
        Check(searchNodes.OfType<XComment>().Any(c => c.Value.Contains("Currently With")), "search notes Currently With is grid-only");
        search("50%_[x]");
        Check(fetch().OfType<XElement>().Single().Elements().Any(c => (string)c.Attribute("value") == "%50[%][_][[]x]%"), "like wildcards in the search text are escaped");
        search("");
        Check(fetch().Count == 0, "no filters, no search -> nothing");
        form.Close();

        // ---- merging into the editor
        var editorType = plugin.GetType("ApprovalAdministrationTool.Controls.QueryEditorControl", true);
        var editor = (Control)Activator.CreateInstance(editorType);
        var editorForm = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false, ClientSize = new Size(800, 400) };
        editor.Dock = DockStyle.Fill; editorForm.Controls.Add(editor); editorForm.Show(); Application.DoEvents();
        editorType.GetMethod("SetTableColumns").Invoke(editor, new object[] { tableColumns });
        var filterXml = editorType.GetProperty("FilterXml");
        var setGrid = editorType.GetMethod("SetGridFilters");
        Func<bool> canRun = () => (bool)editorType.GetProperty("CanRun", flags).GetValue(editor);
        var defaultFilter = (string)plugin.GetType("ApprovalAdministrationTool.Services.ApprovalQuery", true).GetField("DefaultFilter").GetValue(null);
        var statusIn = XElement.Parse("<condition attribute=\"statuscode\" operator=\"in\"><value>192350001</value><value>192350004</value></condition>");

        filterXml.SetValue(editor, defaultFilter);
        setGrid.Invoke(editor, new object[] { new List<XNode> { new XElement(statusIn) } });
        var merged = XElement.Parse((string)filterXml.GetValue(editor));
        Check((string)merged.Attribute("type") == "and" && merged.Nodes().OfType<XComment>().Count() == 2 && merged.Elements().Single().ToString(SaveOptions.DisableFormatting) == statusIn.ToString(SaveOptions.DisableFormatting),
            "default filter + grid condition: example comment kept, marker added, condition below it");
        Check(canRun(), "merged filter is valid");
        Console.WriteLine("     merged:\n" + ((string)filterXml.GetValue(editor)).Replace("\n", "\n       "));

        setGrid.Invoke(editor, new object[] { new List<XNode> { XElement.Parse("<condition attribute=\"msdyn_flow_approval_result\" operator=\"null\" />") } });
        merged = XElement.Parse((string)filterXml.GetValue(editor));
        Check(merged.Elements().Count() == 1 && (string)merged.Elements().Single().Attribute("attribute") == "msdyn_flow_approval_result", "a new grid change replaces the previous grid section");

        filterXml.SetValue(editor, "<filter type=\"and\">\n  <condition attribute=\"msdyn_flow_approval_title\" operator=\"like\" value=\"%exp%\" />\n</filter>");
        setGrid.Invoke(editor, new object[] { new List<XNode> { new XElement(statusIn) } });
        merged = XElement.Parse((string)filterXml.GetValue(editor));
        Check(merged.Elements().Count() == 2 && (string)merged.Elements().First().Attribute("attribute") == "msdyn_flow_approval_title", "the user's own condition is kept above the grid section");
        setGrid.Invoke(editor, new object[] { new List<XNode>() });
        merged = XElement.Parse((string)filterXml.GetValue(editor));
        Check(merged.Elements().Count() == 1 && !merged.Nodes().OfType<XComment>().Any(), "clearing the grid's filters removes the grid section only");

        filterXml.SetValue(editor, "<filter type=\"or\">\n  <condition attribute=\"statuscode\" operator=\"eq\" value=\"192350001\" />\n  <condition attribute=\"statuscode\" operator=\"eq\" value=\"192350000\" />\n</filter>");
        setGrid.Invoke(editor, new object[] { new List<XNode> { XElement.Parse("<condition attribute=\"msdyn_flow_approval_result\" operator=\"null\" />") } });
        merged = XElement.Parse((string)filterXml.GetValue(editor));
        Check((string)merged.Attribute("type") == "and" && (string)merged.Elements().First().Attribute("type") == "or" && merged.Elements().First().Elements().Count() == 2,
            "a user 'or' filter is wrapped in an 'and' so the grid conditions still apply to every row");

        var before = "<filter>\n  <condition attribute=\"statuscode\" operator=\"eq\"";
        filterXml.SetValue(editor, before);
        setGrid.Invoke(editor, new object[] { new List<XNode> { new XElement(statusIn) } });
        Check((string)filterXml.GetValue(editor) == before, "a filter with XML errors is left alone");

        filterXml.SetValue(editor, defaultFilter);
        setGrid.Invoke(editor, new object[] { new List<XNode> { new XElement(statusIn) } });
        var box = editor.Controls[0].Controls.OfType<RichTextBox>().OrderBy(b => b.Top).ElementAt(1);
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(box, new object[] { new KeyEventArgs(Keys.Control | Keys.Z) });
        Application.DoEvents();
        Check((string)filterXml.GetValue(editor) == defaultFilter.Replace("\r\n", "\n"), "Ctrl+Z undoes a grid update in one step");
        editorForm.Close();

        // ---- main control: grid filters and search flow into its query panel (created, not shown, so it does not load)
        var main = (Control)Activator.CreateInstance(plugin.GetType("ApprovalAdministrationTool.ApprovalAdminControl", true));
        var mainGrid = (Control)main.GetType().GetField("dataGridControl1", flags).GetValue(main);
        var mainEditor = main.GetType().GetField("queryEditor", flags).GetValue(main);
        gridType.GetMethod("ShowApprovals").Invoke(mainGrid, new object[] { load() });
        gridType.GetMethod("SetColumnFilter", flags).Invoke(mainGrid, new object[] { "statuscode", new[] { "Pending" } });
        var panelText = (string)filterXml.GetValue(mainEditor);
        Check(panelText.Contains("<condition attribute=\"statuscode\" operator=\"eq\" value=\"192350001\" />") && panelText.Contains("Add conditions here"),
            "main control: a grid filter appears in the query panel, below the default filter's comment");
        gridType.GetMethod("SetSearchText").Invoke(mainGrid, new object[] { "budget" });
        panelText = (string)filterXml.GetValue(mainEditor);
        Check(panelText.Contains("value=\"%budget%\"") && panelText.Contains("192350001"), "main control: the search is added too, with the column filter");
        gridType.GetMethod("SetColumnFilter", flags).Invoke(mainGrid, new object[] { "statuscode", null });
        gridType.GetMethod("SetSearchText").Invoke(mainGrid, new object[] { "" });
        panelText = (string)filterXml.GetValue(mainEditor);
        Check(!panelText.Contains("Grid filters") && panelText.Contains("Add conditions here"), "main control: clearing them restores the panel's filter");
        // ---- Run clears the grid's filters and search, and keeps their conditions in the query as ordinary ones
        gridType.GetMethod("SetColumnFilter", flags).Invoke(mainGrid, new object[] { "statuscode", new[] { "Pending" } });
        var mainSearch = (ToolStripTextBox)main.GetType().GetField("tstSearch", flags).GetValue(main);
        mainSearch.Text = "budget";
        gridType.GetMethod("SetSearchText").Invoke(mainGrid, new object[] { "budget" });
        var ran = 0;
        editorType.GetEvent("RunRequested").AddEventHandler(mainEditor, new EventHandler<string>((s, e) => ran++));
        editorType.GetMethod("Run", flags).Invoke(mainEditor, null);
        panelText = (string)filterXml.GetValue(mainEditor);
        var gridFilters = (IDictionary)gridType.GetField("filters", flags).GetValue(mainGrid);
        Check(ran == 1, "Run raised RunRequested");
        Check(gridFilters.Count == 0 && (string)gridType.GetField("searchText", flags).GetValue(mainGrid) == "" && mainSearch.Text == "", "Run clears the column filters, the grid search and the search box");
        Check(!panelText.Contains("Grid filters") && panelText.Contains("192350001") && panelText.Contains("%budget%"), "Run keeps the grid's conditions in the query, without the marker: " + panelText.Length);
        gridType.GetMethod("SetColumnFilter", flags).Invoke(mainGrid, new object[] { "result", null });
        Check(((string)activeFilter(main)).Contains("192350001") && ((string)activeFilter(main)).Contains("%budget%"), "the query that runs includes the grid's conditions");
        main.Dispose();

        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }
}



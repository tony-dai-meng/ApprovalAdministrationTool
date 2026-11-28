using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

// Checks the query editor: FetchFilterValidator rules, ApprovalQuery building, QueryEditorControl (colouring, errors,
// Run, undo, plain paste) and the main control's "Modify query" button showing and hiding the panel.
static class QueryTest
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }

    [STAThread]
    static int Main(string[] args)
    {
        var plugin = Assembly.LoadFrom(args[0]);
        var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        // ---- validator
        var validator = plugin.GetType("ApprovalAdministrationTool.Services.FetchFilterValidator", true);
        var columns = new HashSet<string> { "statuscode", "msdyn_flow_approval_title", "createdon", "ownerid" };
        Func<string, List<object>> validate = xml => ((IEnumerable)validator.GetMethod("Validate").Invoke(null, new object[] { xml, columns })).Cast<object>().ToList();
        Func<object, bool> isError = p => (bool)p.GetType().GetProperty("IsError").GetValue(p);
        Func<object, int> line = p => (int)p.GetType().GetProperty("Line").GetValue(p);
        Func<List<object>, string> describe = ps => string.Join(" | ", ps.Select(p => p.ToString()));
        var defaultFilter = (string)plugin.GetType("ApprovalAdministrationTool.Services.ApprovalQuery", true).GetField("DefaultFilter").GetValue(null);

        Check(validate("").Count == 0, "empty filter is valid (no filter)");
        Check(validate(defaultFilter).Count == 0, "the default filter (with its example comment) is valid");
        Check(validate("<filter type=\"and\">\n  <condition attribute=\"statuscode\" operator=\"eq\" value=\"192350001\" />\n  <filter type=\"or\">\n    <condition attribute=\"ownerid\" operator=\"eq-userid\" />\n    <condition attribute=\"createdon\" operator=\"last-x-days\" value=\"7\" />\n  </filter>\n</filter>").Count == 0,
            "nested and/or filters with valid conditions are valid");
        Check(validate("<filter><condition attribute=\"statuscode\" operator=\"in\"><value>1</value><value>2</value></condition></filter>").Count == 0, "'in' with <value> elements is valid");

        var broken = validate("<filter>\n  <condition attribute=\"statuscode\" operator=\"eq\" value=\"1\">\n</filter>");
        Check(broken.Count == 1 && isError(broken[0]) && line(broken[0]) == 3, "malformed XML: one error with its line (" + describe(broken) + ")");
        var wrongRoot = validate("<entity name=\"x\" />");
        Check(wrongRoot.Count == 1 && isError(wrongRoot[0]) && wrongRoot[0].ToString().Contains("single <filter>"), "root must be <filter> (" + describe(wrongRoot) + ")");
        var link = validate("<filter>\n  <link-entity name=\"systemuser\" from=\"systemuserid\" to=\"ownerid\" />\n</filter>");
        Check(link.Count == 1 && isError(link[0]) && line(link[0]) == 2 && link[0].ToString().Contains("not allowed"), "<link-entity> is locked out (" + describe(link) + ")");
        var attr = validate("<filter type=\"and\" hint=\"x\"><condition attribute=\"statuscode\" operator=\"eq\" value=\"1\" entityname=\"a\" /></filter>");
        Check(attr.Count(isError) == 2, "attributes other than type/attribute/operator/value are locked out (" + describe(attr) + ")");
        var badType = validate("<filter type=\"xor\" />");
        Check(badType.Count == 1 && isError(badType[0]), "filter type must be and/or");
        var unknownColumn = validate("<filter><condition attribute=\"nosuchcolumn\" operator=\"eq\" value=\"1\" /></filter>");
        Check(unknownColumn.Count == 1 && isError(unknownColumn[0]) && unknownColumn[0].ToString().Contains("no column named"), "unknown column is an error (" + describe(unknownColumn) + ")");
        var caseColumn = validate("<filter><condition attribute=\"StatusCode\" operator=\"eq\" value=\"1\" /></filter>");
        Check(caseColumn.Count == 1 && caseColumn[0].ToString().Contains("\"statuscode\""), "wrong-case column suggests the right name (" + describe(caseColumn) + ")");
        var noOperator = validate("<filter><condition attribute=\"statuscode\" value=\"1\" /></filter>");
        Check(noOperator.Count == 1 && isError(noOperator[0]), "missing operator is an error");
        var noValue = validate("<filter><condition attribute=\"statuscode\" operator=\"eq\" /></filter>");
        Check(noValue.Count == 1 && isError(noValue[0]) && noValue[0].ToString().Contains("needs a value"), "eq without a value is an error");
        var between = validate("<filter><condition attribute=\"createdon\" operator=\"between\"><value>2026-01-01</value></condition></filter>");
        Check(between.Count == 1 && isError(between[0]), "between needs exactly two values");
        var nullWithValue = validate("<filter><condition attribute=\"createdon\" operator=\"null\" value=\"1\" /></filter>");
        Check(nullWithValue.Count == 1 && !isError(nullWithValue[0]), "a value on 'null' is only a warning");
        var unknownOperator = validate("<filter><condition attribute=\"statuscode\" operator=\"sort-of\" value=\"1\" /></filter>");
        Check(unknownOperator.Count == 1 && !isError(unknownOperator[0]), "an unknown operator is a warning, left to Dataverse");
        var text = validate("<filter>hello<condition attribute=\"statuscode\" operator=\"null\" /></filter>");
        Check(text.Count == 1 && isError(text[0]), "stray text inside <filter> is an error");

        // ---- query building
        var query = plugin.GetType("ApprovalAdministrationTool.Services.ApprovalQuery", true);
        var prefix = (string)query.GetMethod("Prefix").Invoke(null, new object[] { new[] { "msdyn_flow_approvalid", "statuscode" } });
        var suffix = (string)query.GetField("Suffix").GetValue(null);
        var whole = (string)query.GetMethod("Compose").Invoke(null, new object[] { prefix, defaultFilter, suffix });
        var doc = System.Xml.Linq.XDocument.Parse(whole);
        Check(doc.Root.Name == "fetch" && doc.Root.Element("entity").Elements("attribute").Count() == 2 && doc.Root.Element("entity").Element("filter") != null,
            "prefix + filter + suffix is one well-formed fetch with the filter inside the entity");
        var paged = System.Xml.Linq.XDocument.Parse((string)query.GetMethod("ForPage").Invoke(null, new object[] { whole, 3, 5000, "<cookie page=\"2\"><id last=\"x\" /></cookie>" }));
        Check((string)paged.Root.Attribute("page") == "3" && (string)paged.Root.Attribute("count") == "5000" && ((string)paged.Root.Attribute("paging-cookie")).Contains("<id last"),
            "paging attributes are set (cookie XML escaped correctly)");

        // ---- editor control
        var editorType = plugin.GetType("ApprovalAdministrationTool.Controls.QueryEditorControl", true);
        var editor = (UserControl)Activator.CreateInstance(editorType);
        var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false, ClientSize = new Size(800, 420) };
        editor.Dock = DockStyle.Fill;
        form.Controls.Add(editor); form.Show(); Application.DoEvents();
        editorType.GetMethod("SetQueryParts").Invoke(editor, new object[] { prefix, suffix });
        editorType.GetMethod("SetTableColumns").Invoke(editor, new object[] { columns });
        var setFilter = editorType.GetProperty("FilterXml");
        setFilter.SetValue(editor, defaultFilter);
        Application.DoEvents();
        var boxes = editor.Controls[0].Controls.OfType<RichTextBox>().OrderBy(b => b.Top).ToList();
        var run = editor.Controls[0].Controls.OfType<Button>().Single();
        var status = editor.Controls[0].Controls.OfType<Label>().OrderBy(l => l.Top).Last();
        string sent = null;
        editorType.GetEvent("RunRequested").AddEventHandler(editor, new EventHandler<string>((s, xml) => sent = xml));
        Func<bool> canRun = () => (bool)editorType.GetProperty("CanRun", flags).GetValue(editor);
        Action lint = () => { editorType.GetMethod("LintNow", flags).Invoke(editor, null); Application.DoEvents(); };

        Check(boxes.Count == 3 && boxes[0].ReadOnly && !boxes[1].ReadOnly && boxes[2].ReadOnly, "three boxes: read-only start, editable filter, read-only end");
        Check(boxes[0].Text.StartsWith("<fetch>") && boxes[0].Text.Contains("<attribute name=\"statuscode\" />") && boxes[2].Text.Contains("</fetch>"), "fixed parts show the start and end of the query");
        Check(boxes[1].Text.StartsWith("<filter") && boxes[1].Text.TrimEnd().EndsWith("</filter>"), "the editable box holds the <filter> tags");
        Check(canRun() && status.Text.Contains("valid"), "default filter: Run enabled (" + status.Text + ")");

        var editable = boxes[1];
        editable.Select(1, 1);   // the "f" of <filter
        Check(editable.SelectionColor.ToArgb() == Color.FromArgb(163, 21, 21).ToArgb(), "element names are coloured");
        var commentStart = editable.Text.IndexOf("<!--");
        editable.Select(commentStart + 5, 1);
        Check(editable.SelectionColor.ToArgb() == Color.Green.ToArgb(), "comments are coloured green");

        editable.SelectionStart = editable.TextLength; editable.SelectionLength = 0;
        editable.SelectedText = "\n<link-entity name=\"x\" />";   // typed at the end, outside </filter>
        lint();
        Check(!canRun() && status.Text.StartsWith("Error:"), "invalid XML disables Run and shows the error (" + status.Text.Replace("\n", " / ") + ")");
        run.PerformClick(); Application.DoEvents();
        Check(sent == null, "Run does nothing while there are errors");

        editable.Focus();
        var keyDown = typeof(Control).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance);
        keyDown.Invoke(editable, new object[] { new KeyEventArgs(Keys.Control | Keys.Z) }); Application.DoEvents(); lint();
        Check(editable.Text == defaultFilter.Replace("\r\n", "\n") && canRun(), "Ctrl+Z undoes the edit and the filter is valid again");
        keyDown.Invoke(editable, new object[] { new KeyEventArgs(Keys.Control | Keys.Y) }); Application.DoEvents(); lint();
        Check(editable.Text.Contains("link-entity") && !canRun(), "Ctrl+Y redoes it");

        setFilter.SetValue(editor, "<filter type=\"and\">\n  <condition attribute=\"statuscode\" operator=\"eq\" value=\"192350001\" />\n</filter>");
        Application.DoEvents();
        Check(canRun(), "a valid condition enables Run");
        run.PerformClick(); Application.DoEvents();
        Check(sent != null && sent.Contains("192350001"), "Run raises RunRequested with the filter");
        var badLine = "<filter>\n  <condition attribute=\"nosuch\" operator=\"eq\" value=\"1\" />\n</filter>";
        setFilter.SetValue(editor, badLine); Application.DoEvents();
        editable.Select(editable.GetFirstCharIndexFromLine(1) + 1, 1);
        Check(editable.SelectionBackColor.ToArgb() == Color.FromArgb(255, 226, 226).ToArgb(), "the first error's line is tinted");

        Clipboard.SetData(DataFormats.Rtf, @"{\rtf1\ansi{\fonttbl\f0\fswiss Arial;}\f0\fs48 BIG}");
        Clipboard.SetText("<!-- pasted -->", TextDataFormat.UnicodeText);
        setFilter.SetValue(editor, "<filter>\n</filter>"); Application.DoEvents();
        editable.SelectionStart = editable.Text.IndexOf("</filter>"); editable.SelectionLength = 0;
        keyDown.Invoke(editable, new object[] { new KeyEventArgs(Keys.Control | Keys.V) }); Application.DoEvents();
        Check(editable.Text.Contains("<!-- pasted -->"), "Ctrl+V pastes plain text");
        form.Close();

        // ---- main control: Modify query shows and hides the panel (the control is created, not shown, so it does not load)
        try
        {
            var main = (Control)Activator.CreateInstance(plugin.GetType("ApprovalAdministrationTool.ApprovalAdminControl", true));
            var mainType = main.GetType();
            var button = (ToolStripButton)mainType.GetField("tsbModifyQuery", flags).GetValue(main);
            var panel = (Control)mainType.GetField("queryEditor", flags).GetValue(main);
            var splitter = (Control)mainType.GetField("splitterQuery", flags).GetValue(main);
            var getState = typeof(Control).GetMethod("GetState", BindingFlags.NonPublic | BindingFlags.Instance);
            Func<Control, bool> visibleFlag = c => (bool)getState.Invoke(c, new object[] { 0x2 });
            var mainEditable = panel.Controls[0].Controls.OfType<RichTextBox>().OrderBy(b => b.Top).ElementAt(1);
            Check(!visibleFlag(panel) && !visibleFlag(splitter), "query panel is hidden by default");
            Check(mainEditable.Text.StartsWith("<filter") && mainEditable.Text.Contains("<!--"), "panel starts with the default filter");
            button.PerformClick();
            Check(button.Checked && visibleFlag(panel) && visibleFlag(splitter), "Modify query shows the panel and its resize bar");
            button.PerformClick();
            Check(!button.Checked && !visibleFlag(panel), "pressing it again hides the panel");
            var toolbar = (ToolStrip)mainType.GetField("toolStripMenu", flags).GetValue(main);
            Check(string.Join(" | ", toolbar.Items.Cast<ToolStripItem>().Where(i => !(i is ToolStripSeparator)).Select(i => i.Text)) == "Show logical names | Reset settings | Modify query | Refresh",
                "toolbar: Show logical names | Reset settings | Modify query, Refresh on the right");
            main.Dispose();
        }
        catch (Exception e)
        {
            Check(false, "main control could not be created outside XrmToolBox: " + (e.InnerException ?? e).Message);
        }

        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }
}

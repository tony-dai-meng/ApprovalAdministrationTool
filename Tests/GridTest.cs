using System;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

// Checks DataGridControl: friendly/logical toggle keeps column widths, headers wrap instead of clipping, header height fits.
static class GridHarness
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }

    [STAThread]
    static int Main(string[] args)
    {
        var plugin = Assembly.LoadFrom(args[0]);
        var gridType = plugin.GetType("ApprovalAdministrationTool.Controls.DataGridControl", true);
        var wrap = gridType.GetMethod("WrapHeaderText", BindingFlags.NonPublic | BindingFlags.Static);

        Func<DataTable> makeTable = () =>
        {
            var t = new DataTable();
            t.Columns.Add("msdyn_flow_approvalid", typeof(Guid)).Caption = "Approval";
            t.Columns.Add("msdyn_flow_approval_title").Caption = "Title";
            t.Columns.Add("msdyn_flow_approval_allowcancel").Caption = "Allow Cancel";
            t.Columns.Add("msdyn_flow_approval_allowreassign").Caption = "Allow Reassign";
            t.Columns.Add("msdyn_flow_approval_modeltype").Caption = "Approval Model Type";
            t.Columns.Add("msdyn_flow_approval_itemlink").Caption = "Item Link";
            t.Columns.Add("msdyn_flow_approval_itemlinkdescription").Caption = "Item Link Description";
            t.Rows.Add(Guid.NewGuid(), "Example", "Yes", "Yes", "msdyn_flow_awaitallactionapprovalmodel", "", "");
            t.Rows.Add(Guid.NewGuid(), "Hello hello", "Yes", "Yes", "msdyn_flow_awaitallapprovalmodel", "https://google.com", "Example Link");
            t.Rows.Add(Guid.NewGuid(), "MR Approval", "Yes", "No", "msdyn_flow_basicapprovalmodel", "", "");
            return t;
        };

        var form = new Form { Width = 1100, Height = 260, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false };
        var control = (Control)Activator.CreateInstance(gridType);
        control.Dock = DockStyle.Fill;
        form.Controls.Add(control);
        form.Show();
        Application.DoEvents();
        var grid = (DataGridView)control.Controls[0];
        // Visible data columns in on-screen order (the spacer column is excluded).
        var visible = new Func<DataGridViewColumn[]>(() => grid.Columns.Cast<DataGridViewColumn>()
            .Where(c => c.Visible && !string.IsNullOrEmpty(c.DataPropertyName)).OrderBy(c => c.DisplayIndex).ToArray());
        Func<int[]> widths = () => visible().Select(c => c.Width).ToArray();
        Action<DataTable> show = t => { gridType.GetMethod("ShowApprovals").Invoke(control, new object[] { t }); Application.DoEvents(); };
        Action<bool> useLogical = on => { gridType.GetMethod("UseLogicalNameHeaders").Invoke(control, new object[] { on }); Application.DoEvents(); };
        // Header text area width, matching the control: 2 x 4px padding, cell style padding, 16px kept for the sort arrow.
        // Plus 20px for each header's filter icon. (This test's tables have no friendly-values columns, so no switch icon.)
        Func<DataGridViewColumn, int> textWidth = c => c.Width - 8 - c.HeaderCell.InheritedStyle.Padding.Horizontal - 16 - 20;
        Func<DataGridViewColumn, string> wrapped = c => (string)wrap.Invoke(null, new object[] { c.HeaderText, c.HeaderCell.InheritedStyle.Font, textWidth(c) });
        Func<bool> noClipping = () => visible().All(c => wrapped(c).Split('\n').All(line =>
            TextRenderer.MeasureText(line, c.HeaderCell.InheritedStyle.Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width <= textWidth(c)));
        Func<string> lines = () => string.Join(" | ", visible().Select(c => wrapped(c).Replace("\n", " / ")));
        Action<string> shot = path => { var b = new Bitmap(grid.Width, grid.Height); grid.DrawToBitmap(b, new Rectangle(Point.Empty, grid.Size)); b.Save(path); };

        show(makeTable());
        var firstWidths = widths();
        var singleLineHeight = grid.ColumnHeadersHeight;
        Check(visible().All(c => !wrapped(c).Contains("\n")), "friendly names each fit on one line at the default widths");
        Check(noClipping(), "friendly headers: no line wider than its column");
        shot(args[1] + "\\grid_friendly.png");

        useLogical(true);
        Check(widths().SequenceEqual(firstWidths), "toggle to logical names keeps every column width");
        Check(noClipping(), "logical headers: no line wider than its column");
        Check(grid.ColumnHeadersHeight > singleLineHeight, "header row grows for wrapped logical names (" + singleLineHeight + " -> " + grid.ColumnHeadersHeight + ")");
        Console.WriteLine("     logical wrapping: " + lines());
        shot(args[1] + "\\grid_logical.png");

        show(makeTable());
        Check(widths().SequenceEqual(firstWidths), "refresh while on logical names keeps the same widths");
        Check(visible()[0].HeaderText == "msdyn_flow_approval_title", "refresh keeps logical names showing");

        useLogical(false);
        Check(widths().SequenceEqual(firstWidths), "toggle back to friendly names keeps every column width");
        Check(grid.ColumnHeadersHeight == singleLineHeight, "header row shrinks back to one line (" + grid.ColumnHeadersHeight + ")");

        var desc = grid.Columns["msdyn_flow_approval_itemlinkdescription"];
        desc.Width = 60;
        Application.DoEvents();
        Check(noClipping() && grid.ColumnHeadersHeight > singleLineHeight, "narrowing a column wraps its friendly name and grows the header (" + wrapped(desc).Replace("\n", " / ") + ")");
        shot(args[1] + "\\grid_narrowed.png");

        form.Close();
        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }
}

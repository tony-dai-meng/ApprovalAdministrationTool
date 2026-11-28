using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

// Checks the header hover text: every displayed column has a definition, each header gets its tooltip, lines are wrapped,
// and (best effort) hovering a header in a real window shows the tooltip.
static class TooltipTest
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }

    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
    delegate bool EnumProc(IntPtr h, IntPtr l);

    [STAThread]
    static int Main(string[] args)
    {
        var plugin = Assembly.LoadFrom(args[0]);
        var gridType = plugin.GetType("ApprovalAdministrationTool.Controls.DataGridControl", true);
        var definitions = (IDictionary)plugin.GetType("ApprovalAdministrationTool.Models.ApprovalColumnDefinitions", true).GetField("ByLogicalName").GetValue(null);
        var columns = ((IEnumerable)plugin.GetType("ApprovalAdministrationTool.Models.ApprovalColumns", true).GetField("All").GetValue(null)).Cast<string>().ToList();

        var shown = columns.Where(c => c != "msdyn_flow_approvalid").ToList();
        Check(shown.All(definitions.Contains), "all " + shown.Count + " displayed columns have a definition" +
            (shown.All(definitions.Contains) ? "" : " (missing: " + string.Join(", ", shown.Where(c => !definitions.Contains(c))) + ")"));
        foreach (var c in shown)
        {
            var words = ((string)definitions[c]).Split(' ').Length;
            Console.WriteLine("     {0,-42} {1,3} words", c, words);
        }

        var t = new DataTable();
        foreach (var c in columns) t.Columns.Add(c, c == "msdyn_flow_approvalid" ? typeof(Guid) : typeof(string));
        t.Columns["msdyn_flow_approval_title"].Caption = "Title";
        t.Columns["msdyn_flow_approval_allowcancel"].Caption = "Allow Cancel";
        t.Rows.Add(new object[] { Guid.NewGuid() }.Concat(Enumerable.Repeat((object)"x", columns.Count - 1)).ToArray());

        var form = new Form { Width = 1400, Height = 300, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false };
        var control = (Control)Activator.CreateInstance(gridType);
        control.Dock = DockStyle.Fill;
        form.Controls.Add(control);
        form.Show();
        gridType.GetMethod("ShowApprovals").Invoke(control, new object[] { t });
        Application.DoEvents();
        var grid = (DataGridView)control.Controls[0];

        var title = grid.Columns["msdyn_flow_approval_title"];
        Check(title.ToolTipText.StartsWith("Title (msdyn_flow_approval_title)\n\nThe title of the approval request"), "Title header tooltip starts with its names, then the definition");
        Check(grid.Columns["msdyn_flow_approval_source"].ToolTipText.StartsWith("msdyn_flow_approval_source\n\n"), "a column without a display name shows its logical name once");
        Check(shown.All(c => grid.Columns[c].ToolTipText.Length > 200), "every displayed column header has its paragraph tooltip");
        var longest = shown.SelectMany(c => grid.Columns[c].ToolTipText.Split('\n')).Max(l => l.Length);
        Check(longest <= 80, "tooltip lines wrap at 80 characters (longest " + longest + ")");
        Check(grid.Columns["spacer"].ToolTipText == "", "spacer column has no tooltip");

        gridType.GetMethod("UseLogicalNameHeaders").Invoke(control, new object[] { true });
        Check(title.ToolTipText.StartsWith("Title (msdyn_flow_approval_title)"), "tooltip is the same in logical-name mode");

        // Best effort: hover the Title header with window messages and look for a visible tooltip window with our text.
        var rect = grid.GetCellDisplayRectangle(title.Index, -1, false);
        var lParam = (IntPtr)(((rect.Top + rect.Height / 2) << 16) | ((rect.Left + rect.Width / 2) & 0xFFFF));
        for (int i = 0; i < 20; i++) { SendMessage(grid.Handle, 0x200, IntPtr.Zero, lParam); Application.DoEvents(); System.Threading.Thread.Sleep(50); }
        string tip = null;
        var pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        EnumWindows((h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p);
            var cls = new StringBuilder(64); GetClassName(h, cls, 64);
            if (p == pid && cls.ToString().StartsWith("tooltips_class") && IsWindowVisible(h)) { var sb = new StringBuilder(4096); GetWindowText(h, sb, 4096); if (sb.Length > 0) tip = sb.ToString(); }
            return true;
        }, IntPtr.Zero);
        Console.WriteLine(tip != null
            ? "INFO hovering the Title header showed a tooltip: \"" + tip.Split('\n')[0] + "\" (+" + (tip.Split('\n').Length - 1) + " more lines)"
            : "INFO no tooltip window detected from simulated hover (simulated hover may not trigger it in a hidden test window)");

        form.Close();
        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }
}

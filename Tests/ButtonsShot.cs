using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

// Renders the action buttons enabled and disabled, off-screen, to a PNG, and checks their colours.
static class ButtonsShot
{
    [STAThread]
    static int Main(string[] args)
    {
        var plugin = Assembly.LoadFrom(args[0]);
        var type = plugin.GetType("ApprovalAdministrationTool.Controls.ActionsControl", true);
        var failures = 0;
        using (var sheet = new Bitmap(560, 150))
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(SystemColors.Control);
            for (var i = 0; i < 2; i++)
            {
                var enabled = i == 0;
                var actions = (UserControl)Activator.CreateInstance(type);
                actions.Size = new Size(540, 57);
                var h = actions.Handle;
                actions.PerformLayout();
                type.GetProperty("ActionsEnabled").SetValue(actions, enabled);
                using (var bmp = new Bitmap(actions.Width, actions.Height))
                {
                    actions.DrawToBitmap(bmp, new Rectangle(Point.Empty, actions.Size));
                    g.DrawString(enabled ? "Enabled (an open approval selected)" : "Disabled (nothing open selected, or finished)", SystemFonts.MessageBoxFont, Brushes.Black, 10, 4 + i * 75);
                    g.DrawImage(bmp, 10, 20 + i * 75);
                    var expectedByText = new System.Collections.Generic.Dictionary<string, string> { ["Approve"] = "#107C10", ["Reject"] = "#C50F1F", ["Reassign"] = "#0F6CBD", ["Cancel"] = "#BC4B09", ["Remind"] = "#0F6CBD", ["Action"] = "#0F6CBD" };
                    var ok = true;
                    foreach (Control button in actions.Controls[0].Controls)
                    {
                        if (!button.Visible) continue; // Action, until a custom-response approval is chosen
                        var c = bmp.GetPixel(button.Left + 8, button.Top + button.Height / 2);
                        var want = enabled ? expectedByText[button.Text] : "#F0F0F0";
                        var good = string.Equals(ColorTranslator.ToHtml(Color.FromArgb(c.R, c.G, c.B)), want, StringComparison.OrdinalIgnoreCase) && button.GetType().Name == "FluentButton";
                        Console.WriteLine((good ? "PASS " : "FAIL ") + button.Text + (enabled ? " enabled " : " disabled ") + ColorTranslator.ToHtml(c) + " (want " + want + ")");
                        ok &= good;
                    }
                    if (!ok) failures++;
                }

                actions.Dispose();
            }

            sheet.Save(System.IO.Path.Combine(args[1], "buttons.png"), ImageFormat.Png);
        }

        return failures;
    }
}

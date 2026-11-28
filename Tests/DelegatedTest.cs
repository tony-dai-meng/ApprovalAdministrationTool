using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Serialization;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
static class DelegatedTest
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    [STAThread]
    static int Main(string[] args)
    {
        var plugin = Assembly.LoadFrom(args[0]);
        var settingsType = plugin.GetType("ApprovalAdministrationTool.Settings", true);
        Check(settingsType.GetProperty("TeamsApprovalsTokenCache") != null && settingsType.GetProperty("TeamsApprovalsClientSecret") == null, "settings keep the sign-in, no client secret");
        var settings = Activator.CreateInstance(settingsType);
        var auth = plugin.GetType("ApprovalAdministrationTool.Services.TeamsApprovalsAuth", true);
        Check((string)auth.GetField("ApiBaseUrl").GetValue(null) == "https://approvals.teams.microsoft.com", "token is for https://approvals.teams.microsoft.com");
        var instance = Activator.CreateInstance(auth, Guid.NewGuid().ToString(), Guid.NewGuid(), settings, (Action)(() => { }));
        Check(instance != null, "delegated sign-in client builds for a tenant");

        var main = plugin.GetType("ApprovalAdministrationTool.ApprovalAdminControl", true);
        var isAppErr = main.GetMethod("IsAppRegistrationError", F);
        Func<string, string, bool> classify = (code, msg) => (bool)isAppErr.Invoke(null, new object[] { new Microsoft.Identity.Client.MsalServiceException(code, msg) });
        Check(classify("unauthorized_client", "AADSTS700016: Application with identifier 'x' was not found"), "app not found -> ask for the client ID again");
        Check(classify("invalid_request", "AADSTS50011: The redirect URI ... does not match"), "redirect URI missing -> ask again (with the error)");
        Check(!classify("invalid_resource", "AADSTS500011: The resource principal named https://approvals.teams.microsoft.com was not found"), "Approvals API not available -> explained, not asked again");
        Check(!classify("invalid_grant", "AADSTS65001: The user or administrator has not consented"), "missing consent -> explained, not asked again");

        var dlgType = plugin.GetType("ApprovalAdministrationTool.Controls.ClientIdDialog", true);
        var dlg = (Form)Activator.CreateInstance(dlgType, F, null, new object[] { null, null }, null);
        var boxes = Descendants(dlg).OfType<TextBox>().ToList(); var ok = Descendants(dlg).OfType<Button>().First(b => b.Text == "OK");
        Check(boxes.Count == 1, "dialog asks only for the client ID (no secret)");
        Check(Descendants(dlg).OfType<Label>().Any(l => l.Text.Contains("delegated permission") && l.Text.Contains("nativeclient")), "dialog explains redirect URI and delegated permission");
        boxes[0].Text = "nope"; Check(!ok.Enabled, "OK disabled for an invalid client ID");
        boxes[0].Text = Guid.NewGuid().ToString(); Check(ok.Enabled, "OK enabled for a GUID");
        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }
    static System.Collections.Generic.IEnumerable<Control> Descendants(Control c) { foreach (Control x in c.Controls) { yield return x; foreach (var y in Descendants(x)) yield return y; } }
}

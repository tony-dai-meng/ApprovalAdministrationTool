using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Label = System.Windows.Forms.Label;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

class QuietForm : Form
{
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; } } // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
}

class QueryCapture : IOrganizationService
{
    public QueryExpression Last;
    public EntityCollection RetrieveMultiple(QueryBase query) { Last = (QueryExpression)query; var c = new EntityCollection(); var u = new Entity("systemuser", Guid.NewGuid()); u["fullname"] = "Alex Doe"; u["internalemailaddress"] = "alex@contoso.com"; c.Entities.Add(u); return c; }
    public Entity Retrieve(string a, Guid b, ColumnSet c) => throw new NotImplementedException();
    public void Update(Entity e) => throw new NotImplementedException();
    public Guid Create(Entity e) => throw new NotImplementedException();
    public void Delete(string a, Guid b) => throw new NotImplementedException();
    public OrganizationResponse Execute(OrganizationRequest r) => throw new NotImplementedException();
    public void Associate(string a, Guid b, Relationship c, EntityReferenceCollection d) => throw new NotImplementedException();
    public void Disassociate(string a, Guid b, Relationship c, EntityReferenceCollection d) => throw new NotImplementedException();
}

static class ReassignUiTest
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    static IEnumerable<Control> Walk(Control c) { foreach (Control x in c.Controls) { yield return x; foreach (var y in Walk(x)) yield return y; } }

    [STAThread]
    static int Main(string[] args)
    {
        var plugin = Assembly.LoadFrom(args[0]);

        // ---- people query: real users only
        var svcType = plugin.GetType("ApprovalAdministrationTool.Services.ApprovalActionService", true);
        var capture = new QueryCapture();
        var svc = Activator.CreateInstance(svcType, F, null, new object[] { capture }, null);
        var people = (System.Collections.IList)svcType.GetMethod("GetPeople").Invoke(svc, null);
        var conds = capture.Last.Criteria.Conditions.ToDictionary(c => c.AttributeName);
        Check(capture.Last.EntityName == "systemuser" && people.Count == 1, "people come from systemuser");
        Check(conds["isdisabled"].Operator == ConditionOperator.Equal && (bool)conds["isdisabled"].Values[0] == false, "only enabled users");
        Check(conds["applicationid"].Operator == ConditionOperator.Null, "application users left out (no application ID)");
        Check(conds["azureactivedirectoryobjectid"].Operator == ConditionOperator.NotNull, "SYSTEM / INTEGRATION left out (must have an Entra ID account)");
        Check(conds["accessmode"].Operator == ConditionOperator.In && conds["accessmode"].Values.Cast<int>().OrderBy(v => v).SequenceEqual(new[] { 0, 1, 2 }), "only interactive users (Read-Write, Administrative, Read)");
        Check(conds["internalemailaddress"].Operator == ConditionOperator.NotNull, "must have an email (the API reassigns by email)");

        // ---- the window
        var reqType = plugin.GetType("ApprovalAdministrationTool.Services.PendingRequest", true);
        var userType = plugin.GetType("ApprovalAdministrationTool.Services.UserOption", true);
        Func<Guid, string, Guid, object> req = (approval, title, owner) => { var r = Activator.CreateInstance(reqType); reqType.GetProperty("RequestId").SetValue(r, Guid.NewGuid()); reqType.GetProperty("ApprovalId").SetValue(r, approval); reqType.GetProperty("ApprovalTitle").SetValue(r, title); reqType.GetProperty("Approver").SetValue(r, new EntityReference("systemuser", owner) { Name = "Owner " + owner.ToString().Substring(0, 4) }); return r; };
        Func<string, string, object> user = (name, email) => { var u = Activator.CreateInstance(userType); userType.GetProperty("Id").SetValue(u, Guid.NewGuid()); userType.GetProperty("Name").SetValue(u, name); userType.GetProperty("Email").SetValue(u, email); return u; };
        var listType = typeof(List<>).MakeGenericType(userType);
        var peopleList = (System.Collections.IList)Activator.CreateInstance(listType);
        foreach (var p in new[] { user("Alex Doe", "alex@contoso.com"), user("Sam Lee", "sam@contoso.com"), user("Samantha Ray", "sray@contoso.com"), user("Tony Dai", "tony@contoso.com") }) peopleList.Add(p);
        var reqListType = typeof(List<>).MakeGenericType(reqType);
        Func<object[], System.Collections.IList> reqs = items => { var l = (System.Collections.IList)Activator.CreateInstance(reqListType); foreach (var i in items) l.Add(i); return l; };
        var dlgType = plugin.GetType("ApprovalAdministrationTool.Controls.ReassignDialog", true);

        var a1 = Guid.NewGuid();
        var single = reqs(new[] { req(a1, "Budget", Guid.NewGuid()) });
        var dlg = (Form)Activator.CreateInstance(dlgType, F, null, new object[] { single, new List<string>(), peopleList }, null);
        var host = new QuietForm { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        var h = dlg.Handle; dlg.PerformLayout();
        var all = Walk(dlg).ToList();
        var comment = all.OfType<TextBox>().First(t => t.Multiline);
        var combo = all.OfType<ComboBox>().Single();
        var commentLabel = all.OfType<Label>().First(l => l.Text == "Comment");
        var toLabel = all.OfType<Label>().First(l => l.Text == "Reassign to");
        Check(commentLabel.Top < comment.Top && comment.Top < toLabel.Top && toLabel.Top < combo.Top, "comment box is at the top, above the Reassign to dropdown");
        Check(comment.Text == "" && comment.AcceptsReturn, "comment starts empty");
        Check(combo.DropDownStyle == ComboBoxStyle.DropDown && combo.Items.Count == 4, "dropdown lists everyone and can be typed in");
        Check(!all.OfType<CheckedListBox>().Any(c => c.Visible && c.Parent != null && all.Contains(c)), "single approver: no 'Approvers to replace' list");
        var ok = all.OfType<Button>().First(b => b.Text == "Reassign");
        Check(!ok.Enabled, "Reassign disabled until someone is picked");

        combo.Text = "sam";
        dlgType.GetMethod("FilterPeople", F).Invoke(dlg, null);
        var shown = combo.Items.Cast<object>().Select(o => o.ToString()).ToList();
        Check(shown.Count == 2 && shown.All(s => s.Contains("Sam")), "typing 'sam' filters to Sam Lee and Samantha Ray: " + string.Join(" | ", shown));
        Check(combo.Text == "sam", "typed text is kept while filtering");
        combo.Text = "sray@";
        dlgType.GetMethod("FilterPeople", F).Invoke(dlg, null);
        Check(combo.Items.Count == 1, "search matches email too");
        dlgType.GetMethod("PickTypedPerson", F).Invoke(dlg, null);
        Check(combo.SelectedItem != null && combo.SelectedItem.ToString().StartsWith("Samantha"), "single match is picked when leaving the box");
        Check(ok.Enabled, "Reassign enabled once someone is picked");
        Check(((System.Collections.IEnumerable)dlgType.GetProperty("SelectedRequests").GetValue(dlg)).Cast<object>().Count() == 1, "the one request is reassigned");
        combo.Text = "zzz"; dlgType.GetMethod("FilterPeople", F).Invoke(dlg, null);
        Check(combo.Items.Count == 0 && all.OfType<Label>().Any(l => l.Text.Contains("No one in this environment matches")), "no match says so");
        dlg.Dispose();

        var a2 = Guid.NewGuid();
        var multi = reqs(new[] { req(a2, "Sequential", Guid.NewGuid()), req(a2, "Sequential", Guid.NewGuid()), req(a1, "Budget", Guid.NewGuid()) });
        var dlg2 = (Form)Activator.CreateInstance(dlgType, F, null, new object[] { multi, new List<string> { "Done one" }, peopleList }, null);
        var h2 = dlg2.Handle;
        var list = Walk(dlg2).OfType<CheckedListBox>().Single();
        Check(Walk(dlg2).OfType<Label>().Any(l => l.Text == "Approvers to replace") && list.Items.Count == 3, "several approvers: 'Approvers to replace' list shown");
        Check(list.GetItemChecked(2) && !list.GetItemChecked(0) && !list.GetItemChecked(1), "single-approver approval pre-ticked, multi-approver ones left for you to choose");
        Check(Walk(dlg2).OfType<Label>().Any(l => l.Text.Contains("Done one")), "approvals not waiting on anyone listed as skipped");
        dlg2.Dispose();

        // ---- main control: button enabled state and click
        var main = (Control)Activator.CreateInstance(plugin.GetType("ApprovalAdministrationTool.ApprovalAdminControl", true));
        var form = new QuietForm { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000), ClientSize = new Size(1400, 800), ShowInTaskbar = false };
        main.Dock = DockStyle.Fill; form.Controls.Add(main); form.Show(); Application.DoEvents();
        var reassignButton = Walk(main).OfType<Button>().First(b => b.Text == "Reassign");
        Check(!reassignButton.Enabled, "Reassign disabled while no approval is loaded");
        var grid = (Control)main.GetType().GetField("dataGridControl1", F).GetValue(main);
        var t = new DataTable(); t.Columns.Add("msdyn_flow_approvalid", typeof(Guid)); t.Columns.Add("msdyn_flow_approval_title"); t.Rows.Add(Guid.NewGuid(), "One"); t.Rows.Add(Guid.NewGuid(), "Two");
        grid.GetType().GetMethod("ShowApprovals").Invoke(grid, new object[] { t });
        for (var i = 0; i < 5; i++) Application.DoEvents();
        Check(reassignButton.Enabled, "Reassign enabled once an approval is selected");
        form.Close();

        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }
}


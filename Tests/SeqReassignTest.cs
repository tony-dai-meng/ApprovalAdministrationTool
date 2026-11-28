using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Label = System.Windows.Forms.Label;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

class Stub : HttpMessageHandler
{
    public List<string> Log = new List<string>(); public string Reply;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
    {
        Log.Add(r.Method + " " + r.RequestUri.AbsolutePath);
        return Task.FromResult(Reply == null ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }
            : new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(Reply) });
    }
}

// An approval with three steps: Diego (0, current), Alex (1), Grady (2).
class Org : IOrganizationService
{
    public List<string> Log = new List<string>(); public Guid Approval, Diego, Alex, Grady, RDiego, RAlex, RGrady;
    Entity Req(Guid id, Guid owner, int step)
    {
        var e = new Entity("msdyn_flow_approvalrequest", id);
        e["msdyn_flow_approvalrequest_approval"] = new EntityReference("msdyn_flow_approval", Approval) { Name = "Seq" };
        e["ownerid"] = new EntityReference("systemuser", owner) { Name = owner == Diego ? "Diego" : owner == Alex ? "Alex" : "Grady" };
        e["msdyn_flow_approvalrequest_stepnumber"] = step;
        e["statecode"] = new OptionSetValue(0);
        e["approval.msdyn_flow_approval_currentstepnumber"] = new AliasedValue("msdyn_flow_approval", "msdyn_flow_approval_currentstepnumber", 0);
        return e;
    }
    IEnumerable<Entity> All() { yield return Req(RDiego, Diego, 0); yield return Req(RAlex, Alex, 1); yield return Req(RGrady, Grady, 2); }
    public Entity Retrieve(string n, Guid id, ColumnSet c)
    {
        if (n == "msdyn_flow_approvalrequest") return All().First(r => r.Id == id);
        var e = new Entity(n, id); e["msdyn_flow_approval_allowreassign"] = true; e["ownerid"] = new EntityReference("systemuser", Diego); e["msdyn_flow_approval_currentstepnumber"] = 0; return e;
    }
    public EntityCollection RetrieveMultiple(QueryBase q)
    {
        var x = (QueryExpression)q; var rows = All();
        foreach (var cond in x.Criteria.Conditions)
        {
            if (cond.AttributeName == "ownerid") { var v = (Guid)cond.Values[0]; rows = rows.Where(r => ((EntityReference)r["ownerid"]).Id == v); }
            if (cond.AttributeName == "msdyn_flow_approvalrequestid") { var v = (Guid)cond.Values[0]; rows = rows.Where(r => r.Id != v); }
        }
        return new EntityCollection(rows.ToList());
    }
    public void Update(Entity e) { Log.Add("Update " + e.LogicalName); }
    public Guid Create(Entity e) => throw new NotImplementedException();
    public void Delete(string a, Guid b) => throw new NotImplementedException();
    public OrganizationResponse Execute(OrganizationRequest r) => throw new NotImplementedException();
    public void Associate(string a, Guid b, Relationship c, EntityReferenceCollection d) => throw new NotImplementedException();
    public void Disassociate(string a, Guid b, Relationship c, EntityReferenceCollection d) => throw new NotImplementedException();
}

static class SeqReassignTest
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    static IEnumerable<Control> Walk(Control c) { foreach (Control x in c.Controls) { yield return x; foreach (var y in Walk(x)) yield return y; } }

    [STAThread]
    static int Main(string[] args)
    {
        var plugin = Assembly.LoadFrom(args[0]);
        Func<string, Type> T = n => plugin.GetType("ApprovalAdministrationTool." + n, true);
        var svcType = T("Services.ApprovalActionService"); var reqType = T("Services.PendingRequest"); var userType = T("Services.UserOption");
        var stub = new Stub();
        svcType.GetField("Http", F).SetValue(null, new HttpClient(stub) { BaseAddress = new Uri("https://approvals.teams.microsoft.com") });
        var org = new Org { Approval = Guid.NewGuid(), Diego = Guid.NewGuid(), Alex = Guid.NewGuid(), Grady = Guid.NewGuid(), RDiego = Guid.NewGuid(), RAlex = Guid.NewGuid(), RGrady = Guid.NewGuid() };
        Func<object> svc = () => { var s = Activator.CreateInstance(svcType, F, null, new object[] { org }, null); svcType.GetField("requestAllowReassignLooked", F).SetValue(s, true); return s; };
        Func<Guid, string, object> user = (id, name) => { var u = Activator.CreateInstance(userType); userType.GetProperty("Id").SetValue(u, id); userType.GetProperty("Name").SetValue(u, name); userType.GetProperty("Email").SetValue(u, name.Split(' ')[0].ToLower() + "@contoso.com"); return u; };
        var grady = user(org.Grady, "Grady Archie"); var megan = user(Guid.NewGuid(), "Megan Bowen"); var alex = user(org.Alex, "Alex Wilber"); var diego = user(org.Diego, "Diego Siciliani");
        var env = Activator.CreateInstance(T("Services.EnvironmentInfo")); env.GetType().GetProperty("EnvironmentId").SetValue(env, "env-1");

        // pending requests know who else is in the steps
        var pending = ((System.Collections.IEnumerable)svcType.GetMethod("GetPendingRequests").Invoke(svc(), new object[] { new List<Guid> { org.Approval } })).Cast<object>().ToList();
        Check(pending.Count == 1 && (Guid)reqType.GetProperty("RequestId").GetValue(pending[0]) == org.RDiego, "only the current step's request (Diego's) is pending");
        var request = pending[0];
        Func<Guid, string> why = id => (string)reqType.GetMethod("WhyNotReassignTo").Invoke(request, new object[] { id });
        Check((bool)reqType.GetProperty("Sequential").GetValue(request), "approval is seen as sequential");
        Check(why(org.Grady) == "already on step 3" && why(org.Alex) == "already on step 2", "Grady is on step 3, Alex on step 2: " + why(org.Grady) + " / " + why(org.Alex));
        Check(why(org.Diego) == "already the approver" && why(Guid.NewGuid()) == null, "Diego is the approver; anyone else is fine");

        // the service refuses Grady before anything changes
        Func<object, Exception> reassign = to => { try { svcType.GetMethod("Reassign").Invoke(svc(), new[] { request, to, diego, "", env, "TOKEN" }); return null; } catch (TargetInvocationException ex) { return ex.InnerException; } };
        org.Log.Clear(); stub.Log.Clear();
        var refused = reassign(grady);
        Check(refused != null && refused.Message.StartsWith("Grady Archie is already on step 3 of this approval") && refused.Message.Contains("Pick someone else"), "reassigning to Grady is refused in plain words: " + refused?.Message);
        Check(org.Log.Count == 0 && stub.Log.Count == 0, "nothing changed and Teams not called");
        var fine = reassign(megan);
        Check(fine == null && stub.Log.Contains("POST /api/reassignApproval"), "reassigning to Megan goes ahead: " + fine?.Message);

        // Teams' own refusal translated (someone added meanwhile)
        stub.Reply = "{\"ErrorMessage\":\"Exception thrown by dependency.\",\"Properties\":{\"innerMessage\":\"ApprovalRequestReassignmentUserCurrentlyInSteps\"}}";
        var api = reassign(megan); stub.Reply = null;
        Check(api != null && api.Message.Contains("already an approver of this approval") && !api.Message.Contains("ErrorMessage"), "Teams' in-steps error is shown in plain words: " + api?.Message);

        // the dialog greys Grady out and won't reassign to him
        var people = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(userType));
        foreach (var p in new[] { alex, diego, grady, megan }) people.Add(p);
        var reqs = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(reqType)); reqs.Add(request);
        var dlgType = T("Controls.ReassignDialog");
        var dlg = (Form)Activator.CreateInstance(dlgType, F, null, new object[] { reqs, new List<string>(), people }, null);
        var h = dlg.Handle;
        var all = Walk(dlg).ToList(); var combo = all.OfType<ComboBox>().Single(); var ok = all.OfType<Button>().First(b => b.Text == "Reassign");
        Check(combo.DrawMode == DrawMode.OwnerDrawFixed, "people are drawn by the dialog (so some can be greyed out)");
        var whyNot = dlgType.GetMethod("WhyNot", F);
        var sel = dlgType.GetProperty("SelectedRequests").GetValue(dlg);
        Check((string)whyNot.Invoke(dlg, new[] { grady, sel }) == "already on step 3" && whyNot.Invoke(dlg, new[] { megan, sel }) == null, "Grady listed greyed with 'already on step 3', Megan normal");
        combo.SelectedItem = grady;
        var status = all.OfType<Label>().FirstOrDefault(l => l.Text.Contains("Teams won't reassign"));
        Check(!ok.Enabled && status != null && status.Text.StartsWith("Grady Archie is already on step 3"), "picking Grady: Reassign disabled, reason shown: " + status?.Text);
        combo.SelectedItem = megan;
        Check(ok.Enabled && !all.OfType<Label>().Any(l => l.Text.Contains("Teams won't reassign")), "picking Megan: Reassign enabled");
        dlg.Dispose();

        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }
}

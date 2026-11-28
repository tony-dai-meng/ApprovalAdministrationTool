using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
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
    public HttpStatusCode Status; public Action OnSuccess; public List<string> Log; public string Body; public bool HasRequestId;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
    {
        Body = r.Content == null ? null : await r.Content.ReadAsStringAsync();
        HasRequestId = r.Headers.Contains("x-ms-client-request-id");
        Log.Add(r.Method + " " + r.RequestUri.PathAndQuery);
        if (Status == HttpStatusCode.OK) OnSuccess?.Invoke();
        return new HttpResponseMessage(Status) { Content = new StringContent(Status == HttpStatusCode.OK ? "" : "AccessDenied") };
    }
}

// Rows with owners; responses and other requests as configured.
class Org : IOrganizationService
{
    public List<string> Log = new List<string>(); public Dictionary<Guid, Entity> Rows = new Dictionary<Guid, Entity>(); public Dictionary<Guid, string> Names = new Dictionary<Guid, string>();
    public bool NotesFail; public int Responses, OtherRequests; public List<QueryExpression> Queries = new List<QueryExpression>(); public EntityCollection Next;
    public Entity Retrieve(string n, Guid id, ColumnSet c) { var e = new Entity(n, id); foreach (var k in c.Columns) if (Rows[id].Contains(k)) e[k] = Rows[id][k]; return e; }
    public void Update(Entity e) { foreach (var a in e.Attributes) { Rows[e.Id][a.Key] = a.Value; Log.Add(e.LogicalName + "." + a.Key + "=" + (a.Value is EntityReference r ? Names[r.Id] : a.Value.ToString())); } }
    public Guid Create(Entity e) { if (NotesFail) throw new Exception("no notes"); Log.Add("note: " + e["notetext"] + " on " + ((EntityReference)e["objectid"]).LogicalName + " file " + e["filename"] + " " + e["mimetype"] + " " + e["isdocument"] + " [" + System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)e["documentbody"])) + "]"); return Guid.NewGuid(); }
    public EntityCollection RetrieveMultiple(QueryBase q)
    {
        var x = (QueryExpression)q; Queries.Add(x); if (Next != null) return Next;
        var c = new EntityCollection(); for (var i = 0; i < (x.EntityName.EndsWith("response") ? Responses : OtherRequests); i++) c.Entities.Add(new Entity(x.EntityName, Guid.NewGuid())); return c;
    }
    public void Delete(string a, Guid b) => throw new NotImplementedException();
    public OrganizationResponse Execute(OrganizationRequest r) => throw new NotImplementedException();
    public void Associate(string a, Guid b, Relationship c, EntityReferenceCollection d) => throw new NotImplementedException();
    public void Disassociate(string a, Guid b, Relationship c, EntityReferenceCollection d) => throw new NotImplementedException();
}

class Quiet : Form
{
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; } }
}

static class ActionsTest
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    static IEnumerable<Control> Walk(Control c) { foreach (Control x in c.Controls) { yield return x; foreach (var y in Walk(x)) yield return y; } }
    static Assembly plugin;
    static Type T(string n) => plugin.GetType("ApprovalAdministrationTool." + n, true);
    static object New(string type, params object[] props) { var o = Activator.CreateInstance(T(type)); for (var i = 0; i < props.Length; i += 2) o.GetType().GetProperty((string)props[i]).SetValue(o, props[i + 1]); return o; }

    [STAThread]
    static int Main(string[] args)
    {
        plugin = Assembly.LoadFrom(args[0]);
        var svcType = T("Services.ApprovalActionService");
        var stub = new Stub();
        svcType.GetField("Http", F).SetValue(null, new HttpClient(stub) { BaseAddress = new Uri("https://approvals.teams.microsoft.com") });
        svcType.GetField("waitSeconds", F).SetValue(null, 1);
        Guid approval = Guid.NewGuid(), request = Guid.NewGuid(), megan = Guid.NewGuid(), tony = Guid.NewGuid(), requester = Guid.NewGuid();
        var env = New("Services.EnvironmentInfo", "EnvironmentId", "env-1");
        var caller = New("Services.UserOption", "Id", tony, "Name", "Tony");

        Func<Org> org = () =>
        {
            var o = new Org(); o.Names[megan] = "Megan"; o.Names[tony] = "Tony"; o.Names[requester] = "Requester";
            o.Rows[approval] = new Entity("msdyn_flow_approval", approval); o.Rows[approval]["ownerid"] = new EntityReference("systemuser", requester); o.Rows[approval]["statecode"] = new OptionSetValue(0); o.Rows[approval]["msdyn_flow_approval_allowcancel"] = false;
            o.Rows[request] = new Entity("msdyn_flow_approvalrequest", request); o.Rows[request]["ownerid"] = new EntityReference("systemuser", megan); o.Rows[request]["statecode"] = new OptionSetValue(0);
            return o;
        };
        Func<Org, string, object[], Tuple<string, Exception>> call = (o, method, a) =>
        {
            stub.Log = o.Log; var s = Activator.CreateInstance(svcType, F, null, new object[] { o }, null);
            try { return Tuple.Create((string)svcType.GetMethod(method).Invoke(s, a), (Exception)null); } catch (TargetInvocationException ex) { return Tuple.Create((string)null, ex.InnerException); }
        };
        Func<Guid, object> pending = owner => New("Services.PendingRequest", "RequestId", request, "ApprovalId", approval, "Approver", new EntityReference("systemuser", owner) { Name = "Megan" });
        Func<object> open = () => New("Services.OpenApproval", "ApprovalId", approval, "Title", "Budget", "Owner", new EntityReference("systemuser", requester) { Name = "Requester" });

        foreach (var answer in new[] { "Approve", "Reject" })
        {
            var o = org(); stub.Status = HttpStatusCode.OK; stub.OnSuccess = () => o.Responses = 1;
            var r = call(o, "Respond", new[] { pending(megan), answer, caller, "ok \"fine\"", env, "TOK" });
            Check(r.Item1 == null && r.Item2 == null && o.Log.SequenceEqual(new[] { "msdyn_flow_approvalrequest.ownerid=Tony", "POST /api/UpdateApproval", "msdyn_flow_approvalrequest.ownerid=Megan" }), answer + ": borrow, answer, give back: " + string.Join(" -> ", o.Log));
            Check(stub.Body == "{\"ApprovalId\":\"" + approval + "\",\"Properties\":{\"Response\":\"" + answer + "\",\"Comments\":\"ok \\\"fine\\\"\"},\"FlowEnvironment\":\"env-1\"}" && stub.HasRequestId, answer + ": response, comment and environment are sent");
            Check(((EntityReference)o.Rows[approval]["ownerid"]).Id == requester, answer + ": approval record keeps its owner");
        }

        {
            var o = org(); stub.OnSuccess = () => { o.Responses = 1; o.Rows[approval]["ownerid"] = new EntityReference("systemuser", tony); };
            call(o, "Respond", new[] { pending(megan), "Approve", caller, "", env, "TOK" });
            Check(((EntityReference)o.Rows[approval]["ownerid"]).Id == requester, "if the action changes the record owner, it is put back");
        }

        {
            var o = org(); stub.Status = HttpStatusCode.Forbidden; stub.OnSuccess = null;
            var r = call(o, "Respond", new[] { pending(megan), "Approve", caller, "", env, "TOK" });
            Check(r.Item2 != null && r.Item2.Message.Contains("403") && o.Log.Last() == "msdyn_flow_approvalrequest.ownerid=Megan", "refused answer: error shown, request given back");
        }

        {
            var o = org(); stub.Status = HttpStatusCode.OK; stub.OnSuccess = null;
            var r = call(o, "Respond", new[] { pending(megan), "Approve", caller, "", env, "TOK" });
            Check(r.Item2 == null && r.Item1.Contains("not appeared") && o.Log.Last() == "msdyn_flow_approvalrequest.ownerid=Megan", "accepted but not recorded: warning, request still given back");
        }

        {
            var o = org(); o.OtherRequests = 1;
            var r = call(o, "Respond", new[] { pending(megan), "Approve", caller, "", env, "TOK" });
            Check(r.Item2 != null && r.Item2.Message.Contains("also an approver") && o.Log.Count == 0, "signed-in user has their own request too: refused before changing anything");
            o = org(); o.Rows[request]["statecode"] = new OptionSetValue(1);
            r = call(o, "Respond", new[] { pending(megan), "Approve", caller, "", env, "TOK" });
            Check(r.Item2 != null && o.Log.Count == 0, "already answered: refused before changing anything");
        }

        {
            var o = org(); stub.Status = HttpStatusCode.OK; stub.OnSuccess = () => o.Rows[approval]["statecode"] = new OptionSetValue(1);
            var r = call(o, "Cancel", new[] { open(), caller, "no longer needed é", env, "TOK" });
            Check(r.Item1 == null && r.Item2 == null && o.Log.SequenceEqual(new[] { "msdyn_flow_approval.msdyn_flow_approval_allowcancel=True", "msdyn_flow_approval.ownerid=Tony", "DELETE /api/cancelApproval/" + approval + "?flowEnvironment=env-1", "msdyn_flow_approval.ownerid=Requester", "note: no longer needed é on msdyn_flow_approval file admin_comment.txt text/plain True [no longer needed é]" }), "cancel: " + string.Join(" -> ", o.Log));
            o = org(); stub.Status = HttpStatusCode.Forbidden; stub.OnSuccess = null;
            r = call(o, "Cancel", new[] { open(), caller, "no longer needed é", env, "TOK" });
            Check(r.Item2 != null && o.Log.Skip(3).SequenceEqual(new[] { "msdyn_flow_approval.ownerid=Requester", "msdyn_flow_approval.msdyn_flow_approval_allowcancel=False" }), "refused cancel: owner and Allow Cancel put back");
            o = org(); o.NotesFail = true; stub.Status = HttpStatusCode.OK; var oo = o; stub.OnSuccess = () => oo.Rows[approval]["statecode"] = new OptionSetValue(1);
            r = call(o, "Cancel", new[] { open(), caller, "x", env, "TOK" });
            Check(r.Item2 == null && r.Item1.Contains("could not be attached"), "attaching fails: warning, still cancelled");
            o = org(); var o3 = o; stub.OnSuccess = () => o3.Rows[approval]["statecode"] = new OptionSetValue(1);
            r = call(o, "Cancel", new[] { open(), caller, "  ", env, "TOK" });
            Check(r.Item1 == null && r.Item2 == null && !o.Log.Any(l => l.StartsWith("note")), "no comment: no attachment");
        }

        {
            var o = org(); o.Next = new EntityCollection(); var s = Activator.CreateInstance(svcType, F, null, new object[] { o }, null);
            svcType.GetMethod("GetPendingRequests").Invoke(s, new object[] { new List<Guid> { approval } });
            var link = o.Queries.Single().LinkEntities.Single();
            Check(link.LinkToEntityName == "msdyn_flow_approval" && link.LinkCriteria.Conditions.Single().AttributeName == "statecode" && (int)link.LinkCriteria.Conditions.Single().Values[0] == 0, "requests of abandoned or finished approvals are left out");
        }

        var custom = New("Services.PendingRequest", "Options", "[\"Hello\",\"world\"]"); var basic = New("Services.PendingRequest", "Options", "[\"Approve\",\"Reject\"]");
        var accepts = T("Services.PendingRequest").GetMethod("Accepts");
        Check(!(bool)accepts.Invoke(custom, new object[] { "Approve" }) && (bool)accepts.Invoke(basic, new object[] { "Reject" }), "custom-response approvals do not accept Approve/Reject");

        // ---- window
        var itemType = T("Controls.RespondDialog").GetNestedType("Item", F);
        var items = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
        items.Add(Activator.CreateInstance(itemType, basic, "Budget  —  Megan", true)); items.Add(Activator.CreateInstance(itemType, custom, "Trip  —  Sam", false));
        var dlg = (Form)Activator.CreateInstance(T("Controls.RespondDialog"), F, null, new object[] { "Approve 2 approvals", "Approve", "The response is sent from your own sign-in, in place of the approver's. It cannot be undone.", "Approve for", items, new List<string> { "Old one (not waiting on anyone)" } }, null);
        var h = dlg.Handle; dlg.PerformLayout();
        var all = Walk(dlg).ToList(); var box = all.OfType<TextBox>().Single(); var list = all.OfType<CheckedListBox>().Single(); var ok = all.OfType<Button>().First(b => b.Text == "Approve");
        Check(box.Multiline && box.AcceptsReturn && box.Top < list.Top && dlg.AcceptButton == null, "window: comment on top; Enter never approves by accident");
        Check(list.GetItemChecked(0) && !list.GetItemChecked(1) && ok.Enabled, "window: ticked items start ticked");
        list.SetItemChecked(0, false); Check(!ok.Enabled, "window: button off when nothing is ticked");
        list.SetItemChecked(1, true); Check(ok.Enabled, "window: button on again");
        list.SetItemChecked(0, true); list.SetItemChecked(1, false);
        var host = new Quiet { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000), ClientSize = new Size(700, 600), ShowInTaskbar = false };
        dlg.TopLevel = false; host.Controls.Add(dlg); host.Show(); dlg.Show(); Application.DoEvents();
        Check(all.OfType<Button>().All(b => b.Right <= b.Parent.ClientSize.Width && b.Bottom <= b.Parent.ClientSize.Height && b.Width > 20), "window: buttons fully visible");
        using (var shot = new Bitmap(dlg.Width, dlg.Height)) { dlg.DrawToBitmap(shot, new Rectangle(Point.Empty, dlg.Size)); shot.Save(System.IO.Path.Combine(args[1], "respond.png")); }
        dlg.Dispose();
        host.Dispose();

        // ---- main control
        var main = (Control)Activator.CreateInstance(T("ApprovalAdminControl"));
        var form = new Quiet { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000), ClientSize = new Size(1500, 700), ShowInTaskbar = false };
        main.Dock = DockStyle.Fill; form.Controls.Add(main); form.Show(); Application.DoEvents();
        var grid = (Control)main.GetType().GetField("dataGridControl1", F).GetValue(main); var panel = (Control)main.GetType().GetField("actionPanel1", F).GetValue(main);
        var buttons = new[] { "Approve", "Reject", "Reassign", "Cancel", "Remind" }.Select(n => Walk(panel).OfType<Button>().Single(b => b.Text == n)).ToList();
        Check(!Walk(panel).OfType<TextBox>().Any() && Walk(panel).OfType<Button>().Count(b => b.Visible) == 5, "first screen: just the five buttons, no comment box (Action is hidden until a custom-response approval is chosen)");
        Check(buttons.All(b => b.Right <= b.Parent.ClientSize.Width && b.Bottom <= b.Parent.ClientSize.Height && b.Top == buttons[0].Top), "all five buttons fit on one row");
        Check(buttons.All(b => !b.Enabled), "buttons off with no approvals loaded");
        var t = new DataTable(); t.Columns.Add("msdyn_flow_approvalid", typeof(Guid)); t.Columns.Add("msdyn_flow_approval_title"); t.Columns.Add("statuscode");
        foreach (var st in new[] { "192350001", "192350007", "192350004" }) t.Rows.Add(Guid.NewGuid(), "Status " + st, st);
        grid.GetType().GetMethod("ShowApprovals").Invoke(grid, new object[] { t });
        for (var i = 0; i < 10; i++) Application.DoEvents();
        var view = (DataGridView)grid.Controls[0];
        Func<int> ticks = () => ((IEnumerable<Guid>)grid.GetType().GetProperty("CheckedApprovalIds").GetValue(grid)).Count();
        Action<int> click = row => { grid.GetType().GetMethod("ApprovalGridView_CellMouseClick", F).Invoke(grid, new object[] { view, new DataGridViewCellMouseEventArgs(view.Columns["select"].Index, row, 5, 5, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0)) }); for (var i = 0; i < 10; i++) Application.DoEvents(); };
        Action<int> go = row => { view.CurrentCell = view.Rows[row].Cells["msdyn_flow_approval_title"]; for (var i = 0; i < 10; i++) Application.DoEvents(); };
        go(0); Check(buttons.All(b => b.Enabled), "pending row selected: buttons on");
        go(1); Check(buttons.All(b => !b.Enabled), "abandoned row selected: buttons off");
        go(2); Check(buttons.All(b => !b.Enabled), "completed row selected: buttons off");
        click(1); Check(ticks() == 0, "abandoned row cannot be ticked by clicking");
        go(1); grid.GetType().GetMethod("ApprovalGridView_KeyDown", F).Invoke(grid, new object[] { view, new KeyEventArgs(Keys.Space) }); Check(ticks() == 0, "nor with Space");
        click(-1); Check(ticks() == 1, "header ticks only the open one");
        go(1); Check(buttons.All(b => b.Enabled), "open one ticked: buttons on whatever row is current");
        using (var shot = new Bitmap(form.ClientSize.Width, form.ClientSize.Height)) { main.DrawToBitmap(shot, new Rectangle(Point.Empty, form.ClientSize)); shot.Save(System.IO.Path.Combine(args[1], "main.png")); }
        var done = t.Copy(); done.Rows[0]["statuscode"] = "192350007"; grid.GetType().GetMethod("ShowApprovals").Invoke(grid, new object[] { done }); for (var i = 0; i < 10; i++) Application.DoEvents();
        Check(ticks() == 0, "a ticked approval that becomes abandoned is unticked on reload");
        var enumType = T("Services.ApprovalAction"); var heard = new List<string>();
        var ev = panel.GetType().GetEvent("ActionRequested");
        var mi = typeof(ActionsTest).GetMethod("Hear", F).MakeGenericMethod(enumType); Heard = heard;
        // replace the main control's own handler so nothing tries to connect
        panel.GetType().GetField("ActionRequested", F).SetValue(panel, null);
        ev.AddEventHandler(panel, Delegate.CreateDelegate(ev.EventHandlerType, mi));
        grid.GetType().GetMethod("ShowApprovals").Invoke(grid, new object[] { t }); go(0);
        foreach (var b in buttons) b.PerformClick();
        Check(heard.SequenceEqual(new[] { "Approve", "Reject", "Reassign", "Cancel", "Remind" }), "each button raises its action: " + string.Join(",", heard));
        var small = (Control)Activator.CreateInstance(T("Controls.ActionPanel"));
        var sf = new Quiet { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000), ClientSize = new Size(239, 57), ShowInTaskbar = false };
        small.Dock = DockStyle.Fill; sf.Controls.Add(small); sf.Show(); Application.DoEvents();
        var sb = Walk(small).OfType<Button>().Where(b => b.Visible).ToList();
        foreach (var b in sb) Console.WriteLine("  " + b.Text + " " + b.Bounds + " text " + TextRenderer.MeasureText(b.Text, b.Font).Width);
        Check(sb.All(b => b.Right <= b.Parent.ClientSize.Width && b.Bottom <= b.Parent.ClientSize.Height && b.Top == sb[0].Top), "narrow panel: five buttons on one row");
        Check(sb.All(b => TextRenderer.MeasureText(b.Text, b.Font).Width <= b.Width), "narrow panel: labels not cut off (font " + sb[0].Font.Size + ")"); sf.ClientSize = new Size(600, 57); Application.DoEvents(); Check(sb.All(b => b.Font.Size == 12F), "wide again: normal font");  sf.ClientSize = new Size(239, 57); Application.DoEvents();
        using (var shot = new Bitmap(239, 57)) { small.DrawToBitmap(shot, new Rectangle(0, 0, 239, 57)); shot.Save(System.IO.Path.Combine(args[1], "narrow.png")); }
        sf.Close();
        form.Close();
        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }

    static List<string> Heard;
    static void Hear<TAction>(object sender, TAction action) => Heard.Add(action.ToString());
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Label = System.Windows.Forms.Label;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

// Custom responses, approvals that wait for everyone, the System Administrator check, the record of borrowed rows,
// and Remind (a Teams chat, or an email, through Microsoft Graph). Dataverse and the HTTP APIs are faked.

class Http : HttpMessageHandler
{
    public List<string> Log = new List<string>(); public List<string> Bodies = new List<string>();
    // Requests whose path starts with one of these are refused (403), like a sign-in without that Graph permission.
    public List<string> Refuse = new List<string>();
    // The one-on-one chat that already exists, if any (posting to any other chat ID gets 404).
    public string ExistingChat;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
    {
        var path = Uri.UnescapeDataString(r.RequestUri.AbsolutePath);
        Log.Add(r.Method + " " + path);
        Bodies.Add(r.Content == null ? "" : r.Content.ReadAsStringAsync().Result);
        HttpResponseMessage Reply(HttpStatusCode code, string json) => new HttpResponseMessage(code) { Content = new StringContent(json) };
        if (Refuse.Any(prefix => path.StartsWith(prefix)))
            return Task.FromResult(Reply(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"Forbidden\",\"message\":\"Missing scope permissions on the request.\"}}"));
        if (path == "/v1.0/chats")
            return Task.FromResult(Reply(HttpStatusCode.Created, "{\"@odata.context\":\"x\",\"id\":\"19:abc_def@unq.gbl.spaces\",\"chatType\":\"oneOnOne\"}"));
        if (path.StartsWith("/v1.0/chats/") && path != "/v1.0/chats/19:abc_def@unq.gbl.spaces/messages" && path != "/v1.0/chats/" + ExistingChat + "/messages")
            return Task.FromResult(Reply(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"NotFound\",\"message\":\"Chat not found\"}}"));
        if (path.StartsWith("/v1.0/users/"))
            return Task.FromResult(Reply(HttpStatusCode.OK, "{\"mail\":\"ben@contoso.test\",\"userPrincipalName\":\"ben@contoso.test\"}"));
        if (path == "/v1.0/me/sendMail")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        return Task.FromResult(Reply(HttpStatusCode.Created, "{}"));
    }
}

// One "everyone must approve" approval with Ann (answered Approve) and Ben (waiting), plus admin role data.
class Org : IOrganizationService
{
    public Guid Approval = Guid.NewGuid(), Ann = Guid.NewGuid(), Ben = Guid.NewGuid(), Admin = Guid.NewGuid(), RAnn = Guid.NewGuid(), RBen = Guid.NewGuid();
    public bool AdminDirect, AdminThroughTeam; public List<string> Log = new List<string>(); public Dictionary<Guid, Guid> Owners = new Dictionary<Guid, Guid>();
    public int Responses;
    Entity Req(Guid id, Guid owner)
    {
        var e = new Entity("msdyn_flow_approvalrequest", id);
        e["msdyn_flow_approvalrequest_approval"] = new EntityReference("msdyn_flow_approval", Approval);
        e["ownerid"] = new EntityReference("systemuser", Owners.TryGetValue(id, out var o) ? o : owner) { Name = owner == Ann ? "Ann" : "Ben" };
        e["msdyn_flow_approvalrequest_stepnumber"] = 0;
        e["msdyn_flow_approvalrequest_options"] = "[\"Approve\",\"Reject\"]";
        e["statecode"] = new OptionSetValue(0);
        e["approval.msdyn_flow_approval_currentstepnumber"] = new AliasedValue("msdyn_flow_approval", "msdyn_flow_approval_currentstepnumber", 0);
        e["approval.msdyn_flow_approval_modeltype"] = new AliasedValue("msdyn_flow_approval", "msdyn_flow_approval_modeltype", "msdyn_flow_awaitallapprovalmodel");
        return e;
    }
    public Entity Retrieve(string n, Guid id, ColumnSet c)
    {
        if (n == "msdyn_flow_approvalrequest") return Req(id, id == RAnn ? Ann : Ben);
        if (n == "systemuser") { var u = new Entity(n, id); u["fullname"] = "Ada Admin"; return u; }
        var e = new Entity(n, id); e["ownerid"] = new EntityReference("systemuser", Ann); e["msdyn_flow_approval_currentstepnumber"] = 0; return e;
    }
    public EntityCollection RetrieveMultiple(QueryBase q)
    {
        var x = (QueryExpression)q;
        if (x.EntityName == "msdyn_flow_approvalrequest")
        {
            if (x.Criteria.Conditions.Any(c => c.AttributeName == "ownerid")) return new EntityCollection(); // no other request of the caller
            return new EntityCollection(new List<Entity> { Req(RAnn, Ann), Req(RBen, Ben) });
        }
        if (x.EntityName == "msdyn_flow_approvalresponse")
        {
            if (x.Criteria.Conditions.Any(c => c.AttributeName == "ownerid")) { var list = new EntityCollection(); for (var i = 0; i < Responses; i++) list.Entities.Add(new Entity(x.EntityName, Guid.NewGuid())); return list; }
            var r = new Entity(x.EntityName, Guid.NewGuid());
            r["msdyn_flow_approvalresponse_approval"] = new EntityReference("msdyn_flow_approval", Approval);
            r["msdyn_flow_approvalresponse_stepnumber"] = 0; r["msdyn_flow_approvalresponse_response"] = "Approve"; r["ownerid"] = new EntityReference("systemuser", Ann);
            return new EntityCollection(new List<Entity> { r });
        }
        if (x.EntityName == "role")
        {
            var viaTeam = x.LinkEntities[0].LinkToEntityName == "teamroles";
            return (viaTeam ? AdminThroughTeam : AdminDirect) ? new EntityCollection(new List<Entity> { new Entity("role", Guid.NewGuid()) }) : new EntityCollection();
        }
        if (x.EntityName == "systemuser")
        {
            var ids = x.Criteria.Conditions.First(c => c.AttributeName == "systemuserid").Values.Cast<Guid>();
            return new EntityCollection(ids.Where(id => id != Ben).Select(id => { var u = new Entity("systemuser", id); u["azureactivedirectoryobjectid"] = new Guid(id.ToByteArray().Reverse().ToArray()); return u; }).ToList());
        }
        return new EntityCollection();
    }
    public void Update(Entity e) { Log.Add("Update " + e.LogicalName + " owner " + ((EntityReference)e["ownerid"]).Id); Owners[e.Id] = ((EntityReference)e["ownerid"]).Id; if (e.LogicalName == "msdyn_flow_approvalrequest" && Owners[e.Id] == Admin) Responses++; }
    public Guid Create(Entity e) => throw new NotImplementedException();
    public void Delete(string a, Guid b) => throw new NotImplementedException();
    public OrganizationResponse Execute(OrganizationRequest r)
    {
        if (r is WhoAmIRequest) { var w = new WhoAmIResponse(); w.Results["UserId"] = Admin; w.Results["BusinessUnitId"] = Guid.Empty; w.Results["OrganizationId"] = Guid.Empty; return w; }
        throw new NotImplementedException(r.RequestName);
    }
    public void Associate(string a, Guid b, Relationship c, EntityReferenceCollection d) => throw new NotImplementedException();
    public void Disassociate(string a, Guid b, Relationship c, EntityReferenceCollection d) => throw new NotImplementedException();
}

static class NewFeaturesTest
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    static IEnumerable<Control> Walk(Control c) { foreach (Control x in c.Controls) { yield return x; foreach (var y in Walk(x)) yield return y; } }
    public static object HeardAction;
    public static void Heard(object sender, object action) { HeardAction = action; }

    public static List<string> JournalLog = new List<string>();
    static void Shot(Control c, string folder, string name)
    {
        c.PerformLayout();
        using (var bmp = new System.Drawing.Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height))) { c.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, c.Width, c.Height)); bmp.Save(Path.Combine(folder, name)); }
    }

    [STAThread]
    static int Main(string[] args)
    {
        var plugin = Assembly.LoadFrom(args[0]);
        Func<string, Type> T = n => plugin.GetType("ApprovalAdministrationTool." + n, true);
        var reqType = T("Services.PendingRequest"); var svcType = T("Services.ApprovalActionService"); var userType = T("Services.UserOption");

        // ---- custom responses
        Func<string, object> req = options => { var r = Activator.CreateInstance(reqType); reqType.GetProperty("Options").SetValue(r, options); return r; };
        Func<object, bool> isCustom = r => (bool)reqType.GetProperty("IsCustom").GetValue(r);
        Func<object, IEnumerable<string>> choices = r => (IEnumerable<string>)reqType.GetProperty("Choices").GetValue(r);
        var basic = req("[\"Approve\",\"Reject\"]"); var custom = req("[\"Yes, book it\",\"Not this \\\"time\\\"\",\"Ask me later\"]");
        Check(!isCustom(basic) && !isCustom(req(null)) && !isCustom(req("[\"reject\",\"approve\"]")), "Approve/Reject approvals are not custom (any order or case, or no options stored)");
        Check(isCustom(custom) && choices(custom).SequenceEqual(new[] { "Yes, book it", "Not this \"time\"", "Ask me later" }), "custom responses are read from the JSON list: " + string.Join(" | ", choices(custom)));
        Check(isCustom(req("[\"Approve\"]")) && !isCustom(req("not json")), "only Approve is custom; unreadable options count as Approve/Reject");

        var itemType = T("Controls.RespondDialog").GetNestedType("Item", F);
        var items = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
        items.Add(Activator.CreateInstance(itemType, basic, "Budget  —  Megan", true));
        items.Add(Activator.CreateInstance(itemType, custom, "Trip  —  Sam", true, choices(custom).ToList(), "Approve"));
        var dlg = (Form)Activator.CreateInstance(T("Controls.RespondDialog"), F, null, new object[] { "Approve 2 approvals", "Approve", "explained", "Approve for", items, new List<string>(), new List<string> { "Everyone must respond to \"Budget\": Ann: responded Approve; Ben: waiting." } }, null);
        var h = dlg.Handle; var all = Walk(dlg).ToList(); var ok = all.OfType<Button>().First(b => b.Text == "Approve");
        var combo = all.OfType<ComboBox>().SingleOrDefault();
        Check(combo != null && combo.DropDownStyle == ComboBoxStyle.DropDownList && combo.Items.Count == 3 && combo.SelectedItem == null, "a dropdown of the custom responses, nothing picked when Approve is not one of them");
        Check(!ok.Enabled && ((string)dlg.GetType().GetProperty("WhyNotReady").GetValue(dlg)).Contains("Trip"), "Approve is off until a response is picked for the custom approval");
        combo.SelectedIndex = 0;
        Check(ok.Enabled && (string)dlg.GetType().GetMethod("Response").Invoke(dlg, new[] { custom }) == "Yes, book it", "picking one turns it on, and the picked response is what is sent");
        dlg.StartPosition = FormStartPosition.Manual; dlg.Location = new System.Drawing.Point(-5000, -5000); dlg.ShowInTaskbar = false; dlg.Show(); Application.DoEvents();
        Shot(dlg, args[1], "respond_custom.png");
        var list = all.OfType<CheckedListBox>().Single(); list.SetItemChecked(1, false); combo.SelectedIndex = -1;
        Check(ok.Enabled, "untick the custom approval: no response needed for it");
        Check(all.OfType<Label>().Any(l => l.Text.Contains("Ben: waiting")), "the note on who has and has not responded is shown");
        dlg.GetType().GetProperty("Comments").SetValue(dlg, "Approved by Ada Admin (administrator)");
        Check(all.OfType<TextBox>().Single().Text.StartsWith("Approved by Ada Admin"), "the comment can start filled in");
        dlg.Dispose();
        var preset = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
        preset.Add(Activator.CreateInstance(itemType, custom, "Trip", true, new List<string> { "Approve", "Escalate" }, "approve"));
        var dlg2 = (Form)Activator.CreateInstance(T("Controls.RespondDialog"), F, null, new object[] { "t", "Approve", "e", "h", preset, new List<string>() }, null);
        h = dlg2.Handle;
        Check((string)Walk(dlg2).OfType<ComboBox>().Single().SelectedItem == "Approve" && Walk(dlg2).OfType<Button>().First(b => b.Text == "Approve").Enabled, "the response named like the button is picked to start with");
        dlg2.Dispose();

        // ---- the Action window: the button names the response picked
        var acting = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
        acting.Add(Activator.CreateInstance(itemType, custom, "Trip  —  Sam", true, choices(custom).ToList(), null));
        acting.Add(Activator.CreateInstance(itemType, basic, "Budget  —  Megan", true, choices(basic).ToList(), null));
        var dlg3 = (Form)Activator.CreateInstance(T("Controls.RespondDialog"), F, null, new object[] { "Respond to 2 approvals", "Respond", "e", "Respond for", acting, new List<string>() }, null);
        h = dlg3.Handle;
        dlg3.GetType().GetProperty("ButtonNamesResponse").SetValue(dlg3, true);
        dlg3.GetType().GetProperty("ResponseHeading").SetValue(dlg3, "Response");
        var boxes = Walk(dlg3).OfType<ComboBox>().ToList();
        var send = Walk(dlg3).OfType<Button>().Single(b => b.DialogResult == DialogResult.OK);
        Check(boxes.Count == 2 && boxes.All(b => b.SelectedItem == null) && !send.Enabled && send.Text == "Respond", "every approval gets a dropdown, nothing picked, the button says Respond and is off");
        Check(Walk(dlg3).OfType<Label>().Any(l => l.Text == "Response"), "the dropdowns' heading is just Response");
        boxes[0].SelectedIndex = 0; boxes[1].SelectedItem = "Approve";
        Check(send.Enabled && send.Text == "Send responses", "different responses picked: the button says Send responses (" + send.Text + ")");
        Walk(dlg3).OfType<CheckedListBox>().Single().SetItemChecked(1, false);
        Check(send.Text == "Yes, book it", "one response picked: the button is that response (" + send.Text + ")");
        dlg3.Dispose();

        // ---- the panel: Action instead of Approve and Reject for custom responses
        var panel = (Control)Activator.CreateInstance(T("Controls.ActionPanel"));
        panel.Size = new System.Drawing.Size(640, 70); h = panel.Handle;
        panel.GetType().GetProperty("ActionsEnabled").SetValue(panel, true);
        Func<string, Button> btn = text => Walk(panel).OfType<Button>().Single(b => b.Text == text);
        Check(btn("Approve").Visible && btn("Reject").Visible && !btn("Action").Visible, "Approve and Reject show for Approve/Reject approvals");
        var fiveWide = btn("Reassign").Width;
        panel.GetType().GetProperty("CustomResponses").SetValue(panel, true);
        Check(!btn("Approve").Visible && !btn("Reject").Visible && btn("Action").Visible && btn("Action").Enabled, "custom responses: one Action button instead");
        Check(btn("Action").Width > fiveWide && btn("Reassign").Width == btn("Action").Width, "the four buttons on show share the width (" + fiveWide + " -> " + btn("Action").Width + ")");
        object heardAction = null;
        var ev = panel.GetType().GetEvent("ActionRequested"); var actType = ev.EventHandlerType.GetGenericArguments()[0];
        var ps = new[] { System.Linq.Expressions.Expression.Parameter(typeof(object)), System.Linq.Expressions.Expression.Parameter(actType) };
        ev.AddEventHandler(panel, System.Linq.Expressions.Expression.Lambda(ev.EventHandlerType,
            System.Linq.Expressions.Expression.Call(typeof(NewFeaturesTest).GetMethod("Heard"), ps[0], System.Linq.Expressions.Expression.Convert(ps[1], typeof(object))), ps).Compile());
        btn("Action").PerformClick(); heardAction = HeardAction;
        Check(heardAction != null && heardAction.ToString() == "Respond", "Action raises Respond: " + heardAction);
        panel.Dispose();

        // ---- approvals that wait for everyone
        var org = new Org();
        var svc = Activator.CreateInstance(svcType, F, null, new object[] { org }, null);
        var pending = ((System.Collections.IEnumerable)svcType.GetMethod("GetPendingRequests").Invoke(svc, new object[] { new List<Guid> { org.Approval } })).Cast<object>().ToList();
        Check(pending.Count == 1 && (Guid)reqType.GetProperty("RequestId").GetValue(pending[0]) == org.RBen, "Ann already responded, so only Ben's request is left to act on");
        var summary = (string)reqType.GetProperty("ResponseSummary").GetValue(pending[0]);
        Check((bool)reqType.GetProperty("WaitsForAll").GetValue(pending[0]) && summary == "Ann: responded Approve; Ben: waiting", "who has responded and who has not: " + summary);

        // ---- System Administrator check
        Func<object> access = () => svcType.GetMethod("GetAdminAccess").Invoke(svc, null);
        Func<object, bool> isAdmin = a => (bool)a.GetType().GetProperty("IsSystemAdministrator").GetValue(a);
        var a0 = access();
        Check(!isAdmin(a0) && (string)a0.GetType().GetProperty("FullName").GetValue(a0) == "Ada Admin", "no System Administrator role: not an admin (and the name is read for the default comment)");
        org.AdminDirect = true; Check(isAdmin(access()), "System Administrator role given directly: admin");
        org.AdminDirect = false; org.AdminThroughTeam = true; Check(isAdmin(access()), "System Administrator role through a team: admin");

        var actions = (UserControl)Activator.CreateInstance(T("Controls.ActionsControl"));
        actions.Size = new System.Drawing.Size(540, 57); h = actions.Handle; actions.PerformLayout();
        actions.GetType().GetProperty("ActionsEnabled").SetValue(actions, true);
        actions.GetType().GetProperty("DisabledReason").SetValue(actions, "You must be a System Administrator for this environment to use these functions.");
        var reason = Walk(actions).OfType<Label>().Single();
        Shot(actions, args[1], "not_admin.png");
        Check(Walk(actions).OfType<Button>().All(b => !b.Enabled) && reason.Visible && reason.Text.StartsWith("You must be a System Administrator"), "not an admin: every button off, with the reason under them");
        actions.GetType().GetProperty("DisabledReason").SetValue(actions, null);
        Check(Walk(actions).OfType<Button>().All(b => b.Enabled) && !reason.Visible, "admin: buttons follow the selection again, no reason shown");
        var reasonText = T("ApprovalAdminControl").GetField("NotAdministratorReason", F).GetValue(null) as string;
        Check(reasonText == "You must be a System Administrator for this environment to use these functions.", "the reason in the tool: " + reasonText);
        actions.Dispose();

        // ---- borrowed rows: recorded before the row is assigned, removed once it is back
        var journalType = T("Services.IBorrowJournal");
        var journal = Recorder.Make(journalType);
        svcType.GetProperty("Journal").SetValue(svc, journal);
        svcType.GetField("waitSeconds", F).SetValue(null, 0);
        var http = new Http();
        svcType.GetField("Http", F).SetValue(null, new HttpClient(http) { BaseAddress = new Uri("https://approvals.teams.microsoft.com") });
        var admin = Activator.CreateInstance(userType); userType.GetProperty("Id").SetValue(admin, org.Admin); userType.GetProperty("Name").SetValue(admin, "Ada Admin");
        var env = Activator.CreateInstance(T("Services.EnvironmentInfo")); env.GetType().GetProperty("EnvironmentId").SetValue(env, "env-1");
        reqType.GetProperty("ApprovalTitle").SetValue(pending[0], "Budget");
        JournalLog.Clear(); org.Log.Clear();
        var warning = svcType.GetMethod("Respond").Invoke(svc, new[] { pending[0], "Approve", admin, "Approved by Ada Admin", env, "TOKEN" });
        Check(JournalLog.Count == 2 && JournalLog[0].StartsWith("Borrowed " + org.RBen + " from " + org.Ben + " to " + org.Admin + " Budget") && JournalLog[1] == "Returned " + org.RBen,
            "responding records the borrowed request first and forgets it once given back: " + string.Join(" / ", JournalLog));
        Check(org.Log.Count == 2 && org.Log[0].EndsWith(org.Admin.ToString()) && org.Log[1].EndsWith(org.Ben.ToString()), "the request went to the admin and back to Ben: " + string.Join(" / ", org.Log));
        Check(http.Bodies.Last().Contains("\"Response\":\"Approve\"") && http.Bodies.Last().Contains("Approved by Ada Admin"), "Teams was sent the response and the default comment");

        // a custom response is sent as typed
        http.Bodies.Clear(); org.Owners.Clear(); reqType.GetProperty("Options").SetValue(pending[0], "[\"Yes, book it\",\"No\"]");
        svcType.GetMethod("Respond").Invoke(svc, new[] { pending[0], "Yes, book it", admin, "", env, "TOKEN" });
        Check(http.Bodies.Last().Contains("\"Response\":\"Yes, book it\""), "a custom response is sent to Teams as picked");

        // leftovers
        var row = new EntityReference("msdyn_flow_approvalrequest", org.RBen); var ben = new EntityReference("systemuser", org.Ben) { Name = "Ben" };
        org.Owners[org.RBen] = org.Admin;
        var said = (string)svcType.GetMethod("GiveBackLeftover").Invoke(svc, new object[] { row, ben, org.Admin });
        Check(said == "given back to Ben" && org.Owners[org.RBen] == org.Ben, "a row left with the admin is given back: " + said);
        said = (string)svcType.GetMethod("GiveBackLeftover").Invoke(svc, new object[] { row, ben, org.Admin });
        Check(said == "already back with Ben", "one already back is left alone: " + said);
        org.Owners[org.RBen] = Guid.NewGuid();
        said = (string)svcType.GetMethod("GiveBackLeftover").Invoke(svc, new object[] { row, ben, org.Admin });
        Check(said.StartsWith("now owned by someone else"), "one someone else has taken since is left alone: " + said);

        var settingsType = T("Settings"); var rowType = T("BorrowedRow");
        var settings = Activator.CreateInstance(settingsType);
        var rows = (System.Collections.IList)settingsType.GetProperty("BorrowedRows").GetValue(settings);
        var saved = Activator.CreateInstance(rowType); rowType.GetProperty("RowId").SetValue(saved, org.RBen); rowType.GetProperty("Title").SetValue(saved, "Budget"); rowType.GetProperty("BorrowedOn").SetValue(saved, new DateTime(2026, 11, 28, 1, 2, 3, DateTimeKind.Utc)); rows.Add(saved);
        var xml = new StringWriter(); new XmlSerializer(settingsType).Serialize(xml, settings);
        var back = new XmlSerializer(settingsType).Deserialize(new StringReader(xml.ToString()));
        var backRows = (System.Collections.IList)settingsType.GetProperty("BorrowedRows").GetValue(back);
        Check(backRows.Count == 1 && (Guid)rowType.GetProperty("RowId").GetValue(backRows[0]) == org.RBen, "borrowed rows are saved with the settings (XML) and read back");

        // ---- Remind: a Teams chat (the existing one, else a new one), else an email
        var remType = T("Services.ReminderService");
        var adminAad = Guid.NewGuid(); var benAad = Guid.NewGuid();
        var rem = Activator.CreateInstance(remType, "GRAPH");
        var link = (string)remType.GetMethod("ReceivedApprovalsLink").Invoke(null, new object[] { "env-1" });
        string lo = adminAad.ToString().ToLowerInvariant(), hi = benAad.ToString().ToLowerInvariant();
        if (string.CompareOrdinal(lo, hi) > 0) { var t = lo; lo = hi; hi = t; }
        Http Graph(Action<Http> setup) { var h = new Http(); setup(h); remType.GetField("Http", F).SetValue(null, new HttpClient(h) { BaseAddress = new Uri("https://graph.microsoft.com") }); return h; }
        object Remind() => remType.GetMethod("Send").Invoke(rem, new object[] { adminAad, benAad, "Budget <Q3>", "Please look\ntoday", link });

        // The chat the two already share: posted in straight away, without needing Chat.Create.
        var graph = Graph(h => { h.ExistingChat = "19:" + lo + "_" + hi + "@unq.gbl.spaces"; h.Refuse.Add("/v1.0/chats/19:abc"); h.Refuse.Add("/v1.0/me"); });
        Check(Remind() == null, "a Teams chat is reported as sent (no warning)");
        Check(graph.Log.SequenceEqual(new[] { "POST /v1.0/chats/19:" + lo + "_" + hi + "@unq.gbl.spaces/messages" }), "posts in the chat they already share, found from their two IDs: " + string.Join(" / ", graph.Log));
        Check(graph.Bodies[0].Contains("Budget &lt;Q3&gt;") && graph.Bodies[0].Contains("Please look<br>today") && graph.Bodies[0].Contains("https://make.powerautomate.com/environments/env-1/approvals/received") && graph.Bodies[0].Contains("\"contentType\":\"html\""),
            "the message has the title (HTML-safe), the note and the link");

        // The ID the other way round also works.
        graph = Graph(h => h.ExistingChat = "19:" + hi + "_" + lo + "@unq.gbl.spaces");
        Check(Remind() == null && graph.Log.Count == 2 && graph.Log[1].Contains("19:" + hi + "_" + lo), "tries the chat ID the other way round too: " + string.Join(" / ", graph.Log));

        // No chat yet, Chat.Create allowed: creates it, then posts.
        graph = Graph(h => { });
        Check(Remind() == null, "a new chat is reported as sent");
        Check(graph.Log.Skip(2).SequenceEqual(new[] { "POST /v1.0/chats", "POST /v1.0/chats/19:abc_def@unq.gbl.spaces/messages" }), "with no chat yet, opens one and posts in it: " + string.Join(" / ", graph.Log));
        Check(graph.Bodies[2].Contains("\"chatType\":\"oneOnOne\"") && graph.Bodies[2].Contains("users('" + adminAad + "')") && graph.Bodies[2].Contains("users('" + benAad + "')"), "the new chat is between the admin and the approver");

        // The sign-in in the field: no Chat.Create and no chat yet, but Mail.Send: emailed instead.
        graph = Graph(h => h.Refuse.Add("/v1.0/chats"));
        var emailed = (string)Remind();
        Check(emailed != null && emailed.Contains("emailed to ben@contoso.test"), "when no chat is allowed, emails the approver and says so: " + emailed);
        Check(graph.Log.Last() == "POST /v1.0/me/sendMail" && graph.Log.Contains("GET /v1.0/users/" + benAad), "looks up the approver's address, then sends from the admin's mailbox: " + string.Join(" / ", graph.Log));
        var mailBody = graph.Bodies.Last();
        Check(mailBody.Contains("\"address\":\"ben@contoso.test\"") && mailBody.Contains("Reminder: Budget <Q3> is waiting on your response") && mailBody.Contains("Budget &lt;Q3&gt;"), "the email has the subject, the same message and the approver's address");

        // Nothing allowed: one error listing each refusal, nothing sent.
        graph = Graph(h => { h.Refuse.Add("/v1.0/chats"); h.Refuse.Add("/v1.0/me"); });
        try { Remind(); Check(false, "refused everywhere is an error"); }
        catch (TargetInvocationException ex)
        {
            var m = ex.InnerException.Message;
            Check(m.Contains("nothing was sent") && m.Contains("create chat: 403") && m.Contains("email: 403") && m.Contains("Missing scope"), "refused everywhere is one error listing each refusal: " + m.Replace("\n", " | "));
        }

        try { remType.GetMethod("Send").Invoke(rem, new object[] { adminAad, adminAad, "x", "", link }); Check(false, "reminding yourself is refused"); }
        catch (TargetInvocationException ex) { Check(ex.InnerException.Message.Contains("there is no one to remind"), "reminding yourself is refused: " + ex.InnerException.Message); }
        var ids = (System.Collections.IDictionary)svcType.GetMethod("GetEntraObjectIds").Invoke(svc, new object[] { new[] { org.Admin, org.Ben } });
        Check(ids.Contains(org.Admin) && !ids.Contains(org.Ben), "Entra ID object IDs are looked up; someone without one is left out");

        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }
}

// Records IBorrowJournal calls (the interface is internal to the plugin, so it is implemented with a DispatchProxy-style
// type emitted at run time).
public static class Recorder
{
    public static object Make(Type iface)
    {
        var asm = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Recorder"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
        var mod = asm.DefineDynamicModule("Recorder");
        // The plugin's internal interface can only be implemented by an assembly it trusts; IgnoresAccessChecksTo makes the runtime allow it.
        var ignore = typeof(Recorder).Assembly.GetType("System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute");
        asm.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(ignore.GetConstructor(new[] { typeof(string) }), new object[] { iface.Assembly.GetName().Name }));
        var tb = mod.DefineType("JournalRecorder", TypeAttributes.Public | TypeAttributes.Class, typeof(object), new[] { iface });
        foreach (var m in iface.GetMethods())
        {
            var ps = m.GetParameters();
            var mb = tb.DefineMethod(m.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final, m.ReturnType, ps.Select(p => p.ParameterType).ToArray());
            var il = mb.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldstr, m.Name);
            il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, ps.Length);
            il.Emit(System.Reflection.Emit.OpCodes.Newarr, typeof(object));
            for (var i = 0; i < ps.Length; i++)
            {
                il.Emit(System.Reflection.Emit.OpCodes.Dup); il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, i); il.Emit(System.Reflection.Emit.OpCodes.Ldarg, i + 1);
                if (ps[i].ParameterType.IsValueType) il.Emit(System.Reflection.Emit.OpCodes.Box, ps[i].ParameterType);
                il.Emit(System.Reflection.Emit.OpCodes.Stelem_Ref);
            }
            il.Emit(System.Reflection.Emit.OpCodes.Call, typeof(Recorder).GetMethod("Called"));
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            tb.DefineMethodOverride(mb, m);
        }
        return Activator.CreateInstance(tb.CreateType());
    }

    public static void Called(string name, object[] args)
    {
        string Id(object o) => o is EntityReference r ? r.Id.ToString() : o?.GetType().GetProperty("Id")?.GetValue(o)?.ToString() ?? o?.ToString();
        NewFeaturesTest.JournalLog.Add(name == "Borrowed" ? "Borrowed " + Id(args[0]) + " from " + Id(args[1]) + " to " + Id(args[2]) + " " + args[3] : "Returned " + Id(args[0]));
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName) { AssemblyName = assemblyName; }
        public string AssemblyName { get; }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

// In-memory rows by table; queries filter on simple Equal/In conditions of the main table.
class Db : IOrganizationService
{
    public List<Entity> Rows = new List<Entity>(); public List<QueryExpression> Queries = new List<QueryExpression>(); public List<string> Log = new List<string>();
    public Func<QueryExpression, EntityCollection> Override;
    public Entity Retrieve(string n, Guid id, ColumnSet c) { var r = Rows.Single(x => x.LogicalName == n && x.Id == id); var e = new Entity(n, id); foreach (var k in c.Columns) if (r.Contains(k)) e[k] = r[k]; return e; }
    public EntityCollection RetrieveMultiple(QueryBase q)
    {
        var x = (QueryExpression)q; Queries.Add(x); if (Override != null) return Override(x);
        var rows = Rows.Where(r => r.LogicalName == x.EntityName && x.Criteria.Conditions.All(c => Match(r, c))).ToList();
        return new EntityCollection(rows);
    }
    static bool Match(Entity r, ConditionExpression c)
    {
        object v = r.Contains(c.AttributeName) ? r[c.AttributeName] : (c.AttributeName == r.LogicalName + "id" ? (object)r.Id : null);
        if (v is EntityReference er) v = er.Id; if (v is OptionSetValue o) v = o.Value;
        switch (c.Operator) { case ConditionOperator.Equal: return Equals(v, c.Values[0]); case ConditionOperator.In: return c.Values.Any(y => Equals(v, y)); default: return true; }
    }
    public void Update(Entity e) { Log.Add("update " + e.LogicalName); }
    public Guid Create(Entity e) => throw new NotImplementedException();
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

static class FlowTest
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    static Assembly plugin;
    static Type T(string n) => plugin.GetType("ApprovalAdministrationTool." + n, true);
    static object New(string type, params object[] props) { var o = Activator.CreateInstance(T(type)); for (var i = 0; i < props.Length; i += 2) o.GetType().GetProperty((string)props[i]).SetValue(o, props[i + 1]); return o; }
    static object P(object o, string name) => o.GetType().GetProperty(name).GetValue(o);
    static object Make(string type, object service) => Activator.CreateInstance(T(type), F, null, new[] { service }, null);

    static Guid approval = Guid.NewGuid(), tony = Guid.NewGuid(), diego = Guid.NewGuid(), alex = Guid.NewGuid(), grady = Guid.NewGuid(), megan = Guid.NewGuid();
    static Guid r0 = Guid.NewGuid(), r1 = Guid.NewGuid(), r1old = Guid.NewGuid(), r2 = Guid.NewGuid();

    static Entity Request(Guid id, Guid owner, int step, int status = 1, Guid? from = null)
    {
        var e = new Entity("msdyn_flow_approvalrequest", id);
        e["msdyn_flow_approvalrequest_approval"] = new EntityReference("msdyn_flow_approval", approval) { Name = "Seq" };
        e["ownerid"] = new EntityReference("systemuser", owner); e["statecode"] = new OptionSetValue(0); e["statuscode"] = new OptionSetValue(status);
        e["msdyn_flow_approvalrequest_stepnumber"] = step; e["createdon"] = DateTime.UtcNow.AddMinutes(step);
        if (from.HasValue) e["msdyn_flow_approvalrequest_reassignedfrom"] = new EntityReference("msdyn_flow_approvalrequest", from.Value);
        return e;
    }

    static Entity User(Guid id, string name, string title) { var u = new Entity("systemuser", id); u["fullname"] = name; if (title != null) u["title"] = title; u["azureactivedirectoryobjectid"] = Guid.NewGuid(); return u; }

    static Db Sequential(int currentStep)
    {
        var db = new Db();
        var a = new Entity("msdyn_flow_approval", approval); a["ownerid"] = new EntityReference("systemuser", tony); a["statecode"] = new OptionSetValue(0); a["msdyn_flow_approval_currentstepnumber"] = currentStep;
        db.Rows.Add(a);
        db.Rows.Add(Request(r0, diego, 0)); db.Rows.Add(Request(r1old, megan, 1, 192350000)); db.Rows.Add(Request(r1, alex, 1, 1, r1old)); db.Rows.Add(Request(r2, grady, 2));
        var resp = new Entity("msdyn_flow_approvalresponse", Guid.NewGuid()); resp["msdyn_flow_approvalresponse_approval"] = new EntityReference("msdyn_flow_approval", approval);
        resp["ownerid"] = new EntityReference("systemuser", tony); resp["msdyn_flow_approvalresponse_stepnumber"] = 0; resp["msdyn_flow_approvalresponse_response"] = "Approve"; db.Rows.Add(resp);
        db.Rows.Add(User(tony, "Tony Dai", null)); db.Rows.Add(User(diego, "Diego Siciliani", "HR Manager")); db.Rows.Add(User(alex, "Alex Wilber", "Marketing Assistant"));
        db.Rows.Add(User(grady, "Grady Archie", "Designer")); db.Rows.Add(User(megan, "Megan Bowen", "Marketing Manager"));
        return db;
    }

    // What the link to the approval hands back: each request with its approval's current step as an aliased value.
    static EntityCollection WithCurrentStep(Db db, QueryExpression q, int current)
    {
        var link = q.LinkEntities.Single();
        var rows = db.Rows.Where(r => r.LogicalName == "msdyn_flow_approvalrequest").Select(r =>
        {
            var e = new Entity(r.LogicalName, r.Id); foreach (var c in q.ColumnSet.Columns) if (r.Contains(c)) e[c] = r[c];
            e[link.EntityAlias + ".msdyn_flow_approval_currentstepnumber"] = new AliasedValue("msdyn_flow_approval", "msdyn_flow_approval_currentstepnumber", current);
            return e;
        }).Where(r => ((OptionSetValue)db.Rows.Single(x => x.Id == r.Id)["statuscode"]).Value != 192350000);
        return new EntityCollection(rows.ToList());
    }

    [STAThread]
    static int Main(string[] args)
    {
        plugin = Assembly.LoadFrom(args[0]);

        // ---- step filter: who it waits on and what actions act on
        {
            var db = Sequential(1); db.Override = q => WithCurrentStep(db, q, 1);
            var pending = (System.Collections.IList)T("Services.ApprovalActionService").GetMethod("GetPendingRequests").Invoke(Make("Services.ApprovalActionService", db), new object[] { new List<Guid> { approval } });
            Check(pending.Count == 1 && ((EntityReference)P(pending[0], "Approver")).Id == alex && (Guid)P(pending[0], "RequestId") == r1, "actions only get the current step's request (step 2: Alex), not steps 1 and 3");
            var link = db.Queries.Last().LinkEntities.Single();
            Check(link.LinkCriteria.Conditions.Single().AttributeName == "statecode" && link.Columns.Columns.Contains("msdyn_flow_approval_currentstepnumber") && db.Queries.Last().ColumnSet.Columns.Contains("msdyn_flow_approvalrequest_stepnumber"), "query reads both step numbers and still skips finished approvals");

            db.Queries.Clear();
            var approvers = (System.Collections.IDictionary)T("Services.ApprovalService").GetMethod("GetCurrentApprovers", F).Invoke(Make("Services.ApprovalService", db), null);
            var list = (List<EntityReference>)approvers[approval];
            Check(list.Count == 1 && list[0].Id == alex, "Currently With lists only the current step's approver");
        }
        {
            var db = new Db(); db.Override = q =>
            {
                var e = new Entity("msdyn_flow_approvalrequest", Guid.NewGuid()); e["msdyn_flow_approvalrequest_approval"] = new EntityReference("msdyn_flow_approval", approval); e["ownerid"] = new EntityReference("systemuser", alex);
                return new EntityCollection(new List<Entity> { e });
            };
            var approvers = (System.Collections.IDictionary)T("Services.ApprovalService").GetMethod("GetCurrentApprovers", F).Invoke(Make("Services.ApprovalService", db), null);
            Check(approvers.Count == 1, "requests without step numbers (single-step approvals) still count");
        }

        // ---- re-checks before acting
        {
            var steps = T("Services.ApprovalSteps");
            var stale = Sequential(1); // Diego's step 1 request is still active, but the approval moved on to step 2
            var request = New("Services.PendingRequest", "RequestId", r0, "ApprovalId", approval, "Approver", new EntityReference("systemuser", diego) { Name = "Diego" });
            var caller = New("Services.UserOption", "Id", tony, "Name", "Tony", "Email", "tony@x");
            var env = New("Services.EnvironmentInfo", "EnvironmentId", "env-1");
            foreach (var call in new[] { "Respond", "Reassign" })
            {
                var svc = Make("Services.ApprovalActionService", stale);
                var a = call == "Respond" ? new[] { request, "Approve", caller, "", env, "TOK" } : new[] { request, New("Services.UserOption", "Id", megan, "Name", "Megan", "Email", "m@x"), caller, "", env, "TOK" };
                Exception error = null; try { T("Services.ApprovalActionService").GetMethod(call).Invoke(svc, a); } catch (TargetInvocationException ex) { error = ex.InnerException; }
                Check(error != null && error.Message.Contains("another step") && stale.Log.Count == 0, call + ": a request whose step has passed is refused before anything changes (" + error?.Message + ")");
            }
        }

        // ---- chart data
        object flow;
        {
            var db = Sequential(1);
            flow = T("Services.ApprovalFlowService").GetMethod("Load").Invoke(Make("Services.ApprovalFlowService", db), new object[] { approval });
            var steps = ((System.Collections.IEnumerable)P(flow, "Steps")).Cast<object>().ToList();
            Func<int, List<object>> people = i => ((System.Collections.IEnumerable)P(steps[i], "People")).Cast<object>().ToList();
            Func<object, string> show = p => P(p, "Name") + "/" + P(p, "JobTitle") + "/" + P(p, "State");
            var requester = P(flow, "Requester");
            Check((string)P(requester, "Name") == "Tony Dai" && P(requester, "State").ToString() == "Requester", "requester is the approval's owner");
            Check(steps.Count == 3 && people(0).Count == 1 && people(1).Count == 1 && people(2).Count == 1, "three steps, one person each (Megan's reassigned request left out): " + string.Join(" | ", steps.Select((s, i) => string.Join(",", people(i).Select(show)))));
            Check(show(people(0)[0]) == "Diego Siciliani/HR Manager/Approved", "step 1: Diego approved (answered on his behalf, so owned by Tony): " + show(people(0)[0]));
            Check(show(people(1)[0]) == "Alex Wilber/Marketing Assistant/Waiting", "step 2: Alex is who it is waiting on: " + show(people(1)[0]));
            Check(show(people(2)[0]) == "Grady Archie/Designer/NotStarted", "step 3: Grady has not been reached: " + show(people(2)[0]));

            var done = Sequential(1); done.Rows.Single(r => r.Id == approval)["statecode"] = new OptionSetValue(1);
            var closed = T("Services.ApprovalFlowService").GetMethod("Load").Invoke(Make("Services.ApprovalFlowService", done), new object[] { approval });
            var closedSteps = ((System.Collections.IEnumerable)P(closed, "Steps")).Cast<object>().ToList();
            Check(P(closed, "CurrentStep") == null && ((System.Collections.IEnumerable)P(closedSteps[1], "People")).Cast<object>().All(p => P(p, "State").ToString() == "NoResponse"), "finished approval: nobody is 'currently with', unanswered people show no response");
        }

        // ---- HTML
        var html = T("Controls.ApprovalFlowHtml");
        Func<object, string> render = f => (string)html.GetMethod("Render").Invoke(null, new object[] { f, (Func<object, byte[]>)null });
        var renderMethod = html.GetMethod("Render");
        var personType = T("Services.FlowPerson");
        var photoFunc = typeof(Func<,>).MakeGenericType(personType, typeof(byte[]));
        byte[] alexPhoto;
        using (var bmp = new Bitmap(96, 96)) using (var g = Graphics.FromImage(bmp)) using (var ms = new System.IO.MemoryStream())
        {
            g.Clear(Color.SteelBlue); g.FillEllipse(Brushes.Bisque, 28, 14, 40, 44); g.FillEllipse(Brushes.Bisque, 14, 60, 68, 60); bmp.Save(ms, ImageFormat.Jpeg); alexPhoto = ms.ToArray();
        }
        var lambdaParam = System.Linq.Expressions.Expression.Parameter(personType, "p");
        var nameOf = System.Linq.Expressions.Expression.Property(lambdaParam, "Name");
        var body = System.Linq.Expressions.Expression.Condition(System.Linq.Expressions.Expression.Equal(nameOf, System.Linq.Expressions.Expression.Constant("Alex Wilber")), System.Linq.Expressions.Expression.Constant(alexPhoto), System.Linq.Expressions.Expression.Constant(null, typeof(byte[])));
        var photos = System.Linq.Expressions.Expression.Lambda(photoFunc, body, lambdaParam).Compile();
        var chart = (string)renderMethod.Invoke(null, new object[] { flow, photos });
        Check(chart.Split(new[] { "Currently with" }, StringSplitOptions.None).Length == 2, "one 'Currently with' arrow, above the person it waits on");
        var order = new[] { "Requested by", "Tony Dai", "Step 1", "Diego Siciliani", "HR Manager", "Approved", "Step 2", "Currently with", "Alex Wilber", "Marketing Assistant", "Step 3", "Grady Archie", "Designer", "Not started" }.Select(s => chart.IndexOf(s)).ToList();
        Check(order.All(i => i >= 0) && order.SequenceEqual(order.OrderBy(i => i)), "left to right: requester, step 1, 2, 3; name then title under each circle");
        Check(chart.Contains("data:image/jpeg;base64,") && chart.Contains(">TD<") && chart.Contains(">GA<"), "photo where there is one, initials otherwise");
        var initials = html.GetMethod("Initials");
        Check((string)initials.Invoke(null, new object[] { "diego de la Vega" }) == "DV" && (string)initials.Invoke(null, new object[] { "Cher" }) == "C" && (string)initials.Invoke(null, new object[] { "" }) == "?", "initials: first and last word");
        var evil = New("Services.FlowPerson", "Name", "<script>x</script>", "State", Enum.Parse(T("Services.FlowState"), "Waiting"));
        var evilFlow = Activator.CreateInstance(T("Services.ApprovalFlow")); evilFlow.GetType().GetProperty("Requester").SetValue(evilFlow, evil);
        Check(!((string)renderMethod.Invoke(null, new object[] { evilFlow, Delegate.CreateDelegate(photoFunc, typeof(FlowTest).GetMethod("NoPhoto", F).MakeGenericMethod(personType)) })).Contains("<script>"), "names are HTML-encoded");

        // ---- in the preview pane (off screen)
        var pane = (Control)Activator.CreateInstance(T("Controls.WebContentControl"));
        var host = new Quiet { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000), ClientSize = new Size(560, 520), ShowInTaskbar = false };
        pane.Dock = DockStyle.Fill; host.Controls.Add(pane); host.Show();
        pane.GetType().GetMethod("ShowApproval").Invoke(pane, new object[] { "Seq approval example 1", "Requested by Tony Dai  ·  Pending  ·  With Alex Wilber", "Please approve the **budget**." });
        pane.GetType().GetMethod("ShowFlow", F).Invoke(pane, new object[] { chart }); // before the page has loaded: waits for it
        var until = DateTime.Now.AddSeconds(10); while (DateTime.Now < until && pane.GetType().GetProperty("FlowHtml", F).GetValue(pane) == null) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
        for (var i = 0; i < 30; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
        var shown = (string)pane.GetType().GetProperty("FlowHtml", F).GetValue(pane);
        Check(shown != null && shown.Contains("Currently with"), "chart is put in the pane once the page loads");
        var browser = pane.Controls.OfType<WebBrowser>().Single();
        var widths = browser.Document.GetElementsByTagName("div").Cast<HtmlElement>().Where(e => e.GetAttribute("className") == "circle").Select(e => { var r = e.OffsetRectangle; for (var p = e.OffsetParent; p != null; p = p.OffsetParent) r.Offset(p.OffsetRectangle.Location); return r; }).ToList();
        Check(widths.Count == 4 && widths.All(r => r.Width == 56 && r.Height == 56) && widths.Select(r => r.Top).Distinct().Count() == 1 && widths.Zip(widths.Skip(1), (a, b) => b.Left > a.Left).All(x => x), "four 56px circles on one row, left to right: " + string.Join(" ", widths));
        using (var shot = new Bitmap(host.ClientSize.Width, host.ClientSize.Height)) { browser.DrawToBitmap(shot, new Rectangle(Point.Empty, host.ClientSize)); shot.Save(System.IO.Path.Combine(args[1], "flow.png")); }
        pane.GetType().GetMethod("ShowMessage").Invoke(pane, new object[] { "Select an approval" });
        pane.GetType().GetMethod("ShowFlow", F).Invoke(pane, new object[] { chart });
        until = DateTime.Now.AddSeconds(3); while (DateTime.Now < until) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
        Check(!browser.DocumentText.Contains("Currently with"), "a chart arriving after the pane switched to a message is ignored");
        host.Dispose();

        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }

    static byte[] NoPhoto<TPerson>(TPerson p) => null;
}

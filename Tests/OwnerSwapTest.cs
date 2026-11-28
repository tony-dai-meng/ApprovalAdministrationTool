using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

class StubHandler : HttpMessageHandler
{
    public HttpStatusCode Status = HttpStatusCode.OK; public List<string> Log; public string LastBody, LastAuth;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
    {
        LastBody = await r.Content.ReadAsStringAsync(); LastAuth = r.Headers.Authorization?.ToString();
        Log.Add("POST " + r.RequestUri.AbsolutePath);
        return new HttpResponseMessage(Status) { Content = new StringContent(Status == HttpStatusCode.OK ? "{}" : "{\"innerMessage\":\"ApprovalRequestReassignmentAccessDenied\"}") };
    }
}

class FakeOrg : IOrganizationService
{
    public List<string> Log; public bool AllowReassign; public bool FailOwnerRestore; public Guid OriginalOwner;
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) { var e = new Entity(entityName, id); e["msdyn_flow_approval_allowreassign"] = AllowReassign; if (entityName == "msdyn_flow_approvalrequest") { e["statecode"] = new OptionSetValue(0); e["ownerid"] = new EntityReference("systemuser", OriginalOwner); } return e; }
    public void Update(Entity entity)
    {
        foreach (var a in entity.Attributes)
        {
            var v = a.Value is EntityReference er ? (er.Id == OriginalOwner ? "original" : "app") : a.Value.ToString();
            if (FailOwnerRestore && a.Key == "ownerid" && v == "original" && Log.Contains("POST /api/reassignApproval")) throw new InvalidOperationException("record is inactive");
            Log.Add("Update " + entity.LogicalName + "." + a.Key + "=" + v);
        }
    }
    public Guid Create(Entity entity) => throw new NotImplementedException();
    public void Delete(string entityName, Guid id) => throw new NotImplementedException();
    public OrganizationResponse Execute(OrganizationRequest request) => throw new NotImplementedException();
    public void Associate(string a, Guid b, Relationship c, EntityReferenceCollection d) => throw new NotImplementedException();
    public void Disassociate(string a, Guid b, Relationship c, EntityReferenceCollection d) => throw new NotImplementedException();
    public EntityCollection RetrieveMultiple(QueryBase query) => new EntityCollection(); // the new approver is in none of the steps
}

static class OwnerSwapTest
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

    static int Main(string[] args)
    {
        var plugin = Assembly.LoadFrom(args[0]);
        var svcType = plugin.GetType("ApprovalAdministrationTool.Services.ApprovalActionService", true);
        var reqType = plugin.GetType("ApprovalAdministrationTool.Services.PendingRequest", true);
        var userType = plugin.GetType("ApprovalAdministrationTool.Services.UserOption", true);
        var envType = plugin.GetType("ApprovalAdministrationTool.Services.EnvironmentInfo", true);
        var stub = new StubHandler();
        svcType.GetField("Http", F).SetValue(null, new HttpClient(stub) { BaseAddress = new Uri("https://approvals.teams.microsoft.com") });

        var originalOwner = Guid.NewGuid(); var appUserId = Guid.NewGuid();
        Func<Guid, object> request = owner => { var r = Activator.CreateInstance(reqType); reqType.GetProperty("RequestId").SetValue(r, Guid.NewGuid()); reqType.GetProperty("ApprovalId").SetValue(r, Guid.NewGuid()); reqType.GetProperty("ApprovalTitle").SetValue(r, "Budget"); reqType.GetProperty("Approver").SetValue(r, new EntityReference("systemuser", owner) { Name = "Alex" }); return r; };
        Func<Guid, string, string, object> user = (id, name, email) => { var u = Activator.CreateInstance(userType); userType.GetProperty("Id").SetValue(u, id); userType.GetProperty("Name").SetValue(u, name); userType.GetProperty("Email").SetValue(u, email); return u; };
        var env = Activator.CreateInstance(envType); envType.GetProperty("EnvironmentId").SetValue(env, "env-123");
        var newApprover = user(Guid.NewGuid(), "Sam", "sam@contoso.com"); var app = user(appUserId, "Tool app", null);

        Func<FakeOrg, object, Tuple<string, Exception>> run = (org, req) =>
        {
            stub.Log = org.Log;
            var svc = Activator.CreateInstance(svcType, F, null, new object[] { org }, null);
            svcType.GetField("requestAllowReassignLooked", F).SetValue(svc, true); // no Allow Reassign column on the request table
            try { return Tuple.Create((string)svcType.GetMethod("Reassign").Invoke(svc, new[] { req, newApprover, app, "please cover", env, "TOKEN" }), (Exception)null); }
            catch (TargetInvocationException ex) { return Tuple.Create((string)null, ex.InnerException); }
        };

        // 1. success
        var org1 = new FakeOrg { Log = new List<string>(), AllowReassign = false, OriginalOwner = originalOwner };
        stub.Status = HttpStatusCode.OK;
        var r1 = run(org1, request(originalOwner));
        Console.WriteLine("  steps: " + string.Join(" -> ", org1.Log));
        Check(r1.Item2 == null && r1.Item1 == null, "success: no error, no warning");
        Check(org1.Log.SequenceEqual(new[] { "Update msdyn_flow_approval.msdyn_flow_approval_allowreassign=True", "Update msdyn_flow_approvalrequest.ownerid=app", "POST /api/reassignApproval", "Update msdyn_flow_approvalrequest.ownerid=original" }),
            "success: allow reassign on, owner -> app user, reassign API, owner -> back to the original approver");
        Check(stub.LastAuth == "Bearer TOKEN" && stub.LastBody.Contains("\"AssignedTo\":\"sam@contoso.com\"") && stub.LastBody.Contains("\"Comments\":\"please cover\"") && stub.LastBody.Contains("\"FlowEnvironment\":\"env-123\""),
            "API call carries the app token, new approver's email, comments and environment");

        // 2. API refuses
        var org2 = new FakeOrg { Log = new List<string>(), AllowReassign = false, OriginalOwner = originalOwner };
        stub.Status = HttpStatusCode.Forbidden;
        var r2 = run(org2, request(originalOwner));
        Console.WriteLine("  steps: " + string.Join(" -> ", org2.Log));
        Check(r2.Item2 != null && r2.Item2.Message.Contains("403") && r2.Item2.Message.Contains("AccessDenied"), "API refusal is reported with its status and message");
        Check(org2.Log.Skip(3).SequenceEqual(new[] { "Update msdyn_flow_approvalrequest.ownerid=original", "Update msdyn_flow_approval.msdyn_flow_approval_allowreassign=False" }),
            "API refusal: owner and Allow Reassign are both put back");

        // 3. reassigned but giving it back fails
        var org3 = new FakeOrg { Log = new List<string>(), AllowReassign = true, FailOwnerRestore = true, OriginalOwner = originalOwner };
        stub.Status = HttpStatusCode.OK;
        var r3 = run(org3, request(originalOwner));
        Check(r3.Item2 == null && r3.Item1 != null && r3.Item1.Contains("could not be given back to Alex") && r3.Item1.Contains("record is inactive"),
            "if giving it back fails after a successful reassign, that's a warning, not a failure: " + r3.Item1);

        // 4. request already owned by the app user: no owner changes
        var org4 = new FakeOrg { Log = new List<string>(), AllowReassign = true, OriginalOwner = appUserId };
        var r4 = run(org4, request(appUserId));
        Check(r4.Item2 == null && org4.Log.SequenceEqual(new[] { "POST /api/reassignApproval" }), "already owned by the app user: just the API call");

        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }
}

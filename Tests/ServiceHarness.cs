using System;
using System.Collections;
using System.Data;
using System.Linq;
using System.Reflection;
using Microsoft.Xrm.Sdk.Query;

// Exercises the internal ApprovalService.GetApprovals against FakeService (FakeApprovals.cs).
static class ServiceHarness
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }

    static int Main(string[] args)
    {
        var plugin = Assembly.LoadFrom(args[0]);
        var all = ((IEnumerable)plugin.GetType("ApprovalAdministrationTool.Models.ApprovalColumns", true).GetField("All").GetValue(null)).Cast<string>().ToArray();
        var serviceType = plugin.GetType("ApprovalAdministrationTool.Services.ApprovalService", true);
        var fake = new FakeService { ToolColumns = all };
        var service = Activator.CreateInstance(serviceType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { fake }, null);
        var result = serviceType.GetMethod("GetApprovals").Invoke(service, new object[] { null });
        var table = FakeService.Approvals(result);
        Func<int, string, object> cell = (row, column) => table.Rows[row][column];

        Check(fake.MetadataRequests == 1, "reads the table metadata once");
        Check(fake.Fetches.Count == 2 && (string)fake.Fetches[0].Root.Attribute("page") == "1" && (string)fake.Fetches[1].Root.Attribute("page") == "2",
            "retrieves both pages with FetchXML (page 1, page 2)");
        Check(fake.Fetches[0].Root.Attribute("paging-cookie") == null && (string)fake.Fetches[1].Root.Attribute("paging-cookie") == "<cookie page=\"1\" />",
            "page 2 passes page 1's paging cookie");
        var entity = fake.Fetches[0].Root.Element("entity");
        var order = entity.Element("order");
        Check((string)entity.Attribute("name") == "msdyn_flow_approval" && (string)order.Attribute("attribute") == "createdon" && (string)order.Attribute("descending") == "true",
            "queries approvals newest first");
        var attributes = entity.Elements("attribute").Select(a => (string)a.Attribute("name")).ToList();
        Check(attributes.Count == all.Length - 2 && !attributes.Contains("currentstage") && !attributes.Contains("currentlywith"),
            "skips the column missing from this environment and the calculated one (" + attributes.Count + " of " + all.Length + " requested)");
        Check(entity.Element("filter") == null || !entity.Element("filter").HasElements, "no filter: no conditions sent");
        Check((string)result.GetType().GetProperty("FetchXmlPrefix").GetValue(result) != null
            && ((System.Collections.Generic.IReadOnlyCollection<string>)result.GetType().GetProperty("TableColumns").GetValue(result)).Contains("createdon"),
            "result carries the query's fixed part and every table column (for the editor's checks)");

        var filtered = new FakeService { ToolColumns = all };
        var filteredService = Activator.CreateInstance(serviceType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { filtered }, null);
        serviceType.GetMethod("GetApprovals").Invoke(filteredService, new object[] {
            "<filter type=\"or\">\n  <!-- a comment -->\n  <condition attribute=\"statuscode\" operator=\"eq\" value=\"192350001\" />\n</filter>" });
        var sentFilter = filtered.Fetches[0].Root.Element("entity").Element("filter");
        Check(sentFilter != null && (string)sentFilter.Attribute("type") == "or" && (string)sentFilter.Element("condition").Attribute("value") == "192350001",
            "a custom filter is sent inside the entity");
        Check(!filtered.Fetches[0].DescendantNodes().OfType<System.Xml.Linq.XComment>().Any(), "XML comments are removed before sending");
        var rq = fake.RequestQueries.Single();
        Check(rq.ColumnSet.Columns.Contains("msdyn_flow_approvalrequest_approval") && rq.ColumnSet.Columns.Contains("ownerid")
            && rq.Criteria.Conditions.Any(c => c.AttributeName == "statecode" && (int)c.Values[0] == 0), "reads active approval requests (approval + owner, statecode = 0) once");
        Check(table.Columns["currentlywith"].Caption == "Currently With" && table.Columns["ownerid"].Caption == "Owner", "Currently With and Owner columns are captioned");
        Check((string)cell(0, "currentlywith__friendly") == "Alex Doe; Sam Lee", "Currently With lists each active approver once, sorted (" + cell(0, "currentlywith__friendly") + ")");
        Check((string)cell(0, "currentlywith") == FakeService.Alex + "; " + FakeService.Sam, "Currently With stored value lists their user IDs");
        Check(cell(1, "currentlywith") == DBNull.Value && cell(1, "currentlywith__friendly") == DBNull.Value, "Currently With is empty for an approval with no active requests");
        Check((string)cell(0, "ownerid__friendly") == "Owner of Expense claim" && Guid.TryParse((string)cell(0, "ownerid"), out _), "Owner: friendly name, stored user ID");
        Check(!table.Columns.Contains("currentstage") && !table.Columns.Contains("currentstage__friendly"), "missing column is not in the table");
        Check(table.Columns.Contains("statuscode") && table.Columns.Contains("msdyn_flow_approval_result"), "new Status Reason and Result columns are in the table");
        Check(table.Rows.Count == 3, "one row per approval across pages");

        Check(table.Columns["statuscode"].Caption == "Status Reason" && table.Columns["msdyn_flow_approval_result"].Caption == "Result", "captions are the Dataverse display names");
        Check(table.Columns["msdyn_flow_approval_priority"].Caption == "msdyn_flow_approval_priority", "column without a display name is captioned with its logical name");

        var switchable = table.Columns.Cast<DataColumn>().Where(c => c.ColumnName.EndsWith("__friendly")).Select(c => c.ColumnName.Replace("__friendly", "")).OrderBy(n => n).ToArray();
        Check(string.Join(",", switchable) == "currentlywith,msdyn_flow_approval_modeltype,msdyn_flow_approval_priority,msdyn_flow_approval_requesttype,ownerid,statuscode",
            "friendly-values columns exist for currently with, model type, priority, request type, owner, status reason (" + string.Join(", ", switchable) + ")");
        Check(!table.Columns.Contains("msdyn_flow_approval_result__friendly") && !table.Columns.Contains("msdyn_flow_approval_title__friendly"), "no friendly values for plain text columns");

        Check((string)cell(0, "statuscode") == "192350001" && (string)cell(0, "statuscode__friendly") == "Pending", "Status Reason: stored 192350001, friendly Pending");
        Check((string)cell(1, "statuscode") == "192350004" && (string)cell(1, "statuscode__friendly") == "Completed", "Status Reason: stored 192350004, friendly Completed");
        Check((string)cell(1, "msdyn_flow_approval_requesttype") == "192350001" && (string)cell(1, "msdyn_flow_approval_requesttype__friendly") == "Basic", "Request Type: stored number, friendly label");
        Check(!table.Columns.Contains("msdyn_flow_approval_stage"), "Stage is no longer read or shown");
        Check((string)cell(0, "msdyn_flow_approval_modeltype") == "msdyn_flow_basicapprovalmodel" && (string)cell(0, "msdyn_flow_approval_modeltype__friendly") == "Approve/Reject – First to respond", "Model type: stored table name, friendly approval type");
        Check((string)cell(2, "msdyn_flow_approval_modeltype__friendly") == "msdyn_flow_somefuturemodel", "unknown model type: friendly falls back to the stored value");
        Check((string)cell(2, "msdyn_flow_approval_priority") == "192350001" && (string)cell(2, "msdyn_flow_approval_priority__friendly") == "Important",
            "Priority sent without a label: friendly value is the choice's label from metadata (" + cell(2, "msdyn_flow_approval_priority__friendly") + ")");
        Check((string)cell(1, "msdyn_flow_approval_priority__friendly") == "192359999", "a number that is not one of the choice's options falls back to the number");
        Check((string)cell(2, "msdyn_flow_approval_allowreassign") == "No", "Yes/No sent without a label shows the metadata label (" + cell(2, "msdyn_flow_approval_allowreassign") + ")");
        Check(cell(0, "msdyn_flow_approval_priority") == DBNull.Value && cell(0, "msdyn_flow_approval_priority__friendly") == DBNull.Value, "missing choice value is empty in both");

        Check((Guid)cell(0, "msdyn_flow_approvalid") == new Guid("11111111-1111-1111-1111-111111111111"), "ID column holds the record Guid");
        Check((string)cell(0, "msdyn_flow_approval_allowreassign") == "Yes", "Yes/No column shows its label");
        Check((string)cell(2, "msdyn_flow_approval_source") == "Alex Doe", "lookup without formatted value shows its name");
        Check((string)cell(2, "msdyn_flow_approval_result") == "Reject" && cell(0, "msdyn_flow_approval_result") == DBNull.Value, "Result shows the outcome, empty while there is none");

        bool threw = false;
        try { Activator.CreateInstance(serviceType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { null }, null); }
        catch (TargetInvocationException e) { threw = e.InnerException is ArgumentNullException; }
        Check(threw, "rejects a null connection");

        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }
}

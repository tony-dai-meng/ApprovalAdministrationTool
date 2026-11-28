using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

// A fake Dataverse connection for the approval table, shared by ServiceHarness and FriendlyTest.
// Metadata: every tool column except "currentstage" (to check missing columns are skipped); Priority has no display name.
// Data: two pages of approvals with choices (number + label), model types, a lookup, Yes/No and text.
class FakeService : IOrganizationService
{
    public readonly List<QueryExpression> Queries = new List<QueryExpression>();
    public readonly List<string> CookiesSeen = new List<string>();
    public int MetadataRequests;
    public string[] ToolColumns;   // ApprovalColumns.All, set by the caller

    static Entity Approval(string id, string title, string model, int status, string statusLabel)
    {
        var a = new Entity("msdyn_flow_approval", new Guid(id));
        a["msdyn_flow_approval_title"] = title;
        a["msdyn_flow_approval_modeltype"] = model;
        a["statuscode"] = new OptionSetValue(status); a.FormattedValues["statuscode"] = statusLabel;
        a["msdyn_flow_approval_requesttype"] = new OptionSetValue(192350001); a.FormattedValues["msdyn_flow_approval_requesttype"] = "Basic";
        a["ownerid"] = new EntityReference("systemuser", new Guid("bbbbbbbb-0000-0000-0000-00000000000" + title.Length % 10)) { Name = "Owner of " + title };
        return a;
    }

    public readonly List<QueryExpression> RequestQueries = new List<QueryExpression>();
    public static readonly Guid Alex = new Guid("aaaaaaaa-0000-0000-0000-000000000001"), Sam = new Guid("aaaaaaaa-0000-0000-0000-000000000002");

    /// <summary>The DataTable inside an ApprovalLoadResult (returned by ApprovalService.GetApprovals).</summary>
    public static System.Data.DataTable Approvals(object loadResult)
    {
        return (System.Data.DataTable)loadResult.GetType().GetProperty("Approvals").GetValue(loadResult);
    }

    /// <summary>Approval FetchXML queries received, in order (one per page).</summary>
    public readonly List<System.Xml.Linq.XDocument> Fetches = new List<System.Xml.Linq.XDocument>();

    public EntityCollection RetrieveMultiple(QueryBase query)
    {
        if (query is FetchExpression fetch)
        {
            var document = System.Xml.Linq.XDocument.Parse(fetch.Query);
            if (document.Root.Element("entity").Attribute("name").Value != "msdyn_flow_approval") throw new Exception("unexpected fetch entity");
            Fetches.Add(document);
            var pageAttribute = document.Root.Attribute("page");
            return ApprovalsPage(pageAttribute == null ? 1 : int.Parse(pageAttribute.Value));
        }

        var q = (QueryExpression)query;
        if (q.EntityName == "msdyn_flow_approvalrequest")
        {
            // Active approval requests: Expense claim waits on Sam and Alex (Sam listed twice); the others wait on nobody.
            RequestQueries.Add(q);
            var requests = new EntityCollection { EntityName = "msdyn_flow_approvalrequest", MoreRecords = false };
            Func<Guid, string, Entity> request = (owner, name) =>
            {
                var r = new Entity("msdyn_flow_approvalrequest", Guid.NewGuid());
                r["msdyn_flow_approvalrequest_approval"] = new EntityReference("msdyn_flow_approval", new Guid("11111111-1111-1111-1111-111111111111"));
                r["ownerid"] = new EntityReference("systemuser", owner) { Name = name };
                return r;
            };
            requests.Entities.Add(request(Sam, "Sam Lee"));
            requests.Entities.Add(request(Alex, "Alex Doe"));
            requests.Entities.Add(request(Sam, "Sam Lee"));
            return requests;
        }

        Queries.Add(q);
        CookiesSeen.Add(q.PageInfo.PagingCookie ?? "(none)");
        return ApprovalsPage(q.PageInfo.PageNumber);
    }

    EntityCollection ApprovalsPage(int pageNumber)
    {
        var page = new EntityCollection { EntityName = "msdyn_flow_approval" };
        if (pageNumber == 1)
        {
            var a = Approval("11111111-1111-1111-1111-111111111111", "Expense claim", "msdyn_flow_basicapprovalmodel", 192350001, "Pending");
            a["msdyn_flow_approval_allowreassign"] = true; a.FormattedValues["msdyn_flow_approval_allowreassign"] = "Yes";
            page.Entities.Add(a);
            var budget = Approval("33333333-3333-3333-3333-333333333333", "Budget", "msdyn_flow_awaitallapprovalmodel", 192350004, "Completed");
            budget["msdyn_flow_approval_priority"] = new OptionSetValue(192359999);   // not one of the choice's options, no label sent
            page.Entities.Add(budget);
            page.MoreRecords = true;
            page.PagingCookie = "<cookie page=\"1\" />";
        }
        else
        {
            var b = Approval("22222222-2222-2222-2222-222222222222", "Leave request", "msdyn_flow_somefuturemodel", 192350006, "Canceled");
            b["msdyn_flow_approval_priority"] = new OptionSetValue(192350001);        // no label sent: the label must come from the choice's options
            b["msdyn_flow_approval_allowreassign"] = false;                           // no label sent: Yes/No label must come from metadata
            b["msdyn_flow_approval_source"] = new EntityReference("systemuser", Guid.Empty) { Name = "Alex Doe" };
            b["msdyn_flow_approval_result"] = "Reject";
            page.Entities.Add(b);
            page.MoreRecords = false;
        }
        return page;
    }

    public OrganizationResponse Execute(OrganizationRequest request)
    {
        var r = (Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest)request;
        MetadataRequests++;
        if (r.LogicalName != "msdyn_flow_approval" || r.EntityFilters != EntityFilters.Attributes) throw new Exception("unexpected metadata request");
        var labels = new Dictionary<string, string>
        {
            ["msdyn_flow_approvalid"] = "Approval", ["msdyn_flow_approval_title"] = "Title", ["msdyn_flow_approval_modeltype"] = "Approval Model Type",
            ["statuscode"] = "Status Reason", ["msdyn_flow_approval_result"] = "Result",
            ["msdyn_flow_approval_allowreassign"] = "Allow Reassign", ["msdyn_flow_approval_requesttype"] = "Request Type",
        };
        Func<string, Label> L = text => new Label(new LocalizedLabel(text, 1033), new[] { new LocalizedLabel(text, 1033) });
        Func<string, (string, int)[], AttributeMetadata> choice = (name, options) => new PicklistAttributeMetadata
        {
            LogicalName = name,
            OptionSet = new OptionSetMetadata(new OptionMetadataCollection(options.Select(o => new OptionMetadata(L(o.Item1), o.Item2)).ToList())),
        };
        labels["ownerid"] = "Owner";
        var attributes = ToolColumns.Where(c => c != "currentstage" && c != "currentlywith").Concat(new[] { "createdon" }).Select(name =>
        {
            AttributeMetadata a;
            if (name == "msdyn_flow_approval_priority")
                a = choice(name, new[] { ("Urgent", 192350000), ("Important", 192350001), ("Medium", 192350002), ("Low", 192350003) });
            else if (name == "statuscode")
                a = choice(name, new[] { ("Created", 192350000), ("Pending", 192350001), ("Suspended", 192350002), ("Completed", 192350004), ("Expired", 192350005), ("Canceled", 192350006), ("Abandoned", 192350007) });
            else if (name == "msdyn_flow_approval_allowreassign")
                a = new BooleanAttributeMetadata { LogicalName = name, OptionSet = new BooleanOptionSetMetadata(new OptionMetadata(L("Yes"), 1), new OptionMetadata(L("No"), 0)) };
            else
                a = new StringAttributeMetadata { LogicalName = name };
            if (labels.TryGetValue(name, out var label)) a.DisplayName = L(label);
            return a;
        }).ToArray();
        var metadata = new EntityMetadata { LogicalName = "msdyn_flow_approval" };
        typeof(EntityMetadata).GetProperty("Attributes").GetSetMethod(true).Invoke(metadata, new object[] { attributes });
        var response = new Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse();
        response.Results["EntityMetadata"] = metadata;
        return response;
    }

    public Guid Create(Entity entity) { throw new NotImplementedException(); }
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) { throw new NotImplementedException(); }
    public void Update(Entity entity) { throw new NotImplementedException(); }
    public void Delete(string entityName, Guid id) { throw new NotImplementedException(); }
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { throw new NotImplementedException(); }
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { throw new NotImplementedException(); }
}

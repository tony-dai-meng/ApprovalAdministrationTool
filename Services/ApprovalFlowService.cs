using System;
using System.Collections.Generic;
using System.Linq;
using ApprovalAdministrationTool.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ApprovalAdministrationTool.Services
{
    /// <summary>Where a person stands in an approval.</summary>
    internal enum FlowState
    {
        Requester,
        Waiting,
        Approved,
        Rejected,
        Responded,   // a custom response
        NotStarted,  // a later step of an approval that is still open
        NoResponse,  // the approval finished without this person answering
    }

    /// <summary>A person in the approval flow chart.</summary>
    internal sealed class FlowPerson
    {
        public EntityReference User { get; set; }
        public string Name { get; set; }
        public string JobTitle { get; set; }
        public Guid? EntraObjectId { get; set; }
        public FlowState State { get; set; }

        /// <summary>The response as given, for custom responses (e.g. "Hello").</summary>
        public string Response { get; set; }

        /// <summary>The Dataverse user image, if the user has one (used when no Microsoft 365 photo can be read).</summary>
        public byte[] DataverseImage { get; set; }
    }

    /// <summary>One step of an approval: everyone it was assigned to (all at once).</summary>
    internal sealed class FlowStep
    {
        public int Number { get; set; }
        public List<FlowPerson> People { get; } = new List<FlowPerson>();
    }

    /// <summary>Who requested an approval and who it goes to, step by step.</summary>
    internal sealed class ApprovalFlow
    {
        public Guid ApprovalId { get; set; }
        public FlowPerson Requester { get; set; }
        public List<FlowStep> Steps { get; } = new List<FlowStep>();

        /// <summary>The step the approval is on, or null when it is finished.</summary>
        public int? CurrentStep { get; set; }

        public IEnumerable<FlowPerson> Everyone => new[] { Requester }.Where(p => p != null).Concat(Steps.SelectMany(s => s.People));
    }

    /// <summary>
    /// Reads an approval's flow for the chart: the requester (the approval's owner), and for each step the people its
    /// requests went to and whether they answered. Requests that were reassigned are left out (the request made for
    /// the new approver takes their place). Responses are matched to people by owner and step. No UI code.
    /// </summary>
    internal sealed class ApprovalFlowService
    {
        private const string RequestEntity = "msdyn_flow_approvalrequest";
        private const string RequestApprovalLookup = "msdyn_flow_approvalrequest_approval";
        private const string RequestReassignedFrom = "msdyn_flow_approvalrequest_reassignedfrom";
        private const string ResponseEntity = "msdyn_flow_approvalresponse";
        private const string ResponseApprovalLookup = "msdyn_flow_approvalresponse_approval";
        private const string ResponseStep = "msdyn_flow_approvalresponse_stepnumber";
        private const string ResponseValue = "msdyn_flow_approvalresponse_response";
        private const int ReassignedStatus = 192350000;

        private readonly IOrganizationService service;

        public ApprovalFlowService(IOrganizationService service)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public ApprovalFlow Load(Guid approvalId)
        {
            var approval = service.Retrieve(ApprovalColumns.EntityName, approvalId, new ColumnSet("ownerid", "statecode", ApprovalSteps.CurrentStep));
            var open = approval.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0;
            var flow = new ApprovalFlow
            {
                ApprovalId = approvalId,
                CurrentStep = open ? approval.GetAttributeValue<int?>(ApprovalSteps.CurrentStep) ?? 0 : (int?)null,
            };

            var requests = GetRequests(approvalId);
            var responses = GetResponses(approvalId);
            var users = GetUsers(requests.Select(r => r.GetAttributeValue<EntityReference>("ownerid"))
                .Concat(new[] { approval.GetAttributeValue<EntityReference>("ownerid") }));

            var requester = approval.GetAttributeValue<EntityReference>("ownerid");
            if (requester != null)
            {
                flow.Requester = Person(requester, approval, users);
                flow.Requester.State = FlowState.Requester;
            }

            // A request that was reassigned is replaced by the one made for the new approver.
            var replaced = new HashSet<Guid>(requests
                .Select(r => r.GetAttributeValue<EntityReference>(RequestReassignedFrom)?.Id)
                .Where(id => id.HasValue)
                .Select(id => id.Value));

            foreach (var group in requests
                .Where(r => !replaced.Contains(r.Id) && r.GetAttributeValue<OptionSetValue>("statuscode")?.Value != ReassignedStatus)
                .GroupBy(r => r.GetAttributeValue<int?>(ApprovalSteps.RequestStep) ?? 0)
                .OrderBy(g => g.Key))
            {
                var step = new FlowStep { Number = group.Key };
                foreach (var request in group.OrderBy(r => r.GetAttributeValue<DateTime?>("createdon")))
                {
                    var owner = request.GetAttributeValue<EntityReference>("ownerid");
                    if (owner == null || step.People.Any(p => p.User.Id == owner.Id))
                    {
                        continue;
                    }

                    step.People.Add(Person(owner, request, users));
                }

                flow.Steps.Add(step);
            }

            SetStates(flow, responses);
            return flow;
        }

        /// <summary>Marks who answered (and how), who the approval waits on, and who it has not reached yet.</summary>
        private static void SetStates(ApprovalFlow flow, List<Entity> responses)
        {
            foreach (var step in flow.Steps)
            {
                var stepResponses = responses
                    .Where(r => (r.GetAttributeValue<int?>(ResponseStep) ?? 0) == step.Number)
                    .ToList();
                foreach (var person in step.People)
                {
                    var response = stepResponses.FirstOrDefault(r => r.GetAttributeValue<EntityReference>("ownerid")?.Id == person.User.Id);
                    if (response != null)
                    {
                        stepResponses.Remove(response);
                    }

                    // A response given on someone's behalf is owned by whoever sent it: with one person left, it is theirs.
                    if (response == null && step.People.Count == 1 && stepResponses.Count == 1)
                    {
                        response = stepResponses[0];
                    }

                    if (response != null)
                    {
                        var value = response.GetAttributeValue<string>(ResponseValue);
                        person.Response = value;
                        person.State = string.Equals(value, "Approve", StringComparison.OrdinalIgnoreCase) ? FlowState.Approved
                            : string.Equals(value, "Reject", StringComparison.OrdinalIgnoreCase) ? FlowState.Rejected
                            : FlowState.Responded;
                    }
                    else if (flow.CurrentStep == null || step.Number < flow.CurrentStep)
                    {
                        person.State = FlowState.NoResponse;
                    }
                    else
                    {
                        person.State = step.Number == flow.CurrentStep ? FlowState.Waiting : FlowState.NotStarted;
                    }
                }
            }
        }

        private List<Entity> GetRequests(Guid approvalId)
        {
            var query = new QueryExpression(RequestEntity)
            {
                ColumnSet = new ColumnSet("ownerid", "statuscode", "createdon", ApprovalSteps.RequestStep, RequestReassignedFrom),
            };
            query.Criteria.AddCondition(RequestApprovalLookup, ConditionOperator.Equal, approvalId);
            return service.RetrieveMultiple(query).Entities.ToList();
        }

        private List<Entity> GetResponses(Guid approvalId)
        {
            var query = new QueryExpression(ResponseEntity) { ColumnSet = new ColumnSet("ownerid", ResponseStep, ResponseValue, "createdon") };
            query.Criteria.AddCondition(ResponseApprovalLookup, ConditionOperator.Equal, approvalId);
            query.AddOrder("createdon", OrderType.Ascending);
            return service.RetrieveMultiple(query).Entities.ToList();
        }

        /// <summary>Name, job title, Entra ID object ID and image of each user (teams are left out: they have no profile).</summary>
        private Dictionary<Guid, Entity> GetUsers(IEnumerable<EntityReference> owners)
        {
            var ids = owners
                .Where(o => o != null && (o.LogicalName == null || o.LogicalName == "systemuser"))
                .Select(o => o.Id)
                .Distinct()
                .Cast<object>()
                .ToArray();
            if (ids.Length == 0)
            {
                return new Dictionary<Guid, Entity>();
            }

            var query = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("fullname", "title", "jobtitle", "azureactivedirectoryobjectid", "entityimage") };
            query.Criteria.AddCondition("systemuserid", ConditionOperator.In, ids);
            return service.RetrieveMultiple(query).Entities.ToDictionary(u => u.Id);
        }

        private static FlowPerson Person(EntityReference owner, Entity row, Dictionary<Guid, Entity> users)
        {
            users.TryGetValue(owner.Id, out var user);
            var title = user?.GetAttributeValue<string>("title");
            return new FlowPerson
            {
                User = owner,
                Name = user?.GetAttributeValue<string>("fullname")
                    ?? owner.Name
                    ?? (row.FormattedValues.TryGetValue("ownerid", out var name) ? name : owner.Id.ToString()),
                JobTitle = string.IsNullOrWhiteSpace(title) ? user?.GetAttributeValue<string>("jobtitle") : title,
                EntraObjectId = user?.GetAttributeValue<Guid?>("azureactivedirectoryobjectid"),
                DataverseImage = user?.GetAttributeValue<byte[]>("entityimage"),
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using ApprovalAdministrationTool.Models;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace ApprovalAdministrationTool.Services
{
    /// <summary>An approver's active approval request: the one row Reassign hands to someone else.</summary>
    internal sealed class PendingRequest
    {
        public Guid RequestId { get; set; }
        public Guid ApprovalId { get; set; }
        public string ApprovalTitle { get; set; }
        public EntityReference Approver { get; set; }
    }

    /// <summary>A Dataverse user the approval can be reassigned to.</summary>
    internal sealed class UserOption
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }

        public override string ToString() => string.IsNullOrEmpty(Email) ? Name : Name + "  <" + Email + ">";
    }

    /// <summary>The Power Platform environment and tenant of the Dataverse connection.</summary>
    internal sealed class EnvironmentInfo
    {
        public string EnvironmentId { get; set; }
        public Guid TenantId { get; set; }
    }

    /// <summary>
    /// Reassigns approval requests the way Matthew Devaney's flow does
    /// (https://www.matthewdevaney.com/reassign-an-approval-in-power-automate/): a POST to the Teams Approvals API's
    /// /api/reassignApproval, which records the reassignment and notifies the new approver.
    /// The tool calls it as the person using the tool (a delegated token). That API only lets them reassign requests they
    /// own, so, as suggested in that post's comments, each request is first assigned to them in Dataverse (and given
    /// back afterwards), and Allow Reassign is switched on if it is off.
    /// If the API call fails, those Dataverse changes are undone. No UI code: call from inside WorkAsync.
    /// </summary>
    internal sealed class ReassignService
    {
        private const string RequestEntity = "msdyn_flow_approvalrequest";
        private const string RequestApprovalLookup = "msdyn_flow_approvalrequest_approval";
        private const string AllowReassign = "msdyn_flow_approval_allowreassign";

        private static readonly HttpClient Http = new HttpClient { BaseAddress = new Uri(TeamsApprovalsAuth.ApiBaseUrl) };

        private readonly IOrganizationService service;

        public ReassignService(IOrganizationService service)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>The environment ID (sent to the API as FlowEnvironment) and the tenant to sign in to.</summary>
        public EnvironmentInfo GetEnvironment()
        {
            var detail = ((RetrieveCurrentOrganizationResponse)service.Execute(new RetrieveCurrentOrganizationRequest())).Detail;
            return new EnvironmentInfo { EnvironmentId = detail.EnvironmentId, TenantId = Guid.Parse(detail.TenantId) };
        }

        /// <summary>The active approval requests (one per approver still to respond) of the given approvals.</summary>
        public List<PendingRequest> GetPendingRequests(IReadOnlyCollection<Guid> approvalIds)
        {
            var result = new List<PendingRequest>();
            if (approvalIds.Count == 0)
            {
                return result;
            }

            var query = new QueryExpression(RequestEntity) { ColumnSet = new ColumnSet(RequestApprovalLookup, "ownerid") };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); // Active
            query.Criteria.AddCondition(RequestApprovalLookup, ConditionOperator.In, approvalIds.Cast<object>().ToArray());

            foreach (var request in service.RetrieveMultiple(query).Entities)
            {
                var approval = request.GetAttributeValue<EntityReference>(RequestApprovalLookup);
                var owner = request.GetAttributeValue<EntityReference>("ownerid");
                if (approval == null || owner == null)
                {
                    continue;
                }

                if (owner.Name == null && request.FormattedValues.TryGetValue("ownerid", out var ownerName))
                {
                    owner.Name = ownerName;
                }

                result.Add(new PendingRequest
                {
                    RequestId = request.Id,
                    ApprovalId = approval.Id,
                    ApprovalTitle = approval.Name ?? (request.FormattedValues.TryGetValue(RequestApprovalLookup, out var title) ? title : approval.Id.ToString()),
                    Approver = owner,
                });
            }

            return result;
        }

        /// <summary>
        /// The people in this environment an approval can be reassigned to, sorted by name: enabled users with an Entra ID
        /// account and an email, and an interactive access mode (Read-Write, Administrative or Read). Application users
        /// (which have an application ID), the SYSTEM and INTEGRATION users (no Entra ID account) and non-interactive,
        /// support and delegated admin users are left out.
        /// </summary>
        public List<UserOption> GetPeople()
        {
            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("fullname", "internalemailaddress", "domainname"),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 },
            };
            query.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
            query.Criteria.AddCondition("applicationid", ConditionOperator.Null);
            query.Criteria.AddCondition("azureactivedirectoryobjectid", ConditionOperator.NotNull);
            query.Criteria.AddCondition("internalemailaddress", ConditionOperator.NotNull);
            query.Criteria.AddCondition("accessmode", ConditionOperator.In, 0, 1, 2); // Read-Write, Administrative, Read
            query.AddOrder("fullname", OrderType.Ascending);

            var people = new List<UserOption>();
            EntityCollection page;
            do
            {
                page = service.RetrieveMultiple(query);
                people.AddRange(page.Entities.Select(ToUserOption));
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
            while (page.MoreRecords);

            return people;
        }

        /// <summary>The Dataverse user signed in to Teams Approvals (matched by sign-in name or email), or null.</summary>
        public UserOption FindUser(string userPrincipalName)
        {
            var query = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("fullname", "internalemailaddress", "domainname"), TopCount = 1 };
            var match = query.Criteria.AddFilter(LogicalOperator.Or);
            match.AddCondition("domainname", ConditionOperator.Equal, userPrincipalName);
            match.AddCondition("internalemailaddress", ConditionOperator.Equal, userPrincipalName);
            var user = service.RetrieveMultiple(query).Entities.FirstOrDefault();
            return user == null ? null : ToUserOption(user);
        }

        private static UserOption ToUserOption(Entity user)
        {
            var email = user.GetAttributeValue<string>("internalemailaddress");
            return new UserOption
            {
                Id = user.Id,
                Name = user.GetAttributeValue<string>("fullname") ?? user.Id.ToString(),
                Email = string.IsNullOrEmpty(email) ? user.GetAttributeValue<string>("domainname") : email,
            };
        }

        /// <summary>
        /// Reassigns one approval request to <paramref name="newApprover"/>:
        /// 1. switches on Allow Reassign on the approval (and its request, if that table has the column) when off;
        /// 2. assigns the request to <paramref name="caller"/> (the person signed in to Teams Approvals) in Dataverse, unless it already owns it;
        /// 3. POSTs /api/reassignApproval with the Teams Approvals token;
        /// 4. gives the request back to its original owner (the approver it was reassigned from), so its history still
        ///    shows who it was with.
        /// If step 3 fails, steps 1 and 2 are undone and the API's error is thrown.
        /// </summary>
        /// <returns>
        /// Null, or a warning if the reassignment worked but step 4 failed (the old request is then left owned by
        /// <paramref name="caller"/>).
        /// </returns>
        public string Reassign(PendingRequest request, UserOption newApprover, UserOption caller, string comments, EnvironmentInfo environment, string accessToken)
        {
            var undo = new List<Action>();
            var ownerSwapped = false;
            try
            {
                var approval = service.Retrieve(ApprovalColumns.EntityName, request.ApprovalId, new ColumnSet(AllowReassign));
                if (approval.Contains(AllowReassign) && !approval.GetAttributeValue<bool>(AllowReassign))
                {
                    SetBoolean(ApprovalColumns.EntityName, request.ApprovalId, AllowReassign, true);
                    undo.Add(() => SetBoolean(ApprovalColumns.EntityName, request.ApprovalId, AllowReassign, false));
                }

                var requestAllowReassign = RequestAllowReassignColumn();
                if (requestAllowReassign != null)
                {
                    var current = service.Retrieve(RequestEntity, request.RequestId, new ColumnSet(requestAllowReassign));
                    if (current.Contains(requestAllowReassign) && !current.GetAttributeValue<bool>(requestAllowReassign))
                    {
                        SetBoolean(RequestEntity, request.RequestId, requestAllowReassign, true);
                        undo.Add(() => SetBoolean(RequestEntity, request.RequestId, requestAllowReassign, false));
                    }
                }

                if (request.Approver.Id != caller.Id)
                {
                    SetOwner(request.RequestId, new EntityReference("systemuser", caller.Id));
                    var originalOwner = request.Approver;
                    undo.Add(() => SetOwner(request.RequestId, originalOwner));
                    ownerSwapped = true;
                }

                CallReassignApi(request, newApprover, comments, environment, accessToken);
            }
            catch
            {
                undo.Reverse();
                foreach (var step in undo)
                {
                    try
                    {
                        step();
                    }
                    catch
                    {
                        // Best effort: the original error is what the user needs to see.
                    }
                }

                throw;
            }

            if (!ownerSwapped)
            {
                return null;
            }

            try
            {
                SetOwner(request.RequestId, request.Approver);
                return null;
            }
            catch (Exception ex)
            {
                return "Reassigned, but the old request could not be given back to " + (request.Approver.Name ?? request.Approver.Id.ToString())
                    + " (it is still owned by " + caller.Name + "): " + ex.Message;
            }
        }

        private static void CallReassignApi(PendingRequest request, UserOption newApprover, string comments, EnvironmentInfo environment, string accessToken)
        {
            var body = "{"
                + "\"AssignedTo\":" + Json(newApprover.Email) + ","
                + "\"ApprovalId\":" + Json(request.ApprovalId.ToString()) + ","
                + "\"ApprovalRequestId\":" + Json(request.RequestId.ToString()) + ","
                + "\"Properties\":{\"Response\":\"\",\"Comments\":" + Json(comments ?? string.Empty) + "},"
                + "\"FlowEnvironment\":" + Json(environment.EnvironmentId)
                + "}";

            using (var message = new HttpRequestMessage(HttpMethod.Post, "/api/reassignApproval"))
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                message.Headers.Add("x-ms-client-request-id", Guid.NewGuid().ToString()); // the API refuses calls without one
                message.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using (var response = Http.SendAsync(message).GetAwaiter().GetResult())
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var details = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        throw new InvalidOperationException(
                            "The Teams Approvals API refused the reassignment (" + (int)response.StatusCode + " " + response.ReasonPhrase + ")."
                            + (string.IsNullOrWhiteSpace(details) ? string.Empty : "\n" + details));
                    }
                }
            }
        }

        private string requestAllowReassignColumn;
        private bool requestAllowReassignLooked;

        /// <summary>The approval request table's Allow Reassign column, if it has one (looked up once).</summary>
        private string RequestAllowReassignColumn()
        {
            if (!requestAllowReassignLooked)
            {
                requestAllowReassignLooked = true;
                var metadata = ((RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
                {
                    LogicalName = RequestEntity,
                    EntityFilters = EntityFilters.Attributes,
                })).EntityMetadata;
                requestAllowReassignColumn = metadata.Attributes
                    .OfType<BooleanAttributeMetadata>()
                    .FirstOrDefault(attribute => attribute.LogicalName.EndsWith("allowreassign", StringComparison.Ordinal) && attribute.IsValidForUpdate == true)
                    ?.LogicalName;
            }

            return requestAllowReassignColumn;
        }

        private void SetBoolean(string entity, Guid id, string column, bool value)
        {
            service.Update(new Entity(entity, id) { [column] = value });
        }

        private void SetOwner(Guid requestId, EntityReference owner)
        {
            service.Update(new Entity(RequestEntity, requestId) { ["ownerid"] = new EntityReference(owner.LogicalName ?? "systemuser", owner.Id) });
        }

        private static string Json(string value)
        {
            if (value == null)
            {
                return "null";
            }

            var builder = new StringBuilder("\"");
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            return builder.Append('"').ToString();
        }
    }
}

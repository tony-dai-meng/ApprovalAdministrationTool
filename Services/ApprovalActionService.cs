using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using ApprovalAdministrationTool.Models;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace ApprovalAdministrationTool.Services
{
    /// <summary>What the action panel can do to an approval.</summary>
    public enum ApprovalAction
    {
        Approve,
        Reject,
        Reassign,
        Cancel,
    }

    /// <summary>An approver's active approval request: the one row that is answered or handed to someone else.</summary>
    internal sealed class PendingRequest
    {
        public Guid RequestId { get; set; }
        public Guid ApprovalId { get; set; }
        public string ApprovalTitle { get; set; }
        public EntityReference Approver { get; set; }

        /// <summary>The answers the approver can give, as stored: a JSON list such as ["Approve","Reject"].</summary>
        public string Options { get; set; }

        /// <summary>Whether this answer is one of the choices (approvals with custom responses have others).</summary>
        public bool Accepts(string response)
        {
            return string.IsNullOrEmpty(Options) || Options.IndexOf("\"" + response + "\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>An approval that is still open, so it can be cancelled.</summary>
    internal sealed class OpenApproval
    {
        public Guid ApprovalId { get; set; }
        public string Title { get; set; }
        public EntityReference Owner { get; set; }
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
    /// Approves, rejects, reassigns and cancels approvals through the Teams Approvals API, which is undocumented (see
    /// https://www.matthewdevaney.com/reassign-an-approval-in-power-automate/ and
    /// https://www.matthewdevaney.com/cancel-an-approval-in-power-automate/ and their comments).
    /// The tool calls it as the person using the tool (a delegated token). The API only lets people act on rows they own,
    /// so the row (the approver's request, or the approval itself when cancelling) is first assigned to them in Dataverse
    /// and given back afterwards. The approval record always ends up with the owner it started with.
    /// If the API call fails, the Dataverse changes are undone. No UI code: call from inside WorkAsync.
    /// </summary>
    internal sealed class ApprovalActionService
    {
        private const string RequestEntity = "msdyn_flow_approvalrequest";
        private const string RequestApprovalLookup = "msdyn_flow_approvalrequest_approval";
        private const string RequestOptions = "msdyn_flow_approvalrequest_options";
        private const string ResponseEntity = "msdyn_flow_approvalresponse";
        private const string ApprovalTitle = "msdyn_flow_approval_title";
        private const string AllowReassign = "msdyn_flow_approval_allowreassign";
        private const string AllowCancel = "msdyn_flow_approval_allowcancel";

        private const string CommentFileName = "admin_comment.txt";

        private static readonly HttpClient Http = new HttpClient { BaseAddress = new Uri(TeamsApprovalsAuth.ApiBaseUrl) };
        private static int waitSeconds = 20; // how long a response or cancellation may take to appear in Dataverse

        private readonly IOrganizationService service;

        public ApprovalActionService(IOrganizationService service)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
        }

        // ------------------------------------------------------------ lookups

        /// <summary>The environment ID (sent to the API as FlowEnvironment) and the tenant to sign in to.</summary>
        public EnvironmentInfo GetEnvironment()
        {
            var detail = ((RetrieveCurrentOrganizationResponse)service.Execute(new RetrieveCurrentOrganizationRequest())).Detail;
            return new EnvironmentInfo { EnvironmentId = detail.EnvironmentId, TenantId = Guid.Parse(detail.TenantId) };
        }

        /// <summary>The active approval requests (one per approver still to respond) of those of the given approvals that are still open.</summary>
        public List<PendingRequest> GetPendingRequests(IReadOnlyCollection<Guid> approvalIds)
        {
            var result = new List<PendingRequest>();
            if (approvalIds.Count == 0)
            {
                return result;
            }

            var query = new QueryExpression(RequestEntity) { ColumnSet = new ColumnSet(RequestApprovalLookup, "ownerid", RequestOptions) };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); // Active
            query.Criteria.AddCondition(RequestApprovalLookup, ConditionOperator.In, approvalIds.Cast<object>().ToArray());

            // A finished approval (abandoned, cancelled...) keeps its unanswered requests active, so check the approval too.
            query.AddLink(ApprovalColumns.EntityName, RequestApprovalLookup, ApprovalColumns.Id)
                .LinkCriteria.AddCondition("statecode", ConditionOperator.Equal, 0);

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
                    Options = request.GetAttributeValue<string>(RequestOptions),
                });
            }

            return result;
        }

        /// <summary>Those of the given approvals that are still open.</summary>
        public List<OpenApproval> GetOpenApprovals(IReadOnlyCollection<Guid> approvalIds)
        {
            if (approvalIds.Count == 0)
            {
                return new List<OpenApproval>();
            }

            var query = new QueryExpression(ApprovalColumns.EntityName) { ColumnSet = new ColumnSet(ApprovalTitle, "ownerid") };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); // Active
            query.Criteria.AddCondition(ApprovalColumns.Id, ConditionOperator.In, approvalIds.Cast<object>().ToArray());
            return service.RetrieveMultiple(query).Entities
                .Select(approval => new OpenApproval
                {
                    ApprovalId = approval.Id,
                    Title = approval.GetAttributeValue<string>(ApprovalTitle) ?? approval.Id.ToString(),
                    Owner = approval.GetAttributeValue<EntityReference>("ownerid"),
                })
                .ToList();
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

        // ------------------------------------------------------------ actions

        /// <summary>
        /// Reassigns one approval request to <paramref name="newApprover"/>:
        /// 1. switches on Allow Reassign on the approval (and its request, if that table has the column) when off;
        /// 2. assigns the request to <paramref name="caller"/> (the person signed in to Teams Approvals) in Dataverse, unless it already owns it;
        /// 3. POSTs /api/reassignApproval with the Teams Approvals token;
        /// 4. gives the request back to its original owner (the approver it was reassigned from), so its history still
        ///    shows who it was with, and makes sure the approval record still has its owner.
        /// If step 3 fails, steps 1 and 2 are undone and the API's error is thrown.
        /// </summary>
        /// <returns>Null, or a warning if the reassignment worked but step 4 failed.</returns>
        public string Reassign(PendingRequest request, UserOption newApprover, UserOption caller, string comments, EnvironmentInfo environment, string accessToken)
        {
            var row = new EntityReference(RequestEntity, request.RequestId);
            EntityReference recordOwner;
            var undo = new List<Action>();
            try
            {
                var approval = service.Retrieve(ApprovalColumns.EntityName, request.ApprovalId, new ColumnSet(AllowReassign, "ownerid"));
                recordOwner = approval.GetAttributeValue<EntityReference>("ownerid");
                SwitchOn(approval, AllowReassign, undo);

                var requestAllowReassign = RequestAllowReassignColumn();
                if (requestAllowReassign != null)
                {
                    SwitchOn(service.Retrieve(RequestEntity, request.RequestId, new ColumnSet(requestAllowReassign)), requestAllowReassign, undo);
                }

                TakeOver(row, request.Approver, caller, undo);
                Send(HttpMethod.Post, "/api/reassignApproval", "{"
                    + "\"AssignedTo\":" + Json(newApprover.Email) + ","
                    + "\"ApprovalId\":" + Json(request.ApprovalId.ToString()) + ","
                    + "\"ApprovalRequestId\":" + Json(request.RequestId.ToString()) + ","
                    + "\"Properties\":{\"Response\":\"\",\"Comments\":" + Json(comments ?? string.Empty) + "},"
                    + "\"FlowEnvironment\":" + Json(environment.EnvironmentId)
                    + "}", accessToken);
            }
            catch
            {
                Undo(undo);
                throw;
            }

            return Join(
                GiveBack(row, request.Approver, caller, "the old request"),
                KeepRecordOwner(request.ApprovalId, recordOwner));
        }

        /// <summary>
        /// Answers one approval request with <paramref name="response"/> ("Approve" or "Reject"), as the person signed in:
        /// 1. assigns the request to <paramref name="caller"/> in Dataverse, unless they already own it;
        /// 2. POSTs /api/UpdateApproval with the Teams Approvals token;
        /// 3. waits for the response to appear in Dataverse, then gives the request back to the approver it was with and
        ///    makes sure the approval record still has its owner.
        /// If step 2 fails, step 1 is undone and the API's error is thrown.
        /// </summary>
        /// <returns>Null, or a warning if it was answered but step 3 did not go as expected.</returns>
        public string Respond(PendingRequest request, string response, UserOption caller, string comments, EnvironmentInfo environment, string accessToken)
        {
            var row = new EntityReference(RequestEntity, request.RequestId);
            var current = service.Retrieve(RequestEntity, request.RequestId, new ColumnSet("ownerid", "statecode"));
            if (current.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 || current.GetAttributeValue<EntityReference>("ownerid")?.Id != request.Approver.Id)
            {
                throw new InvalidOperationException("It was answered or reassigned by someone else in the meantime. Refresh and try again.");
            }

            // The API is told the approval but not the request, so it could not tell the caller's own request from this one.
            if (request.Approver.Id != caller.Id && HasOtherRequest(request, caller))
            {
                throw new InvalidOperationException(caller.Name + " is also an approver of this approval and has not answered yet, so Teams could not " +
                    "tell whose request this answers. Answer or reassign " + caller.Name + "'s own request first.");
            }

            var recordOwner = ApprovalOwner(request.ApprovalId);
            var responsesBefore = CountResponses(request.ApprovalId, caller);
            var undo = new List<Action>();
            try
            {
                TakeOver(row, request.Approver, caller, undo);
                Send(HttpMethod.Post, "/api/UpdateApproval", "{"
                    + "\"ApprovalId\":" + Json(request.ApprovalId.ToString()) + ","
                    + "\"Properties\":{\"Response\":" + Json(response) + ",\"Comments\":" + Json(comments ?? string.Empty) + "},"
                    + "\"FlowEnvironment\":" + Json(environment.EnvironmentId)
                    + "}", accessToken);
            }
            catch
            {
                Undo(undo);
                throw;
            }

            var recorded = WaitUntil(() => CountResponses(request.ApprovalId, caller) > responsesBefore);
            return Join(
                recorded ? null : "Teams accepted it, but the response has not appeared in Dataverse yet. Refresh in a minute to check.",
                GiveBack(row, request.Approver, caller, "the request"),
                KeepRecordOwner(request.ApprovalId, recordOwner));
        }

        /// <summary>
        /// Cancels an approval for all its approvers:
        /// 1. switches on Allow Cancel when off;
        /// 2. assigns the approval to <paramref name="caller"/> in Dataverse, unless they already own it (only its owner may
        ///    cancel it; assigning it does not cascade to its requests);
        /// 3. DELETEs /api/cancelApproval with the Teams Approvals token;
        /// 4. waits for it to close in Dataverse, gives it back to its owner, and attaches <paramref name="comments"/>
        ///    to it as a text file (the API takes no comment).
        /// If step 3 fails, steps 1 and 2 are undone and the API's error is thrown.
        /// </summary>
        /// <returns>Null, or a warning if it was cancelled but step 4 did not go as expected.</returns>
        public string Cancel(OpenApproval approval, UserOption caller, string comments, EnvironmentInfo environment, string accessToken)
        {
            var row = new EntityReference(ApprovalColumns.EntityName, approval.ApprovalId);
            var undo = new List<Action>();
            try
            {
                SwitchOn(service.Retrieve(row.LogicalName, row.Id, new ColumnSet(AllowCancel)), AllowCancel, undo);
                TakeOver(row, approval.Owner, caller, undo);
                Send(HttpMethod.Delete, "/api/cancelApproval/" + approval.ApprovalId + "?flowEnvironment=" + Uri.EscapeDataString(environment.EnvironmentId), null, accessToken);
            }
            catch
            {
                Undo(undo);
                throw;
            }

            var closed = WaitUntil(() => service.Retrieve(row.LogicalName, row.Id, new ColumnSet("statecode")).GetAttributeValue<OptionSetValue>("statecode")?.Value == 1);
            return Join(
                closed ? null : "Teams accepted it, but the approval is still open in Dataverse. Refresh in a minute to check.",
                GiveBack(row, approval.Owner, caller, "the approval"),
                AttachComment(row, comments));
        }

        /// <summary>
        /// Attaches the comment as a text file. Teams lists every note on an approval as an attachment, and shows one
        /// without a file as "null.txt", so the note needs a file and a name.
        /// </summary>
        private string AttachComment(EntityReference approval, string comments)
        {
            if (string.IsNullOrWhiteSpace(comments))
            {
                return null;
            }

            try
            {
                service.Create(new Entity("annotation")
                {
                    ["objectid"] = approval,
                    ["subject"] = "Cancelled",
                    ["notetext"] = comments,
                    ["isdocument"] = true,
                    ["filename"] = CommentFileName,
                    ["mimetype"] = "text/plain",
                    ["documentbody"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(comments)),
                });
                return null;
            }
            catch (Exception ex)
            {
                return "Done, but the comment could not be attached: " + ex.Message;
            }
        }

        // ------------------------------------------------------------ borrowing rows and giving them back

        /// <summary>Switches on a Yes/No column if it is off.</summary>
        private void SwitchOn(Entity row, string column, List<Action> undo)
        {
            if (row.Contains(column) && !row.GetAttributeValue<bool>(column))
            {
                SetBoolean(row.LogicalName, row.Id, column, true);
                undo.Add(() => SetBoolean(row.LogicalName, row.Id, column, false));
            }
        }

        /// <summary>Assigns a row to the caller, unless they already own it.</summary>
        private void TakeOver(EntityReference row, EntityReference owner, UserOption caller, List<Action> undo)
        {
            if (owner.Id != caller.Id)
            {
                SetOwner(row, new EntityReference("systemuser", caller.Id));
                undo.Add(() => SetOwner(row, owner));
            }
        }

        private static void Undo(List<Action> undo)
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
        }

        private string GiveBack(EntityReference row, EntityReference owner, UserOption caller, string what)
        {
            if (owner.Id == caller.Id)
            {
                return null;
            }

            try
            {
                SetOwner(row, owner);
                return null;
            }
            catch (Exception ex)
            {
                return "Done, but " + what + " could not be given back to " + (owner.Name ?? owner.Id.ToString())
                    + " (it is still owned by " + caller.Name + "): " + ex.Message;
            }
        }

        private EntityReference ApprovalOwner(Guid approvalId)
        {
            return service.Retrieve(ApprovalColumns.EntityName, approvalId, new ColumnSet("ownerid")).GetAttributeValue<EntityReference>("ownerid");
        }

        /// <summary>Puts the approval record's owner (who requested it) back if the action changed it.</summary>
        private string KeepRecordOwner(Guid approvalId, EntityReference owner)
        {
            if (owner == null)
            {
                return null;
            }

            try
            {
                if (ApprovalOwner(approvalId)?.Id != owner.Id)
                {
                    SetOwner(new EntityReference(ApprovalColumns.EntityName, approvalId), owner);
                }

                return null;
            }
            catch (Exception ex)
            {
                return "Done, but could not make sure the approval is still owned by " + (owner.Name ?? owner.Id.ToString()) + ": " + ex.Message;
            }
        }

        private bool HasOtherRequest(PendingRequest request, UserOption user)
        {
            var query = new QueryExpression(RequestEntity) { ColumnSet = new ColumnSet(false), TopCount = 1 };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition(RequestApprovalLookup, ConditionOperator.Equal, request.ApprovalId);
            query.Criteria.AddCondition("ownerid", ConditionOperator.Equal, user.Id);
            query.Criteria.AddCondition("msdyn_flow_approvalrequestid", ConditionOperator.NotEqual, request.RequestId);
            return service.RetrieveMultiple(query).Entities.Count > 0;
        }

        private int CountResponses(Guid approvalId, UserOption user)
        {
            var query = new QueryExpression(ResponseEntity) { ColumnSet = new ColumnSet(false) };
            query.Criteria.AddCondition("msdyn_flow_approvalresponse_approval", ConditionOperator.Equal, approvalId);
            query.Criteria.AddCondition("ownerid", ConditionOperator.Equal, user.Id);
            return service.RetrieveMultiple(query).Entities.Count;
        }

        /// <summary>Checks once a second, for up to <see cref="waitSeconds"/>, until true.</summary>
        private static bool WaitUntil(Func<bool> done)
        {
            for (var second = 0; ; second++)
            {
                try
                {
                    if (done())
                    {
                        return true;
                    }
                }
                catch
                {
                    // The action itself worked; a failed check must not stop the row being given back.
                }

                if (second >= waitSeconds)
                {
                    return false;
                }

                Thread.Sleep(1000);
            }
        }

        private static string Join(params string[] warnings)
        {
            var text = string.Join("\n", warnings.Where(warning => warning != null));
            return text.Length == 0 ? null : text;
        }

        private string requestAllowReassignColumn;
        private bool requestAllowReassignLooked;

        /// <summary>The approval request table's Allow Reassignment column, if it has one (looked up once).</summary>
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
                    .FirstOrDefault(attribute => attribute.LogicalName.EndsWith("allowreassignment", StringComparison.Ordinal) && attribute.IsValidForUpdate == true)
                    ?.LogicalName;
            }

            return requestAllowReassignColumn;
        }

        private void SetBoolean(string entity, Guid id, string column, bool value)
        {
            service.Update(new Entity(entity, id) { [column] = value });
        }

        private void SetOwner(EntityReference row, EntityReference owner)
        {
            service.Update(new Entity(row.LogicalName, row.Id) { ["ownerid"] = new EntityReference(owner.LogicalName ?? "systemuser", owner.Id) });
        }

        // ------------------------------------------------------------ Teams Approvals API

        private static void Send(HttpMethod method, string path, string body, string accessToken)
        {
            using (var message = new HttpRequestMessage(method, path))
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                message.Headers.Add("x-ms-client-request-id", Guid.NewGuid().ToString()); // the API refuses calls without one
                if (body != null)
                {
                    message.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }

                using (var response = Http.SendAsync(message).GetAwaiter().GetResult())
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var details = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        throw new InvalidOperationException(
                            "The Teams Approvals API refused it (" + (int)response.StatusCode + " " + response.ReasonPhrase + ")."
                            + (string.IsNullOrWhiteSpace(details) ? string.Empty : "\n" + details));
                    }
                }
            }
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

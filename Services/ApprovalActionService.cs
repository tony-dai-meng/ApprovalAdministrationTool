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
        Remind,

        /// <summary>Answer with a response picked per approval: the Action button, shown instead of Approve and Reject for custom responses.</summary>
        Respond,
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

        /// <summary>
        /// The other people this approval's open steps are with (user ID → 1-based step number): Teams will not reassign a
        /// request to any of them. Includes others on this request's own step.
        /// </summary>
        public IDictionary<Guid, int> OthersInSteps { get; set; } = new Dictionary<Guid, int>();

        /// <summary>Whether the approval has more than one step (a sequential approval).</summary>
        public bool Sequential { get; set; }

        /// <summary>Why this request cannot be reassigned to the user (e.g. "already on step 3"), or null if it can.</summary>
        public string WhyNotReassignTo(Guid userId)
        {
            if (Approver != null && Approver.Id == userId)
            {
                return "already the approver";
            }

            return OthersInSteps.TryGetValue(userId, out var step) ? (Sequential ? "already on step " + step : "already an approver") : null;
        }

        /// <summary>Whether the approval waits for everyone's response (so the request may stay active after it was answered).</summary>
        public bool WaitsForAll { get; set; }

        /// <summary>For an approval that waits for everyone: who has responded (and how) and who has not, in one line; otherwise null.</summary>
        public string ResponseSummary { get; set; }

        /// <summary>Whether this answer is one of the choices (approvals with custom responses have others).</summary>
        public bool Accepts(string response)
        {
            return string.IsNullOrEmpty(Options) || Choices.Any(choice => string.Equals(choice, response, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The answers the approver can give, read from <see cref="Options"/> (Approve and Reject when not stored).</summary>
        public IReadOnlyList<string> Choices => ParseChoices(Options);

        /// <summary>Whether the approval has custom responses (anything but exactly Approve and Reject).</summary>
        public bool IsCustom
        {
            get
            {
                var choices = Choices;
                return !(choices.Count == 2
                    && choices.Any(c => string.Equals(c, "Approve", StringComparison.OrdinalIgnoreCase))
                    && choices.Any(c => string.Equals(c, "Reject", StringComparison.OrdinalIgnoreCase)));
            }
        }

        internal static IReadOnlyList<string> ParseChoices(string options)
        {
            if (!string.IsNullOrWhiteSpace(options))
            {
                try
                {
                    using (var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(options)))
                    {
                        var parsed = (string[])new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(string[])).ReadObject(stream);
                        var choices = parsed.Where(choice => !string.IsNullOrWhiteSpace(choice)).ToList();
                        if (choices.Count > 0)
                        {
                            return choices;
                        }
                    }
                }
                catch (Exception)
                {
                    // Not a JSON list: treat as a basic approval.
                }
            }

            return new[] { "Approve", "Reject" };
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

    /// <summary>Who is using the tool, and whether they may use its actions.</summary>
    internal sealed class AdminAccess
    {
        public Guid UserId { get; set; }
        public string FullName { get; set; }

        /// <summary>Whether they have the System Administrator role in this environment (directly or through a team).</summary>
        public bool IsSystemAdministrator { get; set; }
    }

    /// <summary>
    /// Keeps a record of rows the tool has assigned to the admin for an action, so they can be given back if the action
    /// never finishes (XrmToolBox closed or crashed in the middle). Called off the UI thread.
    /// </summary>
    internal interface IBorrowJournal
    {
        /// <summary>Called just before <paramref name="row"/> is assigned from <paramref name="owner"/> to <paramref name="borrower"/>.</summary>
        void Borrowed(EntityReference row, EntityReference owner, UserOption borrower, string title);

        /// <summary>Called once the row is back with its owner.</summary>
        void Returned(EntityReference row);
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
        private const string ResponseApprovalLookup = "msdyn_flow_approvalresponse_approval";
        private const string ResponseStep = "msdyn_flow_approvalresponse_stepnumber";
        private const string ResponseValue = "msdyn_flow_approvalresponse_response";
        private const string ApprovalTitle = "msdyn_flow_approval_title";
        private const string AllowReassign = "msdyn_flow_approval_allowreassign";
        private const string AllowCancel = "msdyn_flow_approval_allowcancel";

        private const string CommentFileName = "admin_comment.txt";

        private static readonly HttpClient Http = new HttpClient { BaseAddress = new Uri(TeamsApprovalsAuth.ApiBaseUrl) };
        private static int waitSeconds = 20; // how long a response or cancellation may take to appear in Dataverse

        /// <summary>The role every System Administrator role copy (one per business unit) is made from.</summary>
        public static readonly Guid SystemAdministratorRoleTemplate = new Guid("627090ff-40a3-4053-8790-584edc5be201");

        private readonly IOrganizationService service;

        public ApprovalActionService(IOrganizationService service)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>Records rows assigned to the admin for an action until they are given back; optional.</summary>
        public IBorrowJournal Journal { get; set; }

        /// <summary>
        /// Who the connection's user is and whether they hold the System Administrator role in this environment, given to
        /// them directly or through a team they are a member of.
        /// </summary>
        public AdminAccess GetAdminAccess()
        {
            var userId = ((WhoAmIResponse)service.Execute(new WhoAmIRequest())).UserId;
            var user = service.Retrieve("systemuser", userId, new ColumnSet("fullname"));
            return new AdminAccess
            {
                UserId = userId,
                FullName = user.GetAttributeValue<string>("fullname") ?? userId.ToString(),
                IsSystemAdministrator = HasRoleDirectly(userId) || HasRoleThroughTeam(userId),
            };
        }

        private bool HasRoleDirectly(Guid userId)
        {
            var query = new QueryExpression("role") { ColumnSet = new ColumnSet(false), TopCount = 1 };
            query.Criteria.AddCondition("roletemplateid", ConditionOperator.Equal, SystemAdministratorRoleTemplate);
            var userRoles = query.AddLink("systemuserroles", "roleid", "roleid");
            userRoles.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
            return service.RetrieveMultiple(query).Entities.Count > 0;
        }

        private bool HasRoleThroughTeam(Guid userId)
        {
            var query = new QueryExpression("role") { ColumnSet = new ColumnSet(false), TopCount = 1 };
            query.Criteria.AddCondition("roletemplateid", ConditionOperator.Equal, SystemAdministratorRoleTemplate);
            var teamRoles = query.AddLink("teamroles", "roleid", "roleid");
            var membership = teamRoles.AddLink("teammembership", "teamid", "teamid");
            membership.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
            return service.RetrieveMultiple(query).Entities.Count > 0;
        }

        /// <summary>The Microsoft Entra ID object IDs of the given Dataverse users (those that have one).</summary>
        public Dictionary<Guid, Guid> GetEntraObjectIds(IEnumerable<Guid> userIds)
        {
            var ids = userIds.Distinct().Cast<object>().ToArray();
            var result = new Dictionary<Guid, Guid>();
            if (ids.Length == 0)
            {
                return result;
            }

            var query = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("azureactivedirectoryobjectid") };
            query.Criteria.AddCondition("systemuserid", ConditionOperator.In, ids);
            foreach (var user in service.RetrieveMultiple(query).Entities)
            {
                if (user.GetAttributeValue<Guid?>("azureactivedirectoryobjectid") is Guid objectId && objectId != Guid.Empty)
                {
                    result[user.Id] = objectId;
                }
            }

            return result;
        }

        /// <summary>
        /// Gives a row left with the admin back to its owner, if the admin still owns it.
        /// </summary>
        /// <returns>What happened, in a few words.</returns>
        public string GiveBackLeftover(EntityReference row, EntityReference owner, Guid borrowerId)
        {
            Entity current;
            try
            {
                current = service.Retrieve(row.LogicalName, row.Id, new ColumnSet("ownerid"));
            }
            catch (Exception ex)
            {
                return "could not be read (" + ex.Message + "); it may have been deleted";
            }

            var now = current.GetAttributeValue<EntityReference>("ownerid");
            if (now?.Id != borrowerId)
            {
                return now?.Id == owner.Id ? "already back with " + (owner.Name ?? "its owner") : "now owned by someone else, left as it is";
            }

            SetOwner(row, owner);
            return "given back to " + (owner.Name ?? owner.Id.ToString());
        }

        // ------------------------------------------------------------ lookups

        /// <summary>The environment ID (sent to the API as FlowEnvironment) and the tenant to sign in to.</summary>
        public EnvironmentInfo GetEnvironment()
        {
            var detail = ((RetrieveCurrentOrganizationResponse)service.Execute(new RetrieveCurrentOrganizationRequest())).Detail;
            return new EnvironmentInfo { EnvironmentId = detail.EnvironmentId, TenantId = Guid.Parse(detail.TenantId) };
        }

        /// <summary>
        /// The active approval requests (one per approver still to respond) of those of the given approvals that are still
        /// open, on each approval's current step (a sequential approval's later steps wait their turn).
        /// </summary>
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
            ApprovalSteps.LinkOpenApproval(query, RequestApprovalLookup);
            query.LinkEntities[query.LinkEntities.Count - 1].Columns.AddColumn(ApprovalColumns.ModelType);

            var all = service.RetrieveMultiple(query).Entities;

            // Approvals that wait for everyone keep answered requests active, so their responses tell who has answered.
            var waitForAll = new HashSet<Guid>(all
                .Where(request => ApprovalColumns.WaitForAllModelTypes.Contains(request.GetAttributeValue<AliasedValue>("approval." + ApprovalColumns.ModelType)?.Value as string ?? string.Empty))
                .Select(request => request.GetAttributeValue<EntityReference>(RequestApprovalLookup)?.Id ?? Guid.Empty));
            var responses = GetResponses(waitForAll);

            var summaries = new Dictionary<Guid, List<string>>();
            foreach (var request in all)
            {
                var approval = request.GetAttributeValue<EntityReference>(RequestApprovalLookup);
                var owner = request.GetAttributeValue<EntityReference>("ownerid");
                if (approval == null || owner == null || !ApprovalSteps.IsCurrent(request))
                {
                    continue;
                }

                if (owner.Name == null && request.FormattedValues.TryGetValue("ownerid", out var ownerName))
                {
                    owner.Name = ownerName;
                }

                if (waitForAll.Contains(approval.Id))
                {
                    var step = request.GetAttributeValue<int?>(ApprovalSteps.RequestStep) ?? 0;
                    var answered = responses.FirstOrDefault(response => response.ApprovalId == approval.Id && response.Step == step && response.OwnerId == owner.Id);
                    if (!summaries.TryGetValue(approval.Id, out var lines))
                    {
                        summaries[approval.Id] = lines = new List<string>();
                    }

                    lines.Add((owner.Name ?? owner.Id.ToString()) + (answered.OwnerId == Guid.Empty ? ": waiting" : ": responded " + (answered.Value ?? "(no value)")));
                    if (answered.OwnerId != Guid.Empty)
                    {
                        continue; // answered already: nothing left to answer or reassign
                    }
                }

                result.Add(new PendingRequest
                {
                    RequestId = request.Id,
                    ApprovalId = approval.Id,
                    ApprovalTitle = approval.Name ?? (request.FormattedValues.TryGetValue(RequestApprovalLookup, out var title) ? title : approval.Id.ToString()),
                    Approver = owner,
                    Options = request.GetAttributeValue<string>(RequestOptions),
                    WaitsForAll = waitForAll.Contains(approval.Id),
                });
            }

            foreach (var pending in result.Where(p => p.WaitsForAll))
            {
                pending.ResponseSummary = summaries.TryGetValue(pending.ApprovalId, out var lines) ? string.Join("; ", lines) : null;
            }

            // Who else each approval's steps are with (all steps' requests are active from the start).
            foreach (var pending in result)
            {
                var siblings = all.Where(request => request.GetAttributeValue<EntityReference>(RequestApprovalLookup)?.Id == pending.ApprovalId).ToList();
                pending.Sequential = siblings.Select(request => request.GetAttributeValue<int?>(ApprovalSteps.RequestStep) ?? 0).Distinct().Count() > 1;
                foreach (var other in siblings.Where(request => request.Id != pending.RequestId))
                {
                    var owner = other.GetAttributeValue<EntityReference>("ownerid");
                    var step = (other.GetAttributeValue<int?>(ApprovalSteps.RequestStep) ?? 0) + 1;
                    if (owner != null && owner.Id != pending.Approver.Id && (!pending.OthersInSteps.TryGetValue(owner.Id, out var known) || step < known))
                    {
                        pending.OthersInSteps[owner.Id] = step;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// For approvals that wait for everyone: who has responded on which step, and what. A response given on someone's
        /// behalf is owned by whoever gave it, so it counts for that person only.
        /// </summary>
        private List<(Guid ApprovalId, int Step, Guid OwnerId, string Value)> GetResponses(ICollection<Guid> approvalIds)
        {
            var list = new List<(Guid ApprovalId, int Step, Guid OwnerId, string Value)>();
            if (approvalIds.Count == 0)
            {
                return list;
            }

            var query = new QueryExpression(ResponseEntity) { ColumnSet = new ColumnSet(ResponseApprovalLookup, ResponseStep, ResponseValue, "ownerid") };
            query.Criteria.AddCondition(ResponseApprovalLookup, ConditionOperator.In, approvalIds.Cast<object>().ToArray());
            foreach (var response in service.RetrieveMultiple(query).Entities)
            {
                var approval = response.GetAttributeValue<EntityReference>(ResponseApprovalLookup);
                var owner = response.GetAttributeValue<EntityReference>("ownerid");
                if (approval != null && owner != null)
                {
                    list.Add((approval.Id, response.GetAttributeValue<int?>(ResponseStep) ?? 0, owner.Id, response.GetAttributeValue<string>(ResponseValue)));
                }
            }

            return list;
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
        /// Reassigns one approval request (of the approval's current step) to <paramref name="newApprover"/>:
        /// 0. refuses, before changing anything, if the request was answered, reassigned or left behind by a later step meanwhile,
        ///    or if <paramref name="newApprover"/> is already in one of the approval's steps (Teams refuses that);
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
            var row = new EntityReference(RequestEntity, request.RequestId) { Name = request.ApprovalTitle };
            EntityReference recordOwner;
            var undo = new List<Action>();
            try
            {
                var approval = service.Retrieve(ApprovalColumns.EntityName, request.ApprovalId, new ColumnSet(AllowReassign, "ownerid", ApprovalSteps.CurrentStep));
                CheckStillWaiting(request, approval);
                CheckNotInSteps(request, newApprover);
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
            var row = new EntityReference(RequestEntity, request.RequestId) { Name = request.ApprovalTitle };
            CheckStillWaiting(request, service.Retrieve(ApprovalColumns.EntityName, request.ApprovalId, new ColumnSet(ApprovalSteps.CurrentStep)));

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
            var row = new EntityReference(ApprovalColumns.EntityName, approval.ApprovalId) { Name = approval.Title };
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
                Journal?.Borrowed(row, owner, caller, row.Name);
                try
                {
                    SetOwner(row, new EntityReference("systemuser", caller.Id));
                }
                catch
                {
                    Journal?.Returned(row); // never assigned
                    throw;
                }

                undo.Add(() =>
                {
                    SetOwner(row, owner);
                    Journal?.Returned(row);
                });
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
                Journal?.Returned(row);
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

        /// <summary>
        /// Refuses (before anything changes) a request that is no longer waiting: answered or reassigned by someone else,
        /// or no longer on the approval's current step, since the list was loaded.
        /// </summary>
        private void CheckStillWaiting(PendingRequest request, Entity approval)
        {
            var current = service.Retrieve(RequestEntity, request.RequestId, new ColumnSet("ownerid", "statecode", ApprovalSteps.RequestStep));
            if (current.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 || current.GetAttributeValue<EntityReference>("ownerid")?.Id != request.Approver.Id)
            {
                throw new InvalidOperationException("It was answered or reassigned by someone else in the meantime. Refresh and try again.");
            }

            ApprovalSteps.CheckCurrent(current, approval);
        }

        /// <summary>
        /// Refuses (before anything changes) a new approver who already has an open request on the approval, on any step:
        /// Teams refuses to reassign to someone already in the approval's steps.
        /// </summary>
        private void CheckNotInSteps(PendingRequest request, UserOption newApprover)
        {
            var query = new QueryExpression(RequestEntity) { ColumnSet = new ColumnSet(ApprovalSteps.RequestStep) };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition(RequestApprovalLookup, ConditionOperator.Equal, request.ApprovalId);
            query.Criteria.AddCondition("ownerid", ConditionOperator.Equal, newApprover.Id);
            query.Criteria.AddCondition("msdyn_flow_approvalrequestid", ConditionOperator.NotEqual, request.RequestId);
            var steps = service.RetrieveMultiple(query).Entities.Select(other => (other.GetAttributeValue<int?>(ApprovalSteps.RequestStep) ?? 0) + 1).ToList();
            if (steps.Count > 0)
            {
                throw new InvalidOperationException(InStepsMessage(newApprover.Name, request.Sequential || steps.Any(step => step > 1) ? steps.Min() : (int?)null));
            }
        }

        private static string InStepsMessage(string name, int? step)
        {
            return (name ?? "They") + " is already " + (step.HasValue ? "on step " + step + " of this approval" : "an approver of this approval")
                + ", and Teams won't reassign to someone already in its steps. Pick someone else.";
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
                        if (details != null && details.IndexOf("ApprovalRequestReassignmentUserCurrentlyInSteps", StringComparison.Ordinal) >= 0)
                        {
                            throw new InvalidOperationException(InStepsMessage("The person picked", null)); // caught before calling, unless it changed meanwhile
                        }

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

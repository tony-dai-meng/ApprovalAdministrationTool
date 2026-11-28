using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ApprovalAdministrationTool.Controls;
using ApprovalAdministrationTool.Models;
using ApprovalAdministrationTool.Services;
using McTools.Xrm.Connection;
using Microsoft.Identity.Client;
using Microsoft.Xrm.Sdk;
using XrmToolBox.Extensibility;

namespace ApprovalAdministrationTool
{
    /// <summary>
    /// The tool's main screen. It owns the connection and settings, runs Dataverse work through WorkAsync
    /// (via <see cref="ApprovalService"/>) and hands the results to the child controls, which only display data.
    /// </summary>
    public partial class ApprovalAdminControl : PluginControlBase
    {
        private Settings settings;
        private bool showLogicalNames; // grid headers start on friendly (display) names
        private string activeFilterXml = ApprovalQuery.DefaultFilter; // the <filter> used by loads and Refresh (last one Run)
        private AdminAccess adminAccess;   // who is connected and whether they are a System Administrator (per connection)
        private string accessProblem;      // why the role check could not be done, if it failed
        private bool leftoversOffered;     // rows left with the admin are offered back once per connection
        private readonly object settingsLock = new object(); // settings are saved from worker threads too (borrowed rows)

        /// <summary>Shown under the action buttons when the connected user is not a System Administrator.</summary>
        internal const string NotAdministratorReason = "You must be a System Administrator for this environment to use these functions.";

        private const string CommentToolName = "PA Approval Admin Tool";

        public ApprovalAdminControl()
        {
            InitializeComponent();

            // Refresh shows the Windows refresh icon (tooltip "Refresh data"); it keeps its "Refresh" text if the icon font is missing.
            var refreshIcon = SymbolIcons.Create(SymbolIcons.Refresh, 20, toolStripMenu.ForeColor);
            if (refreshIcon != null)
            {
                tsbRefresh.Image = refreshIcon;
                tsbRefresh.DisplayStyle = ToolStripItemDisplayStyle.Image;
            }

            // Search bar (second toolbar row): magnifying-glass icon, falling back to the text "Search:".
            var searchIcon = SymbolIcons.Create(SymbolIcons.Search, 16, toolStripSearch.ForeColor);
            if (searchIcon != null)
            {
                tslSearchIcon.Image = searchIcon;
                tslSearchIcon.DisplayStyle = ToolStripItemDisplayStyle.Image;
            }

            TextBoxCueBanner.Set(tstSearch.TextBox, "Search all columns");

            // Query panel (hidden until "Modify query" is pressed). The fixed part is refreshed after each load with the
            // columns that exist in the connected environment.
            queryEditor.SetQueryParts(
                ApprovalQuery.Prefix(ApprovalColumns.All.Where(column => !ApprovalColumns.Calculated.ContainsKey(column))),
                ApprovalQuery.Suffix);
            queryEditor.FilterXml = activeFilterXml;
            queryEditor.RunRequested += QueryEditor_RunRequested;

            // Keep the query's filter in step with the grid's column filters and search.
            dataGridControl1.FiltersChanged += (sender, e) => queryEditor.SetGridFilters(dataGridControl1.FetchXmlForGridFilters());

            // The pane on the right shows the first ticked approval (or, with none ticked, the current row).
            dataGridControl1.PreviewApprovalChanged += (sender, e) =>
            {
                ShowPreview();
                UpdateActionButtons();
            };
            dataGridControl1.CheckedApprovalsChanged += (sender, e) => UpdateActionButtons();
            UpdateActionButtons();
        }

        /// <summary>
        /// The actions can be clicked when an approval that is still open is ticked or selected, and only by a System
        /// Administrator of the environment (otherwise all are off, with the reason shown under them).
        /// </summary>
        private void UpdateActionButtons()
        {
            actionPanel1.DisabledReason = accessProblem != null ? "Could not check your security roles, so the actions are off: " + accessProblem
                : adminAccess != null && !adminAccess.IsSystemAdministrator ? NotAdministratorReason
                : null;
            var chosen = dataGridControl1.CheckedApprovalIds.ToList();
            if (chosen.Count == 0 && dataGridControl1.PreviewApprovalId is Guid current && dataGridControl1.IsOpen(current))
            {
                chosen.Add(current);
            }

            actionPanel1.ActionsEnabled = chosen.Count > 0;

            // Approve and Reject mean nothing for custom responses: one Action button instead.
            actionPanel1.CustomResponses = chosen.Any(id => dataGridControl1.FindApproval(id) is System.Data.DataRow row
                && row.Table.Columns.Contains(ApprovalColumns.ModelType)
                && row[ApprovalColumns.ModelType] is string modelType
                && ApprovalColumns.CustomResponseModelTypes.Contains(modelType));
        }

        /// <summary>Renders the approval to preview (its details Markdown as HTML) in the pane on the right.</summary>
        private void ShowPreview()
        {
            var id = dataGridControl1.PreviewApprovalId;
            var row = id.HasValue ? dataGridControl1.FindApproval(id.Value) : null;
            if (row == null)
            {
                webContentControl1.ShowMessage("Select an approval to see its details.");
                return;
            }

            string Text(string column) =>
                row.Table.Columns.Contains(column + ApprovalColumnValues.FriendlySuffix) ? row[column + ApprovalColumnValues.FriendlySuffix] as string
                : row.Table.Columns.Contains(column) ? row[column] as string
                : null;

            var subtitle = string.Join("  ·  ", new[]
            {
                Text("ownerid") is string owner && owner.Length > 0 ? "Requested by " + owner : null,
                Text("statuscode"),
                Text(ApprovalColumns.CurrentlyWith) is string with && with.Length > 0 ? "With " + with : null,
            }.Where(part => !string.IsNullOrEmpty(part)));

            webContentControl1.ShowApproval(Text("msdyn_flow_approval_title"), subtitle, Text(ApprovalColumns.Details));
            LoadFlowChart(id.Value);
        }

        // ---------------------------------------------------------------- flow chart

        private int flowVersion;                              // which preview a chart being loaded belongs to
        private ProfilePhotos profilePhotos = new ProfilePhotos(); // per connection
        private Guid? tenantId;                               // the connection's tenant, once looked up

        /// <summary>
        /// Draws the previewed approval's flow chart in the background: first with initials (or Dataverse user images),
        /// then with Microsoft 365 profile photos if the saved Teams Approvals sign-in can read them (it never asks to
        /// sign in for this). A chart for an approval no longer previewed is dropped.
        /// </summary>
        private async void LoadFlowChart(Guid approvalId)
        {
            var version = ++flowVersion;
            var service = Service;
            if (service == null)
            {
                return;
            }

            try
            {
                var flow = await Task.Run(() => new ApprovalFlowService(service).Load(approvalId));
                if (version != flowVersion)
                {
                    return;
                }

                var photos = profilePhotos;
                webContentControl1.ShowFlow(ApprovalFlowHtml.Render(flow, person => photos.Find(person.EntraObjectId) ?? person.DataverseImage));

                var ids = flow.Everyone.Select(person => person.EntraObjectId).ToList();
                if (photos.HasAll(ids))
                {
                    return;
                }

                var token = await GetGraphTokenSilentlyAsync(service);
                if (token == null)
                {
                    return;
                }

                await photos.LoadAsync(ids, token);
                if (version == flowVersion)
                {
                    webContentControl1.ShowFlow(ApprovalFlowHtml.Render(flow, person => photos.Find(person.EntraObjectId) ?? person.DataverseImage));
                }
            }
            catch (Exception ex)
            {
                if (version == flowVersion)
                {
                    webContentControl1.ShowFlow("<p class=\"empty\">Could not load who this approval went to: " + System.Net.WebUtility.HtmlEncode(ex.Message) + "</p>");
                }
            }
        }

        /// <summary>A Microsoft Graph token from the saved Teams Approvals sign-in, or null (never shows a sign-in window).</summary>
        private async Task<string> GetGraphTokenSilentlyAsync(IOrganizationService service)
        {
            if (string.IsNullOrEmpty(settings?.TeamsApprovalsTokenCache))
            {
                return null;
            }

            if (tenantId == null)
            {
                tenantId = await Task.Run(() => new ApprovalActionService(service).GetEnvironment().TenantId);
            }

            var clientId = string.IsNullOrEmpty(settings.TeamsApprovalsClientId) ? TeamsApprovalsAuth.DefaultClientId : settings.TeamsApprovalsClientId;
            var auth = new TeamsApprovalsAuth(clientId, tenantId.Value, settings, SaveSettings);
            return await auth.GetTokenSilentlyAsync(ProfilePhotos.GraphResource, ConnectionDetail?.UserName);
        }

        private void SaveSettings()
        {
            lock (settingsLock)
            {
                SettingsManager.Instance.Save(GetType(), settings);
            }
        }

        /// <summary>
        /// Writes each row the tool assigns to the admin into <see cref="Settings.BorrowedRows"/> (saved at once) and
        /// removes it once it is back, so rows left behind by an action that never finished can be given back later.
        /// </summary>
        private sealed class SettingsJournal : IBorrowJournal
        {
            private readonly ApprovalAdminControl control;
            private readonly string organizationUrl;

            public SettingsJournal(ApprovalAdminControl control, string organizationUrl)
            {
                this.control = control;
                this.organizationUrl = organizationUrl;
            }

            public void Borrowed(EntityReference row, EntityReference owner, UserOption borrower, string title)
            {
                lock (control.settingsLock)
                {
                    control.settings.BorrowedRows.RemoveAll(saved => saved.RowId == row.Id);
                    control.settings.BorrowedRows.Add(new BorrowedRow
                    {
                        OrganizationUrl = organizationUrl,
                        EntityName = row.LogicalName,
                        RowId = row.Id,
                        Title = title,
                        OwnerEntityName = owner.LogicalName ?? "systemuser",
                        OwnerId = owner.Id,
                        OwnerName = owner.Name,
                        BorrowerId = borrower.Id,
                        BorrowedOn = DateTime.UtcNow,
                    });
                    SettingsManager.Instance.Save(control.GetType(), control.settings);
                }
            }

            public void Returned(EntityReference row)
            {
                lock (control.settingsLock)
                {
                    if (control.settings.BorrowedRows.RemoveAll(saved => saved.RowId == row.Id) > 0)
                    {
                        SettingsManager.Instance.Save(control.GetType(), control.settings);
                    }
                }
            }
        }

        /// <summary>
        /// Rows an earlier action assigned to the admin in this environment and never gave back (XrmToolBox closed or
        /// crashed mid-action): offered back once per connection, after the approvals load.
        /// </summary>
        private void OfferLeftovers()
        {
            if (leftoversOffered || settings?.BorrowedRows == null || Service == null)
            {
                return;
            }

            var url = ConnectionDetail?.WebApplicationUrl;
            List<BorrowedRow> leftovers;
            lock (settingsLock)
            {
                leftovers = settings.BorrowedRows.Where(row => string.Equals(row.OrganizationUrl, url, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            leftoversOffered = true;
            if (leftovers.Count == 0)
            {
                return;
            }

            var list = string.Join("\n", leftovers.Take(10).Select(row => "\u2022 " + (row.Title ?? row.RowId.ToString())
                + (row.EntityName == ApprovalColumns.EntityName ? " (the approval)" : " (an approval request)")
                + ", from " + (row.OwnerName ?? "its owner") + ", " + row.BorrowedOn.ToLocalTime().ToString("g")));
            if (leftovers.Count > 10)
            {
                list += "\n\u2022 and " + (leftovers.Count - 10) + " more";
            }

            var answer = MessageBox.Show(
                "An earlier action did not finish (XrmToolBox may have closed in the middle), so " + (leftovers.Count == 1 ? "this row was" : "these rows were")
                + " left assigned to you:\n\n" + list + "\n\nGive them back to their owners now? Rows someone else has taken since are left as they are.",
                "Give back borrowed rows", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                return;
            }

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Giving rows back...",
                Work = (worker, args) =>
                {
                    var actions = new ApprovalActionService(Service);
                    var lines = new List<string>();
                    foreach (var row in leftovers)
                    {
                        var what = row.Title ?? row.RowId.ToString();
                        try
                        {
                            lines.Add(what + ": " + actions.GiveBackLeftover(new EntityReference(row.EntityName, row.RowId),
                                new EntityReference(row.OwnerEntityName ?? "systemuser", row.OwnerId) { Name = row.OwnerName }, row.BorrowerId));
                            lock (settingsLock)
                            {
                                settings.BorrowedRows.RemoveAll(saved => saved.RowId == row.RowId);
                            }
                        }
                        catch (Exception ex)
                        {
                            lines.Add(what + ": not given back (" + ex.Message + ")");
                        }
                    }

                    SaveSettings();
                    args.Result = lines;
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        ShowErrorDialog(args.Error, "Give back borrowed rows");
                        return;
                    }

                    MessageBox.Show(string.Join("\n", (List<string>)args.Result), "Give back borrowed rows", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    ExecuteMethod(LoadApprovals);
                },
            });
        }

        // ---------------------------------------------------------------- actions

        /// <summary>
        /// Approve, Reject, Reassign or Cancel (action panel): acts on the ticked approvals, or the current row's if none
        /// are ticked. Looks up what can be acted on, asks for the comment (and the new approver when reassigning), gets a
        /// Teams Approvals token as the person using the tool (saved sign-in first), does it and reloads.
        /// </summary>
        private void actionPanel1_ActionRequested(object sender, ApprovalAction action)
        {
            var ids = dataGridControl1.CheckedApprovalIds.ToList();
            if (ids.Count == 0 && dataGridControl1.PreviewApprovalId is Guid current && dataGridControl1.IsOpen(current))
            {
                ids.Add(current);
            }

            if (ids.Count == 0)
            {
                MessageBox.Show("Tick the approvals (or select one) first.", action.ToString(), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (actionPanel1.DisabledReason != null)
            {
                MessageBox.Show(actionPanel1.DisabledReason, action.ToString(), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // XrmToolBox needs a named method (not a lambda) here, so it can run it again after asking for a connection.
            ExecuteMethod(FindTargets, Tuple.Create(action, ids));
        }

        /// <summary>What an action can apply to, looked up in Dataverse.</summary>
        private sealed class ActionTargets
        {
            public EnvironmentInfo Environment { get; set; }
            public List<PendingRequest> Requests { get; set; }
            public List<OpenApproval> Approvals { get; set; }
            public List<UserOption> People { get; set; }
        }

        private void FindTargets(Tuple<ApprovalAction, List<Guid>> choice)
        {
            var (action, ids) = choice;
            WorkAsync(new WorkAsyncInfo
            {
                Message = action == ApprovalAction.Reassign ? "Finding approvers and people..." : "Checking the approvals...",
                Work = (worker, args) =>
                {
                    var actions = new ApprovalActionService(Service);
                    args.Result = new ActionTargets
                    {
                        Environment = actions.GetEnvironment(),
                        Requests = action == ApprovalAction.Cancel ? null : actions.GetPendingRequests(ids),
                        Approvals = action == ApprovalAction.Cancel ? actions.GetOpenApprovals(ids) : null,
                        People = action == ApprovalAction.Reassign ? actions.GetPeople() : null,
                    };
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        ShowErrorDialog(args.Error, action.ToString());
                        return;
                    }

                    var targets = (ActionTargets)args.Result;
                    foreach (var request in targets.Requests ?? new List<PendingRequest>())
                    {
                        // The approval request's lookup to its approval carries no name, so use the titles shown in the grid.
                        request.ApprovalTitle = GridTitle(request.ApprovalId);
                    }

                    switch (action)
                    {
                        case ApprovalAction.Reassign: ChooseAndReassign(ids, targets); break;
                        case ApprovalAction.Cancel: ChooseAndCancel(ids, targets); break;
                        case ApprovalAction.Remind: ChooseAndRemind(ids, targets); break;
                        default: ChooseAndRespond(action, ids, targets); break;
                    }
                },
            });
        }

        private string GridTitle(Guid approvalId)
        {
            return dataGridControl1.FindApproval(approvalId)?["msdyn_flow_approval_title"] is string title && title.Length > 0 ? title : approvalId.ToString();
        }

        private static string Quantity(IReadOnlyCollection<string> titles)
        {
            return titles.Count == 1 ? "\"" + titles.First() + "\"" : titles.Count + " approvals";
        }

        private void ChooseAndReassign(List<Guid> ids, ActionTargets targets)
        {
            var requests = targets.Requests;
            var skipped = ids.Where(id => requests.All(request => request.ApprovalId != id)).Select(GridTitle).ToList();
            if (requests.Count == 0)
            {
                MessageBox.Show("None of the chosen approvals is waiting on anyone, so there is nothing to reassign.",
                    "Reassign", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new ReassignDialog(requests, skipped, targets.People))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var newApprover = dialog.NewApprover;
                var comments = dialog.Comments;
                var chosen = dialog.SelectedRequests.Where(request => request.Approver.Id != newApprover.Id).ToList(); // those already in the steps are refused with the reason
                Run("Reassign", "Reassigning", "reassigned to " + newApprover.Name, targets.Environment, chosen,
                    request => request.ApprovalTitle + " (" + request.Approver.Name + ")",
                    (actions, request, signedInUser, token) => actions.Reassign(request, newApprover, signedInUser, comments, targets.Environment, token));
            }
        }

        /// <summary>
        /// Approve or Reject: answers the chosen approvers' requests. Approvals with custom responses are included too,
        /// with a dropdown each to pick the response (starting on the one named like the button, if there is one).
        /// Action (<see cref="ApprovalAction.Respond"/>, shown instead of Approve and Reject for custom responses): every
        /// approval gets a dropdown, nothing is picked to start with, and the window's button names the response picked.
        /// </summary>
        private void ChooseAndRespond(ApprovalAction action, List<Guid> ids, ActionTargets targets)
        {
            if (action == ApprovalAction.Respond)
            {
                ChooseAndAct(ids, targets);
                return;
            }

            var name = action.ToString(); // also the answer Teams expects
            var requests = targets.Requests;
            var skipped = ids.Where(id => requests.All(request => request.ApprovalId != id)).Select(id => GridTitle(id) + " (not waiting on anyone)").ToList();
            if (requests.Count == 0)
            {
                MessageBox.Show("None of the chosen approvals is waiting on anyone, so there is nothing to " + name.ToLowerInvariant() + ":\n" + string.Join("\n", skipped),
                    name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // An approval waiting on several people: choose whose request to answer.
            var items = requests.Select(request => new RespondDialog.Item(request,
                request.ApprovalTitle + "  \u2014  " + (request.Approver.Name ?? request.Approver.Id.ToString()),
                requests.Count(other => other.ApprovalId == request.ApprovalId) == 1,
                request.IsCustom ? request.Choices : null,
                name));
            var titles = requests.GroupBy(request => request.ApprovalId).Select(group => group.First().ApprovalTitle).ToList();
            var anyCustom = requests.Any(request => request.IsCustom);
            using (var dialog = new RespondDialog(name + " " + Quantity(titles), name,
                "The response is sent from your own sign-in, in place of the approver's. It cannot be undone."
                + (anyCustom ? " Approvals with custom responses get the response picked for them." : string.Empty),
                name + " for", items, skipped, WaitForAllNotes(requests)))
            {
                dialog.Comments = DefaultComment(action == ApprovalAction.Approve ? "Approved" : "Rejected");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var comments = dialog.Comments;
                var chosen = dialog.Selected<PendingRequest>();
                var answers = chosen.ToDictionary(request => request, request => request.IsCustom ? dialog.Response(request) : name);
                var custom = chosen.Any(request => request.IsCustom);
                Run(name,
                    custom ? "Responding" : action == ApprovalAction.Approve ? "Approving" : "Rejecting",
                    custom ? "answered" : action == ApprovalAction.Approve ? "approved" : "rejected",
                    targets.Environment, chosen,
                    request => request.ApprovalTitle + " (" + request.Approver.Name + (request.IsCustom ? ": " + answers[request] : string.Empty) + ")",
                    (actions, request, signedInUser, token) => actions.Respond(request, answers[request], signedInUser, comments, targets.Environment, token));
            }
        }

        /// <summary>The Action window: a response picked per approval, sent as it is.</summary>
        private void ChooseAndAct(List<Guid> ids, ActionTargets targets)
        {
            var requests = targets.Requests;
            var skipped = ids.Where(id => requests.All(request => request.ApprovalId != id)).Select(id => GridTitle(id) + " (not waiting on anyone)").ToList();
            if (requests.Count == 0)
            {
                MessageBox.Show("None of the chosen approvals is waiting on anyone, so there is nothing to respond to:\n" + string.Join("\n", skipped),
                    "Action", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var items = requests.Select(request => new RespondDialog.Item(request,
                request.ApprovalTitle + "  \u2014  " + (request.Approver.Name ?? request.Approver.Id.ToString()),
                requests.Count(other => other.ApprovalId == request.ApprovalId) == 1,
                request.Choices,
                null));
            var titles = requests.GroupBy(request => request.ApprovalId).Select(group => group.First().ApprovalTitle).ToList();
            using (var dialog = new RespondDialog("Respond to " + Quantity(titles), "Respond",
                "The response picked is sent from your own sign-in, in place of the approver's. It cannot be undone.",
                "Respond for", items, skipped, WaitForAllNotes(requests)))
            {
                dialog.ResponseHeading = "Response";
                dialog.ButtonNamesResponse = true;
                dialog.Comments = DefaultComment("Answered");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var comments = dialog.Comments;
                var chosen = dialog.Selected<PendingRequest>();
                var answers = chosen.ToDictionary(request => request, request => dialog.Response(request));
                Run("Action", "Responding", "answered", targets.Environment, chosen,
                    request => request.ApprovalTitle + " (" + request.Approver.Name + ": " + answers[request] + ")",
                    (actions, request, signedInUser, token) => actions.Respond(request, answers[request], signedInUser, comments, targets.Environment, token));
            }
        }

        /// <summary>
        /// For approvals that wait for everyone (Currently With shows N/A), who has responded and who has not, one line
        /// per approval, so it is clear whose request is still open.
        /// </summary>
        private static List<string> WaitForAllNotes(IEnumerable<PendingRequest> requests)
        {
            return requests.Where(request => request.WaitsForAll && request.ResponseSummary != null)
                .GroupBy(request => request.ApprovalId)
                .Select(group => "Everyone must respond to \"" + group.First().ApprovalTitle + "\": " + group.First().ResponseSummary + ".")
                .ToList();
        }

        /// <summary>The comment the window starts with, naming the admin, so the approval history shows who did it.</summary>
        private string DefaultComment(string verb)
        {
            var who = adminAccess?.FullName ?? ConnectionDetail?.UserName ?? "an administrator";
            return verb + " by " + who + " (administrator) on the approver's behalf, using " + CommentToolName + ".";
        }

        /// <summary>
        /// Remind: sends each chosen approver a Teams chat message from the admin (through Microsoft Graph) with the
        /// approval's title, a note and a link to their approvals. Nothing in the approval changes.
        /// </summary>
        private void ChooseAndRemind(List<Guid> ids, ActionTargets targets)
        {
            var requests = targets.Requests;
            var skipped = ids.Where(id => requests.All(request => request.ApprovalId != id)).Select(id => GridTitle(id) + " (not waiting on anyone)").ToList();
            if (requests.Count == 0)
            {
                MessageBox.Show("None of the chosen approvals is waiting on anyone, so there is no one to remind.", "Remind", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var items = requests.Select(request => new RespondDialog.Item(request,
                request.ApprovalTitle + "  \u2014  " + (request.Approver.Name ?? request.Approver.Id.ToString()), true));
            var titles = requests.GroupBy(request => request.ApprovalId).Select(group => group.First().ApprovalTitle).ToList();
            using (var dialog = new RespondDialog("Remind about " + Quantity(titles), "Send reminder",
                "Each approver gets a Teams chat message from you (or an email, if Teams chats are not allowed) with the approval's title, this note and a link to their approvals. Nothing in the approval changes.",
                "Remind", items, skipped, WaitForAllNotes(requests)))
            {
                dialog.Comments = "Hi, a quick reminder that this approval is waiting on your response. Thank you!";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var note = dialog.Comments;
                var link = ReminderService.ReceivedApprovalsLink(targets.Environment.EnvironmentId);
                Run("Remind", "Reminding", "reminded", targets.Environment, dialog.Selected<PendingRequest>(),
                    request => request.ApprovalTitle + " (" + request.Approver.Name + ")",
                    (actions, request, signedInUser, token) =>
                    {
                        var objectIds = actions.GetEntraObjectIds(new[] { signedInUser.Id, request.Approver.Id });
                        if (!objectIds.TryGetValue(request.Approver.Id, out var approver))
                        {
                            throw new InvalidOperationException((request.Approver.Name ?? "The approver") + " has no Microsoft Entra ID account, so cannot get a Teams chat.");
                        }

                        if (!objectIds.TryGetValue(signedInUser.Id, out var admin))
                        {
                            throw new InvalidOperationException("Your user has no Microsoft Entra ID account in this environment.");
                        }

                        return new ReminderService(token).Send(admin, approver, request.ApprovalTitle, note, link);
                    },
                    ReminderService.GraphResource);
            }
        }

        private void ChooseAndCancel(List<Guid> ids, ActionTargets targets)
        {
            var approvals = targets.Approvals;
            var skipped = ids.Where(id => approvals.All(approval => approval.ApprovalId != id)).Select(id => GridTitle(id) + " (already finished)").ToList();
            if (approvals.Count == 0)
            {
                MessageBox.Show("None of the chosen approvals is still open, so there is nothing to cancel.", "Cancel approval", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new RespondDialog("Cancel " + Quantity(approvals.Select(approval => approval.Title).ToList()), "Cancel approval",
                "Cancels for every approver and cannot be undone. Your comment is attached to the approval as admin_comment.txt.",
                "Approvals to cancel", approvals.Select(approval => new RespondDialog.Item(approval, approval.Title, true)), skipped))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var comments = dialog.Comments;
                Run("Cancel approval", "Cancelling", "cancelled", targets.Environment, dialog.Selected<OpenApproval>(), approval => approval.Title,
                    (actions, approval, signedInUser, token) => actions.Cancel(approval, signedInUser, comments, targets.Environment, token));
            }
        }

        /// <summary>
        /// A delegated token for the Teams Approvals API (https://approvals.teams.microsoft.com) in the environment's tenant,
        /// as the person using the tool: the saved sign-in if it still works, otherwise the Microsoft sign-in window with
        /// their XrmToolBox connection's account pre-filled. Signs in as Microsoft's Teams client unless an app
        /// registration's client ID is saved; one is asked for (with Entra's error) only if Entra refuses the client.
        /// Null if cancelled or if Entra refuses for another reason (shown to the user).
        /// </summary>
        /// <param name="resource">Another resource to get the token for (Microsoft Graph, to send reminders), or null.</param>
        private async Task<AuthenticationResult> GetTeamsApprovalsTokenAsync(Guid tenantId, string caption, string resource = null)
        {
            string error = null;
            while (true)
            {
                if (error != null)
                {
                    using (var dialog = new ClientIdDialog(settings.TeamsApprovalsClientId, error))
                    {
                        if (dialog.ShowDialog(this) != DialogResult.OK)
                        {
                            return null;
                        }

                        if (dialog.ClientId != settings.TeamsApprovalsClientId)
                        {
                            settings.TeamsApprovalsTokenCache = null; // a sign-in for another app registration is no use
                        }

                        settings.TeamsApprovalsClientId = dialog.ClientId;
                        SaveSettings();
                    }
                }

                try
                {
                    var clientId = string.IsNullOrEmpty(settings.TeamsApprovalsClientId) ? TeamsApprovalsAuth.DefaultClientId : settings.TeamsApprovalsClientId;
                    var auth = new TeamsApprovalsAuth(clientId, tenantId, settings, SaveSettings);
                    return await auth.GetTokenAsync(ConnectionDetail?.UserName, ParentForm?.Handle ?? Handle, resource);
                }
                catch (MsalClientException ex) when (ex.ErrorCode == MsalError.AuthenticationCanceledError)
                {
                    return null;
                }
                catch (MsalServiceException ex) when (IsAppRegistrationError(ex))
                {
                    error = (string.IsNullOrEmpty(settings.TeamsApprovalsClientId)
                        ? "Entra ID would not let Microsoft's Teams sign-in get this token, so this needs your own app registration:\n"
                        : "Entra ID rejected the app registration:\n") + FirstLine(ex.Message);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Entra ID would not give you a token for " + (resource ?? "the Teams Approvals API (" + TeamsApprovalsAuth.ApiBaseUrl + ")") + ":\n\n" + ex.Message +
                        "\n\nThe app registration needs a delegated permission on the Approvals API (with admin consent if required). " +
                        "If the Approvals API offers none in your tenant, only Microsoft's own apps can call it.",
                        caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }
            }
        }

        /// <summary>Entra errors that mean the app registration (client ID or its set-up) is wrong, so asking again may help.</summary>
        private static bool IsAppRegistrationError(MsalServiceException ex)
        {
            // AADSTS700016 app not found in the tenant, AADSTS50011 redirect URI not registered,
            // AADSTS7000218 public client flows not allowed, AADSTS90002 tenant not found, AADSTS65002 a Microsoft client
            // not pre-authorised for the resource, AADSTS650057 the permission is not on the app, AADSTS53003 blocked by
            // Conditional Access, AADSTS500113 no reply address registered.
            return ex.ErrorCode == "unauthorized_client" || ex.ErrorCode == "invalid_client"
                || new[] { "AADSTS700016", "AADSTS50011", "AADSTS7000218", "AADSTS90002", "AADSTS65002", "AADSTS650057", "AADSTS53003", "AADSTS500113" }.Any(ex.Message.Contains);
        }

        private static string FirstLine(string text)
        {
            var end = text.IndexOfAny(new[] { '\r', '\n' });
            return end < 0 ? text : text.Substring(0, end);
        }

        /// <summary>Signs in, does an action to each target, shows a summary and reloads.</summary>
        /// <param name="caption">The message boxes' title.</param>
        /// <param name="doing">For the progress message, such as "Approving".</param>
        /// <param name="done">For the summary, such as "approved".</param>
        /// <param name="describe">How a target is named.</param>
        /// <param name="act">Does it as the signed-in user with their token; returns a warning or null.</param>
        /// <param name="resource">What the token is for: the Teams Approvals API if null, or e.g. Microsoft Graph.</param>
        private async void Run<T>(string caption, string doing, string done, EnvironmentInfo environment, List<T> targets, Func<T, string> describe,
            Func<ApprovalActionService, T, UserOption, string, string> act, string resource = null)
        {
            if (settings == null)
            {
                settings = new Settings();
            }

            var journal = new SettingsJournal(this, ConnectionDetail?.WebApplicationUrl);
            var signIn = await GetTeamsApprovalsTokenAsync(environment.TenantId, caption, resource);
            if (signIn == null)
            {
                return;
            }

            WorkAsync(new WorkAsyncInfo
            {
                Message = doing + "...",
                Work = (worker, args) =>
                {
                    var actions = new ApprovalActionService(Service) { Journal = journal };
                    var signedInUser = actions.FindUser(signIn.Account.Username)
                        ?? throw new InvalidOperationException(
                            "You signed in to Teams Approvals as " + signIn.Account.Username + ", but no user in this environment " +
                            "has that sign-in name or email.");

                    var failures = new List<string>();
                    var warnings = new List<string>();
                    for (var i = 0; i < targets.Count; i++)
                    {
                        var target = targets[i];
                        worker.ReportProgress(100 * i / targets.Count, doing + " " + (i + 1) + " of " + targets.Count + ": " + describe(target));
                        try
                        {
                            var warning = act(actions, target, signedInUser, signIn.AccessToken);
                            if (warning != null)
                            {
                                warnings.Add(describe(target) + ": " + warning);
                            }
                        }
                        catch (Exception ex)
                        {
                            failures.Add(describe(target) + ": " + ex.Message);
                        }
                    }

                    args.Result = Tuple.Create(failures, warnings);
                },
                ProgressChanged = args => SetWorkingMessage(args.UserState as string),
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        ShowErrorDialog(args.Error, caption);
                        return;
                    }

                    var (failures, warnings) = (Tuple<List<string>, List<string>>)args.Result;
                    var summary = (targets.Count - failures.Count) + " of " + targets.Count + " " + done + ".";
                    if (failures.Count > 0)
                    {
                        summary += "\n\nNot " + done + ":\n" + string.Join("\n\n", failures);
                    }

                    if (warnings.Count > 0)
                    {
                        summary += "\n\nWarnings:\n" + string.Join("\n\n", warnings);
                    }

                    MessageBox.Show(summary, caption, MessageBoxButtons.OK,
                        failures.Count == 0 && warnings.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

                    ExecuteMethod(LoadApprovals);
                },
            });
        }

        /// <summary>Shows or hides the query panel (and its resize bar).</summary>
        private void tsbModifyQuery_CheckedChanged(object sender, EventArgs e)
        {
            queryEditor.Visible = tsbModifyQuery.Checked;
            splitterQuery.Visible = tsbModifyQuery.Checked;
        }

        /// <summary>
        /// Run in the query panel: clears the grid's column filters and search (the query already includes them), then
        /// reloads the approvals with the edited filter (Refresh keeps using it).
        /// </summary>
        private void QueryEditor_RunRequested(object sender, string filterXml)
        {
            searchDelay.Stop();
            tstSearch.TextChanged -= tstSearch_TextChanged;
            tstSearch.Clear();
            tstSearch.TextChanged += tstSearch_TextChanged;
            dataGridControl1.ClearFilters();

            activeFilterXml = filterXml;
            ExecuteMethod(LoadApprovals);
        }

        /// <summary>Searches as you type, after a short pause so large lists stay responsive.</summary>
        private void tstSearch_TextChanged(object sender, EventArgs e)
        {
            searchDelay.Stop();
            searchDelay.Start();
        }

        private void searchDelay_Tick(object sender, EventArgs e)
        {
            searchDelay.Stop();
            dataGridControl1.SetSearchText(tstSearch.Text);
        }

        /// <summary>Escape clears the search; Enter searches straight away.</summary>
        private void tstSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                tstSearch.Clear();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                searchDelay_Tick(sender, e);
                e.SuppressKeyPress = true;
            }
        }

        private void tslSearchIcon_Click(object sender, EventArgs e)
        {
            tstSearch.Focus();
        }

        /// <summary>Stretches the search box across the toolbar.</summary>
        private void toolStripSearch_Resize(object sender, EventArgs e)
        {
            var available = toolStripSearch.ClientSize.Width - tslSearchIcon.Width - toolStripSearch.Padding.Horizontal - 16;
            tstSearch.Width = Math.Max(150, available);
        }

        private void ApprovalAdminControl_Load(object sender, EventArgs e)
        {
            if (!SettingsManager.Instance.TryLoad(GetType(), out settings))
            {
                settings = new Settings();
                LogWarning("Settings not found => a new settings file has been created!");
            }

            if (settings.BorrowedRows == null)
            {
                settings.BorrowedRows = new List<BorrowedRow>();
            }

            // Asks for a connection first when there is none, then runs LoadApprovals.
            ExecuteMethod(LoadApprovals);
        }

        /// <summary>
        /// Loads the approvals from the connected environment into the grid. The query runs off the UI thread.
        /// </summary>
        private void LoadApprovals()
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading approvals...",
                Work = (worker, args) =>
                {
                    if (adminAccess == null)
                    {
                        try
                        {
                            adminAccess = new ApprovalActionService(Service).GetAdminAccess();
                            accessProblem = null;
                        }
                        catch (Exception ex)
                        {
                            accessProblem = ex.Message;
                        }
                    }

                    args.Result = new ApprovalService(Service).GetApprovals(activeFilterXml);
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        ShowErrorDialog(args.Error, "Error loading approvals");
                        return;
                    }

                    var result = (ApprovalLoadResult)args.Result;
                    dataGridControl1.ShowApprovals(result.Approvals);
                    queryEditor.SetQueryParts(result.FetchXmlPrefix, result.FetchXmlSuffix);
                    queryEditor.SetTableColumns(result.TableColumns);
                    UpdateActionButtons();

                    if (result.Approvals.Rows.Count == 0)
                    {
                        MessageBox.Show("No approvals match the query.", "Approvals", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }

                    if (adminAccess?.IsSystemAdministrator == true)
                    {
                        OfferLeftovers();
                    }
                }
            });
        }

        /// <summary>
        /// Reloads the approvals (asking for a connection first if there is none).
        /// </summary>
        private void tsbRefresh_Click(object sender, EventArgs e)
        {
            ExecuteMethod(LoadApprovals);
        }

        /// <summary>
        /// Swaps the grid headers between friendly names and logical names; the button shows what the next press does.
        /// </summary>
        private void tsbHeaderNames_Click(object sender, EventArgs e)
        {
            showLogicalNames = !showLogicalNames;
            dataGridControl1.UseLogicalNameHeaders(showLogicalNames);
            tsbHeaderNames.Text = showLogicalNames ? "Show friendly names" : "Show logical names";
        }

        private void buttonResetSettings_Click(object sender, EventArgs e)
        {
            settings = new Settings();
            SaveSettings();
            MessageBox.Show("Settings have been reset to default.", "Settings Reset", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// Occurs when the tool is closed: saves the settings.
        /// </summary>
        private void ApprovalAdminControl_OnCloseTool(object sender, EventArgs e)
        {
            if (settings != null)
            {
                SaveSettings();
            }
        }

        /// <summary>
        /// Occurs when the connection has been updated in XrmToolBox.
        /// </summary>
        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            base.UpdateConnection(newService, detail, actionName, parameter);
            profilePhotos = new ProfilePhotos();
            tenantId = null;
            adminAccess = null;
            accessProblem = null;
            leftoversOffered = false;

            if (settings != null && detail != null)
            {
                settings.LastUsedOrganizationWebappUrl = detail.WebApplicationUrl;
                LogInfo("Connection has changed to: {0}", detail.WebApplicationUrl);
            }
        }
    }
}

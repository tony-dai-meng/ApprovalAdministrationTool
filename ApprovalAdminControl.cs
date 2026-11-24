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

        /// <summary>The actions can be clicked when an approval that is still open is ticked or selected.</summary>
        private void UpdateActionButtons()
        {
            actionPanel1.ActionsEnabled = dataGridControl1.CheckedApprovalIds.Count > 0
                || (dataGridControl1.PreviewApprovalId is Guid current && dataGridControl1.IsOpen(current));
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
        }

        private void SaveSettings()
        {
            SettingsManager.Instance.Save(GetType(), settings);
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
                var chosen = dialog.SelectedRequests.Where(request => request.Approver.Id != newApprover.Id).ToList();
                Run("Reassign", "Reassigning", "reassigned to " + newApprover.Name, targets.Environment, chosen,
                    request => request.ApprovalTitle + " (" + request.Approver.Name + ")",
                    (actions, request, signedInUser, token) => actions.Reassign(request, newApprover, signedInUser, comments, targets.Environment, token));
            }
        }

        /// <summary>Approve or Reject: answers the chosen approvers' requests.</summary>
        private void ChooseAndRespond(ApprovalAction action, List<Guid> ids, ActionTargets targets)
        {
            var name = action.ToString(); // also the answer Teams expects
            var requests = targets.Requests.Where(request => request.Accepts(name)).ToList();
            var skipped = ids.Where(id => requests.All(request => request.ApprovalId != id))
                .Select(id => GridTitle(id) + (targets.Requests.Any(request => request.ApprovalId == id) ? " (has custom responses)" : " (not waiting on anyone)"))
                .ToList();
            if (requests.Count == 0)
            {
                MessageBox.Show("None of the chosen approvals can be answered with " + name + ":\n" + string.Join("\n", skipped),
                    name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // An approval waiting on several people: choose whose request to answer.
            var items = requests.Select(request => new RespondDialog.Item(request,
                request.ApprovalTitle + "  \u2014  " + (request.Approver.Name ?? request.Approver.Id.ToString()),
                requests.Count(other => other.ApprovalId == request.ApprovalId) == 1));
            var titles = requests.GroupBy(request => request.ApprovalId).Select(group => group.First().ApprovalTitle).ToList();
            using (var dialog = new RespondDialog(name + " " + Quantity(titles), name,
                "The response is sent from your own sign-in, in place of the approver's. It cannot be undone.", name + " for", items, skipped))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var comments = dialog.Comments;
                Run(name, action == ApprovalAction.Approve ? "Approving" : "Rejecting", action == ApprovalAction.Approve ? "approved" : "rejected",
                    targets.Environment, dialog.Selected<PendingRequest>(),
                    request => request.ApprovalTitle + " (" + request.Approver.Name + ")",
                    (actions, request, signedInUser, token) => actions.Respond(request, name, signedInUser, comments, targets.Environment, token));
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
        private async Task<AuthenticationResult> GetTeamsApprovalsTokenAsync(Guid tenantId, string caption)
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
                    return await auth.GetTokenAsync(ConnectionDetail?.UserName, ParentForm?.Handle ?? Handle);
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
                        "Entra ID would not give you a token for the Teams Approvals API (" + TeamsApprovalsAuth.ApiBaseUrl + "):\n\n" + ex.Message +
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
        private async void Run<T>(string caption, string doing, string done, EnvironmentInfo environment, List<T> targets, Func<T, string> describe,
            Func<ApprovalActionService, T, UserOption, string, string> act)
        {
            if (settings == null)
            {
                settings = new Settings();
            }

            var signIn = await GetTeamsApprovalsTokenAsync(environment.TenantId, caption);
            if (signIn == null)
            {
                return;
            }

            WorkAsync(new WorkAsyncInfo
            {
                Message = doing + "...",
                Work = (worker, args) =>
                {
                    var actions = new ApprovalActionService(Service);
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

                    if (result.Approvals.Rows.Count == 0)
                    {
                        MessageBox.Show("No approvals match the query.", "Approvals", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            SettingsManager.Instance.Save(GetType(), settings);
            MessageBox.Show("Settings have been reset to default.", "Settings Reset", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// Occurs when the tool is closed: saves the settings.
        /// </summary>
        private void ApprovalAdminControl_OnCloseTool(object sender, EventArgs e)
        {
            if (settings != null)
            {
                SettingsManager.Instance.Save(GetType(), settings);
            }
        }

        /// <summary>
        /// Occurs when the connection has been updated in XrmToolBox.
        /// </summary>
        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            base.UpdateConnection(newService, detail, actionName, parameter);

            if (settings != null && detail != null)
            {
                settings.LastUsedOrganizationWebappUrl = detail.WebApplicationUrl;
                LogInfo("Connection has changed to: {0}", detail.WebApplicationUrl);
            }
        }
    }
}

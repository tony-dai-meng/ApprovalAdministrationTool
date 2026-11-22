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

        /// <summary>Reassign can be clicked when an approval is ticked or selected.</summary>
        private void UpdateActionButtons()
        {
            actionPanel1.ReassignEnabled = dataGridControl1.CheckedApprovalIds.Count > 0 || dataGridControl1.PreviewApprovalId.HasValue;
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

        // ---------------------------------------------------------------- reassign

        /// <summary>
        /// Reassign (action panel): reassigns the ticked approvals, or the current row's if none are ticked.
        /// Finds their pending approvers, asks which to replace and with whom, gets a Teams Approvals token as the person
        /// using the tool (saved sign-in first) and reassigns each request (<see cref="ReassignService.Reassign"/>), then reloads.
        /// </summary>
        private void actionPanel1_ReassignRequested(object sender, EventArgs e)
        {
            var ids = dataGridControl1.CheckedApprovalIds.ToList();
            if (ids.Count == 0 && dataGridControl1.PreviewApprovalId is Guid current)
            {
                ids.Add(current);
            }

            if (ids.Count == 0)
            {
                MessageBox.Show("Tick the approvals to reassign (or select one).", "Reassign", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var comments = actionPanel1.Comments;
            // XrmToolBox needs a named method (not a lambda) here, so it can run it again after asking for a connection.
            ExecuteMethod(FindPendingRequests, Tuple.Create(ids, comments));
        }

        private void FindPendingRequests(Tuple<List<Guid>, string> request)
        {
            var (approvalIds, comments) = request;
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Finding approvers and people...",
                Work = (worker, args) =>
                {
                    var reassign = new ReassignService(Service);
                    args.Result = Tuple.Create(reassign.GetEnvironment(), reassign.GetPendingRequests(approvalIds), reassign.GetPeople());
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        ShowErrorDialog(args.Error, "Reassign");
                        return;
                    }

                    var (environment, requests, people) = (Tuple<EnvironmentInfo, List<PendingRequest>, List<UserOption>>)args.Result;
                    ChooseAndReassign(approvalIds, environment, requests, people, comments);
                },
            });
        }

        private async void ChooseAndReassign(List<Guid> approvalIds, EnvironmentInfo environment, List<PendingRequest> requests, List<UserOption> people, string comments)
        {
            // The approval request's lookup to its approval carries no name, so use the titles shown in the grid.
            foreach (var request in requests)
            {
                if (dataGridControl1.FindApproval(request.ApprovalId)?["msdyn_flow_approval_title"] is string title && title.Length > 0)
                {
                    request.ApprovalTitle = title;
                }
            }

            var skipped = approvalIds
                .Where(id => requests.All(request => request.ApprovalId != id))
                .Select(id => dataGridControl1.FindApproval(id)?["msdyn_flow_approval_title"] as string ?? id.ToString())
                .ToList();
            if (requests.Count == 0)
            {
                MessageBox.Show("None of the chosen approvals is waiting on anyone, so there is nothing to reassign.",
                    "Reassign", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<PendingRequest> chosen;
            UserOption newApprover;
            using (var dialog = new ReassignDialog(requests, skipped, people, comments))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                newApprover = dialog.NewApprover;
                comments = dialog.Comments;
                chosen = dialog.SelectedRequests.Where(request => request.Approver.Id != newApprover.Id).ToList();
            }

            if (chosen.Count == 0)
            {
                MessageBox.Show(newApprover.Name + " is already the approver.", "Reassign", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (settings == null)
            {
                settings = new Settings();
            }

            var signIn = await GetTeamsApprovalsTokenAsync(environment.TenantId);
            if (signIn == null)
            {
                return;
            }

            ReassignRequests(chosen, newApprover, comments, environment, signIn);
        }

        /// <summary>
        /// A delegated token for the Teams Approvals API (https://approvals.teams.microsoft.com) in the environment's tenant,
        /// as the person using the tool: the saved sign-in if it still works, otherwise the Microsoft sign-in window with
        /// their XrmToolBox connection's account pre-filled. Signs in as Microsoft's Teams client unless an app
        /// registration's client ID is saved; one is asked for (with Entra's error) only if Entra refuses the client.
        /// Null if cancelled or if Entra refuses for another reason (shown to the user).
        /// </summary>
        private async Task<AuthenticationResult> GetTeamsApprovalsTokenAsync(Guid tenantId)
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
                        ? "Entra ID would not let Microsoft's Teams sign-in get this token, so Reassign needs your own app registration:\n"
                        : "Entra ID rejected the app registration:\n") + FirstLine(ex.Message);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Entra ID would not give you a token for the Teams Approvals API (" + TeamsApprovalsAuth.ApiBaseUrl + "):\n\n" + ex.Message +
                        "\n\nThe app registration needs a delegated permission on the Approvals API (with admin consent if required). " +
                        "If the Approvals API offers none in your tenant, only Microsoft's own apps can call it.",
                        "Reassign", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private void ReassignRequests(List<PendingRequest> requests, UserOption newApprover, string comments, EnvironmentInfo environment, AuthenticationResult signIn)
        {
            var accessToken = signIn.AccessToken;
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Reassigning...",
                Work = (worker, args) =>
                {
                    var reassign = new ReassignService(Service);
                    var signedInUser = reassign.FindUser(signIn.Account.Username)
                        ?? throw new InvalidOperationException(
                            "You signed in to Teams Approvals as " + signIn.Account.Username + ", but no user in this environment " +
                            "has that sign-in name or email.");

                    var failures = new List<string>();
                    var warnings = new List<string>();
                    for (var i = 0; i < requests.Count; i++)
                    {
                        var request = requests[i];
                        worker.ReportProgress(100 * i / requests.Count, "Reassigning " + (i + 1) + " of " + requests.Count + ": " + request.ApprovalTitle);
                        try
                        {
                            var warning = reassign.Reassign(request, newApprover, signedInUser, comments, environment, accessToken);
                            if (warning != null)
                            {
                                warnings.Add(request.ApprovalTitle + ": " + warning);
                            }
                        }
                        catch (Exception ex)
                        {
                            failures.Add(request.ApprovalTitle + " (" + request.Approver.Name + "): " + ex.Message);
                        }
                    }

                    args.Result = Tuple.Create(failures, warnings);
                },
                ProgressChanged = args => SetWorkingMessage(args.UserState as string),
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        ShowErrorDialog(args.Error, "Reassign");
                        return;
                    }

                    var (failures, warnings) = (Tuple<List<string>, List<string>>)args.Result;
                    var done = requests.Count - failures.Count;
                    var summary = done + " of " + requests.Count + " request(s) reassigned to " + newApprover.Name + ".";
                    if (failures.Count > 0)
                    {
                        summary += "\n\nNot reassigned:\n" + string.Join("\n\n", failures);
                    }

                    if (warnings.Count > 0)
                    {
                        summary += "\n\nWarnings:\n" + string.Join("\n\n", warnings);
                    }

                    MessageBox.Show(summary, "Reassign", MessageBoxButtons.OK,
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

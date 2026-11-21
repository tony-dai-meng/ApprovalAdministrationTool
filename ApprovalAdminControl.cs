using System;
using System.Linq;
using System.Windows.Forms;
using ApprovalAdministrationTool.Controls;
using ApprovalAdministrationTool.Models;
using ApprovalAdministrationTool.Services;
using McTools.Xrm.Connection;
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

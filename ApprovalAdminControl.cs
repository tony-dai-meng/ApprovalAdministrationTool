using System;
using System.Data;
using System.Windows.Forms;
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

        public ApprovalAdminControl()
        {
            InitializeComponent();
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
                    args.Result = new ApprovalService(Service).GetApprovals();
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        ShowErrorDialog(args.Error, "Error loading approvals");
                        return;
                    }

                    var approvals = (DataTable)args.Result;
                    dataGridControl1.ShowApprovals(approvals);

                    if (approvals.Rows.Count == 0)
                    {
                        MessageBox.Show("No approvals found in this environment.", "Approvals", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            });
        }

        private void tsbClose_Click(object sender, EventArgs e)
        {
            CloseTool();
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

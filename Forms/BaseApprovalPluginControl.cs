using McTools.Xrm.Connection;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Deployment;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Activities.Presentation.Metadata;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using XrmToolBox.Extensibility;

namespace ApprovalAdministrationTool
{
    public partial class BaseApprovalPluginControl : PluginControlBase
    {
        private Settings mySettings; // initialise settings for the plugin
        private DataTable myApprovals; // initialise approvals DataTable
        private DataTable mySimpleApprovals; //initialise approvals for a simpile view
        public BaseApprovalPluginControl()
        {
            InitializeComponent();
        }

        private void MyPluginControl_Load(object sender, EventArgs e)
        {
            ExecuteMethod(WhoAmI);
            //ShowInfoNotification("This is a notification that can lead to XrmToolBox repository", new Uri("https://github.com/MscrmTools/XrmToolBox"));

            // Loads or creates the settings for the plugin
            if (!SettingsManager.Instance.TryLoad(GetType(), out mySettings))
            {
                //mySettings = new Settings();
                //LogWarning("Settings not found => a new settings file has been created!");
            }
            else
            {
                //LogInfo("Settings found and loaded");
            }
            ExecuteMethod(GetAccounts);
        }

        private void WhoAmI()
        {
            Service.Execute(new WhoAmIRequest());
        }

        private void tsbClose_Click(object sender, EventArgs e)
        {
            CloseTool();
        }

        private void buttonResetSettings_Click(object sender, EventArgs e)
        {
            // Reset settings to default
            mySettings = new Settings();
            SettingsManager.Instance.Save(GetType(), mySettings);
            MessageBox.Show("Settings have been reset to default.", "Settings Reset", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void GetAccounts()
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Getting approvals",
                Work = (worker, args) =>
                {
                    // Instantiate QueryExpression query
                    QueryExpression query = new QueryExpression("msdyn_flow_approval");

                    // Add columns to query.ColumnSet
                    query.ColumnSet.AddColumns(
                        "msdyn_flow_approvalid",
                        "msdyn_flow_approval_title",
                        "msdyn_flow_approval_allowcancel",
                        "msdyn_flow_approval_allowreassign",
                        "msdyn_flow_approval_modeltype",
                        "msdyn_flow_approval_itemlink",
                        "msdyn_flow_approval_itemlinkdescription",
                        "msdyn_flow_approval_requesttype",
                        "msdyn_flow_approval_priority",
                        "msdyn_flow_approval_sendemail",
                        "msdyn_flow_approval_source",
                        "msdyn_flow_approval_stage");

                    args.Result = Service.RetrieveMultiple(query);
                },
                PostWorkCallBack = (args) =>
                {
                    if (args.Error != null)
                    {
                        MessageBox.Show(args.Error.ToString(), "Error retriving approvals", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    var result = args.Result as EntityCollection;
                    if (result != null)
                    {
                        MessageBox.Show($"Found {result.Entities.Count} approvals");
                        myApprovals = GetDataTableFromEntityCollection(result);
                        if (myApprovals.Rows.Count > 0)
                        {
                            bindTable(ApprovalGridView, myApprovals);
                        }
                        else
                        {
                            MessageBox.Show("No approvals found or the DataTable is empty.");
                        }
                    }
                }
            });
        }

        private void bindTable(DataGridView dataGridView, DataTable dataTable)
        {
            if (dataTable != null && dataTable.Rows.Count > 0)
            {
                dataGridView.DataSource = dataTable;
                
                // Set the column headers to be more user-friendly
                foreach (DataGridViewColumn column in ApprovalGridView.Columns)
                {
                    switch (column.Name)
                    {
                        case "msdyn_flow_approvalid":
                            column.HeaderText = "Approval ID";
                            break;
                        case "msdyn_name":
                            column.HeaderText = "Approval Name";
                            break;
                        case "msdyn_flowname":
                            column.HeaderText = "Flow Name";
                            break;
                        case "msdyn_status":
                            column.HeaderText = "Status";
                            break;
                        default:
                            break;
                    }
                }
                
                dataGridView.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
            }
            else
            {
                MessageBox.Show("No data available to bind to the DataGridView.");
            }
        }

        /// <summary>
        /// This method converts an Entity Collection object to a DataTable object.
        /// </summary>
        /// <param name="entityCollection">The entity collection object</param>
        /// <returns></returns>
        public static DataTable GetDataTableFromEntityCollection(EntityCollection entityCollection)
        {
            // Init datatable
            DataTable dataTable = new DataTable("Entities");
            //grab first entity to get attributes
            Microsoft.Xrm.Sdk.AttributeCollection attributes = entityCollection.Entities.First().Attributes;

            // Add columns to the DataTable based on the attributes of the first entity
            if (entityCollection.Entities.Count > 0)
            {
                foreach (KeyValuePair<string, object> attribute in attributes)
                {
                    dataTable.Columns.Add(attribute.Key, attribute.Value.GetType());
                }
            }
            else
            {
                return null;
            }

            // Add the records in the datatable
            foreach (Entity record in entityCollection.Entities)
            {
                DataRow row = dataTable.NewRow();
                foreach (KeyValuePair<string, object> attribute in record.Attributes)
                {
                    row[attribute.Key] = attribute.Value;
                }
                dataTable.Rows.Add(row);
            }
            return dataTable;
        }

        /// <summary>
        /// This event occurs when the plugin is closed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MyPluginControl_OnCloseTool(object sender, EventArgs e)
        {
            // Before leaving, save the settings
            SettingsManager.Instance.Save(GetType(), mySettings);
        }

        /// <summary>
        /// This event occurs when the connection has been updated in XrmToolBox
        /// </summary>
        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            base.UpdateConnection(newService, detail, actionName, parameter);

            if (mySettings != null && detail != null)
            {
                mySettings.LastUsedOrganizationWebappUrl = detail.WebApplicationUrl;
                LogInfo("Connection has changed to: {0}", detail.WebApplicationUrl);
            }
        }

        private void toolStripMenu_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {

        }

        private void panel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void webBrowser1_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {

        }

        // This method is called when the control is resized.
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            var WindowWidth = this.Width;
            var WindowHeight = this.Height;

        }
    }
}
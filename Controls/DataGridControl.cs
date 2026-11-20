using System;
using System.Data;
using System.Windows.Forms;
using ApprovalAdministrationTool.Models;

namespace ApprovalAdministrationTool.Controls
{
    /// <summary>
    /// Shows the approvals grid. Display only: it never calls Dataverse.
    /// </summary>
    public partial class DataGridControl : UserControl
    {
        public DataGridControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows the approvals, using each column's caption as its header and hiding the ID column.
        /// </summary>
        public void ShowApprovals(DataTable approvals)
        {
            if (approvals == null)
            {
                throw new ArgumentNullException(nameof(approvals));
            }

            ApprovalGridView.DataSource = approvals;
            foreach (DataGridViewColumn column in ApprovalGridView.Columns)
            {
                column.HeaderText = approvals.Columns[column.DataPropertyName].Caption;
            }

            if (ApprovalGridView.Columns.Contains(ApprovalColumns.Id))
            {
                ApprovalGridView.Columns[ApprovalColumns.Id].Visible = false;
            }

            ApprovalGridView.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
        }
    }
}

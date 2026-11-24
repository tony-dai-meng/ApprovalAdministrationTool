using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ApprovalAdministrationTool.Services;

namespace ApprovalAdministrationTool.Controls
{
    public partial class ActionPanel : UserControl
    {
        public ActionPanel()
        {
            InitializeComponent();
            actionsControl1.ActionClicked += (sender, action) => ActionRequested?.Invoke(this, action);
        }

        /// <summary>Raised when an action's button is clicked. Its window then asks for the comment.</summary>
        public event EventHandler<ApprovalAction> ActionRequested;

        /// <summary>Whether the action buttons can be clicked.</summary>
        public bool ActionsEnabled
        {
            get => actionsControl1.ActionsEnabled;
            set => actionsControl1.ActionsEnabled = value;
        }
    }
}

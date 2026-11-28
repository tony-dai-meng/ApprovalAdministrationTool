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

        /// <summary>Whether a chosen approval has custom responses: one Action button replaces Approve and Reject.</summary>
        public bool CustomResponses
        {
            get => actionsControl1.CustomResponses;
            set => actionsControl1.CustomResponses = value;
        }

        /// <summary>Why every button is off whatever is selected (shown under the buttons), or null.</summary>
        public string DisabledReason
        {
            get => actionsControl1.DisabledReason;
            set
            {
                var hadReason = actionsControl1.DisabledReason != null;
                actionsControl1.DisabledReason = value;
                var hasReason = actionsControl1.DisabledReason != null;
                if (hasReason != hadReason)
                {
                    // Room for the reason under the buttons, so the buttons keep their size.
                    Height += (hasReason ? 1 : -1) * actionsControl1.ReasonHeight;
                }
            }
        }
    }
}

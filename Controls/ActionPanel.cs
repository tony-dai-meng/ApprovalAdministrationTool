using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ApprovalAdministrationTool.Controls
{
    public partial class ActionPanel : UserControl
    {
        public ActionPanel()
        {
            InitializeComponent();
            actionsControl1.ReassignClicked += (sender, e) => ReassignRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Raised when the Reassign button is clicked.</summary>
        public event EventHandler ReassignRequested;

        /// <summary>Whether the Reassign button can be clicked.</summary>
        public bool ReassignEnabled
        {
            get => actionsControl1.ReassignEnabled;
            set => actionsControl1.ReassignEnabled = value;
        }

        /// <summary>The comments typed in the panel (sent with the action).</summary>
        public string Comments => textBoxWithPlaceholder1.Text;
    }
}
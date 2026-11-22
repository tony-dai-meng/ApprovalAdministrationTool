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
    public partial class ActionsControl : UserControl
    {
        public ActionsControl()
        {
            InitializeComponent();
            AdjustButtonWidths();
            ReassignButton.Click += (sender, e) => ReassignClicked?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Raised when the Reassign button is clicked.</summary>
        public event EventHandler ReassignClicked;

        /// <summary>Whether the Reassign button can be clicked (off in the designer until there is something to reassign).</summary>
        public bool ReassignEnabled
        {
            get => ReassignButton.Enabled;
            set => ReassignButton.Enabled = value;
        }

        private void FlowLayoutActionH_Resize(object sender, EventArgs e)
        {
            AdjustButtonWidths();
        }

        private void AdjustButtonWidths()
        {
            int buttonCount = flowLayoutActionH.Controls.Count;
            if (buttonCount == 0) return;

            int buttonWidth = flowLayoutActionH.ClientSize.Width / buttonCount;
            foreach (Control ctrl in flowLayoutActionH.Controls)
            {
                ctrl.Width = buttonWidth;
                ctrl.Height = flowLayoutActionH.ClientSize.Height;
            }
        }

        private void ApproveButton_Click(object sender, EventArgs e)
        {

        }
    }
}

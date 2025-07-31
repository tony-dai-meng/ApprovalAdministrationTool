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

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
    public partial class ActionsControl : UserControl
    {
        private const float LargestFontSize = 12F;
        private const float SmallestFontSize = 6F;

        public ActionsControl()
        {
            InitializeComponent();
            AdjustButtonWidths();
            ApproveButton.Click += (sender, e) => ActionClicked?.Invoke(this, ApprovalAction.Approve);
            RejectButton.Click += (sender, e) => ActionClicked?.Invoke(this, ApprovalAction.Reject);
            ReassignButton.Click += (sender, e) => ActionClicked?.Invoke(this, ApprovalAction.Reassign);
            CancelButton.Click += (sender, e) => ActionClicked?.Invoke(this, ApprovalAction.Cancel);
        }

        /// <summary>Raised when one of the buttons is clicked.</summary>
        public event EventHandler<ApprovalAction> ActionClicked;

        /// <summary>Whether the buttons can be clicked (off in the designer until there is an approval to act on).</summary>
        public bool ActionsEnabled
        {
            get => ApproveButton.Enabled;
            set
            {
                foreach (Control button in flowLayoutActionH.Controls)
                {
                    button.Enabled = value;
                }
            }
        }

        private void FlowLayoutActionH_Resize(object sender, EventArgs e)
        {
            AdjustButtonWidths();
        }

        /// <summary>Shrinks the text when the panel is narrow, so that the longest label is not cut off.</summary>
        private void FitFont(int buttonWidth)
        {
            var buttons = flowLayoutActionH.Controls.Cast<Control>().ToList();
            var size = LargestFontSize;
            while (size > SmallestFontSize)
            {
                using (var font = new Font(ApproveButton.Font.FontFamily, size, FontStyle.Bold))
                {
                    if (buttons.All(button => TextRenderer.MeasureText(button.Text, font).Width <= buttonWidth))
                    {
                        break;
                    }
                }

                size--;
            }

            if (size != ApproveButton.Font.Size)
            {
                var font = new Font(ApproveButton.Font.FontFamily, size, FontStyle.Bold);
                buttons.ForEach(button => button.Font = font);
            }
        }

        private void AdjustButtonWidths()
        {
            int buttonCount = flowLayoutActionH.Controls.Count;
            if (buttonCount == 0) return;

            // Each button has a margin around it: leave room for it, or the last button wraps out of view.
            int buttonWidth = flowLayoutActionH.ClientSize.Width / buttonCount;
            FitFont(buttonWidth - ApproveButton.Margin.Horizontal);
            foreach (Control ctrl in flowLayoutActionH.Controls)
            {
                ctrl.Width = buttonWidth - ctrl.Margin.Horizontal;
                ctrl.Height = flowLayoutActionH.ClientSize.Height - ctrl.Margin.Vertical;
            }
        }
    }
}

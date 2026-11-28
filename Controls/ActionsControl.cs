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

        // Remind (a Teams chat to the approver) is added here rather than in the designer file.
        private readonly FluentButton RemindButton = new FluentButton { Name = "RemindButton", Text = "Remind", Enabled = false, TabIndex = 4 };

        // Action: shown instead of Approve and Reject when a chosen approval has custom responses (they mean nothing there).
        private readonly FluentButton RespondButton = new FluentButton { Name = "RespondButton", Text = "Action", Enabled = false, TabIndex = 0, Visible = false };
        private bool customResponses;

        // Why the buttons are off whatever is selected (e.g. not a System Administrator); hidden when there is no such reason.
        private readonly Label reasonLabel = new Label
        {
            Name = "reasonLabel",
            Dock = DockStyle.Bottom,
            Height = 30, // two lines, so it fits a narrow panel
            Visible = false,
            ForeColor = SystemColors.GrayText,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
        };

        private readonly ToolTip reasonTip = new ToolTip();

        /// <summary>The height of the line(s) under the buttons that show <see cref="DisabledReason"/>.</summary>
        public int ReasonHeight => reasonLabel.Height;
        private bool actionsEnabled;
        private string disabledReason;

        public ActionsControl()
        {
            InitializeComponent();
            flowLayoutActionH.Controls.Add(RemindButton);
            flowLayoutActionH.Controls.Add(RespondButton);
            flowLayoutActionH.Controls.SetChildIndex(RespondButton, flowLayoutActionH.Controls.GetChildIndex(ApproveButton)); // where Approve is
            Controls.Add(reasonLabel);
            ApproveButton.Tone = FluentButton.ButtonTone.Success;
            RejectButton.Tone = FluentButton.ButtonTone.Danger;
            CancelButton.Tone = FluentButton.ButtonTone.Warning; // Reassign and Remind stay brand blue
            AdjustButtonWidths();
            ApproveButton.Click += (sender, e) => ActionClicked?.Invoke(this, ApprovalAction.Approve);
            RejectButton.Click += (sender, e) => ActionClicked?.Invoke(this, ApprovalAction.Reject);
            ReassignButton.Click += (sender, e) => ActionClicked?.Invoke(this, ApprovalAction.Reassign);
            CancelButton.Click += (sender, e) => ActionClicked?.Invoke(this, ApprovalAction.Cancel);
            RemindButton.Click += (sender, e) => ActionClicked?.Invoke(this, ApprovalAction.Remind);
            RespondButton.Click += (sender, e) => ActionClicked?.Invoke(this, ApprovalAction.Respond);
        }

        /// <summary>
        /// Whether a chosen approval has custom responses: Approve and Reject are then replaced by one Action button,
        /// whose window has a dropdown per approval to pick the response.
        /// </summary>
        public bool CustomResponses
        {
            get => customResponses;
            set
            {
                if (customResponses == value)
                {
                    return;
                }

                customResponses = value;
                ApproveButton.Visible = !value;
                RejectButton.Visible = !value;
                RespondButton.Visible = value;
                AdjustButtonWidths();
            }
        }

        /// <summary>The buttons on show (Action, or Approve and Reject).</summary>
        private List<Control> ShownButtons()
        {
            return flowLayoutActionH.Controls.Cast<Control>()
                .Where(button => button == RespondButton ? customResponses : button == ApproveButton || button == RejectButton ? !customResponses : true)
                .ToList();
        }

        /// <summary>Raised when one of the buttons is clicked.</summary>
        public event EventHandler<ApprovalAction> ActionClicked;

        /// <summary>
        /// Whether the buttons can be clicked (off in the designer until there is an approval to act on). They stay off
        /// while there is a <see cref="DisabledReason"/>.
        /// </summary>
        public bool ActionsEnabled
        {
            get => actionsEnabled && disabledReason == null;
            set
            {
                actionsEnabled = value;
                ApplyEnabled();
            }
        }

        /// <summary>
        /// Why every button is off whatever is selected, shown under the buttons (a disabled button shows no tooltip), or
        /// null when the buttons follow <see cref="ActionsEnabled"/>.
        /// </summary>
        public string DisabledReason
        {
            get => disabledReason;
            set
            {
                disabledReason = string.IsNullOrWhiteSpace(value) ? null : value;
                reasonLabel.Text = disabledReason ?? string.Empty;
                reasonLabel.Visible = disabledReason != null;
                reasonTip.SetToolTip(reasonLabel, disabledReason);
                ApplyEnabled();
            }
        }

        private void ApplyEnabled()
        {
            foreach (Control button in flowLayoutActionH.Controls)
            {
                button.Enabled = ActionsEnabled;
            }
        }

        private void FlowLayoutActionH_Resize(object sender, EventArgs e)
        {
            AdjustButtonWidths();
        }

        /// <summary>Shrinks the text when the panel is narrow, so that the longest label is not cut off.</summary>
        private void FitFont(int buttonWidth)
        {
            var buttons = ShownButtons();
            var size = LargestFontSize;
            while (size > SmallestFontSize)
            {
                using (var font = new Font(ApproveButton.Font.FontFamily, size, ApproveButton.Font.Style))
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
                var font = new Font(ApproveButton.Font.FontFamily, size, ApproveButton.Font.Style);
                flowLayoutActionH.Controls.Cast<Control>().ToList().ForEach(button => button.Font = font);
            }
        }

        private void AdjustButtonWidths()
        {
            int buttonCount = ShownButtons().Count;
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

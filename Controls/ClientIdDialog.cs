using System;
using System.Drawing;
using System.Windows.Forms;
using ApprovalAdministrationTool.Services;

namespace ApprovalAdministrationTool.Controls
{
    /// <summary>
    /// Asks for the Entra ID app registration Reassign signs in with to get a token for the Teams Approvals API, with how
    /// to set it up. Shown the first time, and again (with the error) if Entra rejects it.
    /// </summary>
    internal sealed class ClientIdDialog : Form
    {
        private readonly TextBox clientIdBox = new TextBox { Width = 460 };
        private readonly Button okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Enabled = false };

        /// <param name="currentClientId">The client ID saved before, if any.</param>
        /// <param name="error">Why it did not work, or null the first time.</param>
        public ClientIdDialog(string currentClientId, string error)
        {
            Text = "Teams Approvals sign-in";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = new TableLayoutPanel { AutoSize = true, Padding = new Padding(10), ColumnCount = 1 };
            if (!string.IsNullOrEmpty(error))
            {
                layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(460, 0), ForeColor = Color.Firebrick, Text = error + "\n" });
            }

            layout.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(460, 0),
                Text = "Reassign can sign you in to the Teams Approvals API (" + TeamsApprovalsAuth.ApiBaseUrl + ") with an Entra ID app " +
                       "registration in your tenant. In the app registration:\n" +
                       "1. Authentication: add the platform \"Mobile and desktop applications\" with the redirect URI\n   " +
                       TeamsApprovalsAuth.RedirectUri + "\n   and set \"Allow public client flows\" to Yes.\n" +
                       "2. API permissions: add the Approvals API's delegated permission (grant admin consent if required).",
            });
            layout.Controls.Add(new Label { AutoSize = true, Text = "Application (client) ID:", Margin = new Padding(3, 8, 3, 0) });
            layout.Controls.Add(clientIdBox);

            var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(okButton);
            layout.Controls.Add(buttons);
            Controls.Add(layout);

            AcceptButton = okButton;
            CancelButton = cancelButton;
            clientIdBox.TextChanged += (sender, e) => okButton.Enabled = Guid.TryParse(ClientId, out _);
            clientIdBox.Text = currentClientId ?? string.Empty;
        }

        public string ClientId => clientIdBox.Text.Trim();
    }
}

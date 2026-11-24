using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ApprovalAdministrationTool.Services;

namespace ApprovalAdministrationTool.Controls
{
    /// <summary>
    /// The Reassign window: a comment box at the top, then a searchable dropdown of the environment's people to reassign
    /// to. If any chosen approval waits on several approvers, a list below lets you choose whose requests move.
    /// </summary>
    internal sealed class ReassignDialog : Form
    {
        private readonly List<UserOption> people;
        private readonly TextBox commentsBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Height = 80 };
        private readonly ComboBox peopleBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, MaxDropDownItems = 15 };
        private readonly CheckedListBox requestList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, Height = 110 };
        private readonly Label status = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(500, 0) };
        private readonly Button okButton = new Button { Text = "Reassign", DialogResult = DialogResult.OK, AutoSize = true, Enabled = false };
        private readonly IReadOnlyList<PendingRequest> requests;
        private bool filtering;

        /// <param name="requests">The pending approval requests of the chosen approvals.</param>
        /// <param name="approvalsWithoutRequests">Titles of chosen approvals with no pending approvers (skipped).</param>
        /// <param name="people">Who the approvals can be reassigned to.</param>
        public ReassignDialog(IReadOnlyList<PendingRequest> requests, IReadOnlyList<string> approvalsWithoutRequests, List<UserOption> people)
        {
            this.requests = requests;
            this.people = people;

            Text = "Reassign " + (requests.Select(r => r.ApprovalId).Distinct().Count() == 1 ? "\"" + requests[0].ApprovalTitle + "\"" : requests.Select(r => r.ApprovalId).Distinct().Count() + " approvals");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = new TableLayoutPanel { AutoSize = true, Padding = new Padding(12), ColumnCount = 1, Width = 520 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500));

            layout.Controls.Add(Heading("Comment"));
            layout.Controls.Add(commentsBox);

            layout.Controls.Add(Heading("Reassign to"));
            layout.Controls.Add(peopleBox);

            // Whose requests move: only asked when an approval waits on more than one person.
            var multiApprover = requests.GroupBy(r => r.ApprovalId).Any(g => g.Count() > 1);
            foreach (var request in requests)
            {
                requestList.Items.Add(new RequestItem(request), !multiApprover || requests.Count(r => r.ApprovalId == request.ApprovalId) == 1);
            }

            if (multiApprover)
            {
                layout.Controls.Add(Heading("Approvers to replace"));
                layout.Controls.Add(requestList);
            }

            if (approvalsWithoutRequests.Count > 0)
            {
                layout.Controls.Add(new Label
                {
                    AutoSize = true,
                    MaximumSize = new Size(500, 0),
                    ForeColor = SystemColors.GrayText,
                    Margin = new Padding(3, 8, 3, 0),
                    Text = "Skipped (not waiting on anyone): " + string.Join(", ", approvalsWithoutRequests),
                });
            }

            layout.Controls.Add(status);

            var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(okButton);
            layout.Controls.Add(buttons);
            Controls.Add(layout);

            AcceptButton = okButton;
            CancelButton = cancelButton;

            peopleBox.Items.AddRange(people.Cast<object>().ToArray());
            peopleBox.TextUpdate += (sender, e) => FilterPeople();
            peopleBox.SelectedIndexChanged += (sender, e) => { if (!filtering) UpdateOk(); };
            peopleBox.Leave += (sender, e) => PickTypedPerson();
            requestList.ItemCheck += (sender, e) => BeginInvoke(new Action(UpdateOk));
            TextBoxCueBanner.SetComboBox(peopleBox, "Type a name or email");
            Shown += (sender, e) => commentsBox.Focus();
            UpdateOk();
        }

        /// <summary>The requests to reassign: all of them, or the ticked ones when some approval has several approvers.</summary>
        public IReadOnlyList<PendingRequest> SelectedRequests => requestList.CheckedItems.Cast<RequestItem>().Select(item => item.Request).ToList();

        /// <summary>The person to reassign to.</summary>
        public UserOption NewApprover => peopleBox.SelectedItem as UserOption;

        public string Comments => commentsBox.Text;

        private static Label Heading(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold), Margin = new Padding(3, 8, 3, 3) };
        }

        /// <summary>Shows only the people whose name or email contains what was typed, keeping the typed text.</summary>
        private void FilterPeople()
        {
            var text = peopleBox.Text;
            var caret = peopleBox.SelectionStart;
            var matches = people.Where(person => person.ToString().IndexOf(text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0).Cast<object>().ToArray();

            filtering = true;
            try
            {
                // Close the list and drop the earlier pick first: the list keeps a stale selection otherwise, and
                // reading the box's text then fails once its items are replaced.
                peopleBox.DroppedDown = false;
                peopleBox.SelectedIndex = -1;
                peopleBox.BeginUpdate();
                peopleBox.Items.Clear();
                peopleBox.Items.AddRange(matches);
                peopleBox.EndUpdate();
                peopleBox.DroppedDown = matches.Length > 0 && text.Length > 0;
                peopleBox.Text = text;
                peopleBox.SelectionStart = Math.Min(caret, text.Length);
                peopleBox.SelectionLength = 0;
                Cursor.Current = Cursors.Default; // opening the list hides the mouse pointer
            }
            finally
            {
                filtering = false;
            }

            status.Text = matches.Length == 0 ? "No one in this environment matches \"" + text.Trim() + "\"." : string.Empty;
            UpdateOk();
        }

        /// <summary>When the box loses focus, picks the only match (or an exact one) for what was typed.</summary>
        private void PickTypedPerson()
        {
            if (peopleBox.SelectedItem != null || peopleBox.Items.Count == 0)
            {
                return;
            }

            var exact = peopleBox.Items.Cast<UserOption>().FirstOrDefault(person =>
                string.Equals(person.Name, peopleBox.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)
                || string.Equals(person.Email, peopleBox.Text.Trim(), StringComparison.CurrentCultureIgnoreCase));
            if (exact != null || peopleBox.Items.Count == 1)
            {
                peopleBox.SelectedItem = exact ?? peopleBox.Items[0];
            }
        }

        private void UpdateOk()
        {
            var newApprover = NewApprover;
            var selected = SelectedRequests;
            okButton.Enabled = newApprover != null && selected.Any(request => request.Approver.Id != newApprover.Id);
            if (newApprover != null && selected.Count > 0 && selected.All(request => request.Approver.Id == newApprover.Id))
            {
                status.Text = newApprover.Name + " is already the approver.";
            }
            else if (newApprover != null)
            {
                status.Text = string.Empty;
            }
        }

        private sealed class RequestItem
        {
            public RequestItem(PendingRequest request)
            {
                Request = request;
            }

            public PendingRequest Request { get; }

            public override string ToString() => Request.ApprovalTitle + "  —  " + (Request.Approver.Name ?? Request.Approver.Id.ToString());
        }
    }
}

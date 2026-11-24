using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ApprovalAdministrationTool.Controls
{
    /// <summary>
    /// The Approve, Reject and Cancel window: a comment box at the top, then what the action applies to (tick to include).
    /// The button names the action; Enter does not press it, because these actions cannot be undone.
    /// </summary>
    internal sealed class RespondDialog : Form
    {
        private readonly TextBox commentsBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Height = 80 };
        private readonly CheckedListBox list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, Height = 110 };
        private readonly Button okButton = new Button { DialogResult = DialogResult.OK, AutoSize = true };

        /// <param name="title">The window's title.</param>
        /// <param name="button">The text of the button that does it, such as "Approve".</param>
        /// <param name="explanation">What will happen, in a sentence or two.</param>
        /// <param name="listHeading">What the list shows.</param>
        /// <param name="items">What the action can apply to.</param>
        /// <param name="skipped">Chosen approvals left out, with the reason.</param>
        public RespondDialog(string title, string button, string explanation, string listHeading, IEnumerable<Item> items, IReadOnlyList<string> skipped)
        {
            Text = title;
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
            layout.Controls.Add(Heading(listHeading));
            layout.Controls.Add(list);
            layout.Controls.Add(Note(explanation));
            if (skipped.Count > 0)
            {
                layout.Controls.Add(Note("Skipped: " + string.Join("; ", skipped)));
            }

            okButton.Text = button;
            var closeButton = new Button { Text = "Close", DialogResult = DialogResult.Cancel, AutoSize = true };
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(closeButton);
            buttons.Controls.Add(okButton);
            layout.Controls.Add(buttons);
            Controls.Add(layout);
            CancelButton = closeButton;

            foreach (var item in items)
            {
                list.Items.Add(item, item.Ticked);
            }

            list.ItemCheck += (sender, e) => okButton.Enabled = list.CheckedItems.Count + (e.NewValue == CheckState.Checked ? 1 : -1) > 0;
            okButton.Enabled = list.CheckedItems.Count > 0;
            Shown += (sender, e) => commentsBox.Focus();
        }

        public string Comments => commentsBox.Text;

        /// <summary>The <see cref="Item.Value"/> of each ticked item.</summary>
        public List<T> Selected<T>() => list.CheckedItems.Cast<Item>().Select(item => (T)item.Value).ToList();

        private static Label Heading(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold), Margin = new Padding(3, 8, 3, 3) };
        }

        private static Label Note(string text)
        {
            return new Label { Text = text, AutoSize = true, MaximumSize = new Size(500, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 8, 3, 0), UseMnemonic = false };
        }

        /// <summary>Something the action can apply to.</summary>
        internal sealed class Item
        {
            public Item(object value, string text, bool ticked)
            {
                Value = value;
                Text = text;
                Ticked = ticked;
            }

            public object Value { get; }
            public string Text { get; }
            public bool Ticked { get; }

            public override string ToString() => Text;
        }
    }
}

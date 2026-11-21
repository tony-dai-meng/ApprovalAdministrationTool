using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ApprovalAdministrationTool.Controls
{
    /// <summary>
    /// The drop-down a column header's filter icon opens: a search box, a checkbox list of the column's values
    /// (with "(Select all)" and "(Blanks)"), and OK / Clear filter buttons. Ticked values are the ones kept.
    /// </summary>
    internal sealed class ColumnFilterPopup : UserControl
    {
        /// <summary>The value that stands for an empty cell.</summary>
        public const string BlankValue = "";

        private const string SelectAllText = "(Select all)";
        private const string BlanksText = "(Blanks)";

        private readonly List<string> values;
        private readonly HashSet<string> ticked;
        private readonly List<string> listed = new List<string>(); // values shown in the list, in order (after item 0, "Select all")
        private readonly TextBox search;
        private readonly CheckedListBox list;
        private readonly Button okButton;
        private bool updating;

        /// <param name="values">The column's values (<see cref="BlankValue"/> for empty cells), in display order.</param>
        /// <param name="selected">The values currently kept by the column's filter, or null when it has no filter.</param>
        public ColumnFilterPopup(IEnumerable<string> values, ICollection<string> selected)
        {
            this.values = values.ToList();
            ticked = new HashSet<string>(selected == null ? this.values : this.values.Where(selected.Contains));

            Size = new Size(260, 320);
            Padding = new Padding(6);
            BackColor = SystemColors.Window;

            search = new TextBox { Dock = DockStyle.Top };
            TextBoxCueBanner.Set(search, "Search values");
            search.TextChanged += (sender, e) => FillList();

            list = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle,
            };
            list.ItemCheck += List_ItemCheck;

            okButton = new Button { Text = "OK", AutoSize = true };
            okButton.Click += (sender, e) => Applied?.Invoke(this, ticked.Count == this.values.Count ? null : ticked.ToList());
            var clearButton = new Button { Text = "Clear filter", AutoSize = true };
            clearButton.Click += (sender, e) => Cleared?.Invoke(this, EventArgs.Empty);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(0, 6, 0, 0),
            };
            buttons.Controls.Add(okButton);
            buttons.Controls.Add(clearButton);

            var gap = new Panel { Dock = DockStyle.Top, Height = 6 };
            Controls.Add(list);
            Controls.Add(gap);
            Controls.Add(search);
            Controls.Add(buttons);

            FillList();
        }

        /// <summary>
        /// Raised when OK is pressed, with the ticked values, or null when every value is ticked (meaning no filter).
        /// </summary>
        public event EventHandler<IReadOnlyCollection<string>> Applied;

        /// <summary>Raised when Clear filter is pressed.</summary>
        public event EventHandler Cleared;

        /// <summary>Puts the cursor in the search box.</summary>
        public void FocusSearch()
        {
            search.Focus();
        }

        private void FillList()
        {
            updating = true;
            list.BeginUpdate();
            list.Items.Clear();
            listed.Clear();

            var text = search.Text.Trim();
            listed.AddRange(values.Where(value => text.Length == 0
                || (value == BlankValue ? BlanksText : value).IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0));

            list.Items.Add(SelectAllText);
            foreach (var value in listed)
            {
                list.Items.Add(value == BlankValue ? BlanksText : value, ticked.Contains(value));
            }

            list.EndUpdate();
            updating = false;
            UpdateSelectAll();
        }

        private void List_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (updating)
            {
                return;
            }

            if (e.Index == 0 && e.CurrentValue == CheckState.Indeterminate)
            {
                e.NewValue = CheckState.Checked; // like Excel: clicking a partly ticked "(Select all)" ticks everything
            }

            var isTicked = e.NewValue == CheckState.Checked;
            updating = true;
            if (e.Index == 0)
            {
                // "(Select all)" ticks or unticks every value currently listed (all of them unless searching).
                for (var i = 0; i < listed.Count; i++)
                {
                    SetTicked(listed[i], isTicked);
                    list.SetItemChecked(i + 1, isTicked);
                }
            }
            else
            {
                SetTicked(listed[e.Index - 1], isTicked);
            }

            updating = false;
            if (e.Index != 0)
            {
                BeginInvoke(new Action(UpdateSelectAll)); // after the clicked item has taken its new state
            }
            else
            {
                okButton.Enabled = ticked.Count > 0;
            }
        }

        private void SetTicked(string value, bool isTicked)
        {
            if (isTicked)
            {
                ticked.Add(value);
            }
            else
            {
                ticked.Remove(value);
            }
        }

        /// <summary>Shows "(Select all)" as ticked, unticked or partly ticked, and enables OK only when something is ticked.</summary>
        private void UpdateSelectAll()
        {
            var count = listed.Count(ticked.Contains);
            updating = true;
            list.SetItemCheckState(0, count == 0 ? CheckState.Unchecked : count == listed.Count ? CheckState.Checked : CheckState.Indeterminate);
            updating = false;
            okButton.Enabled = ticked.Count > 0;
        }
    }
}

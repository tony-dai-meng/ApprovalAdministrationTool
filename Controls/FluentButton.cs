using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ApprovalAdministrationTool.Controls
{
    /// <summary>
    /// A button drawn the Fluent UI way (Microsoft's design system, as in Fluent UI React v9): filled with white text when
    /// it can be clicked (brand blue, or a status colour: see <see cref="Tone"/>), darker on hover and press, and flat grey with muted text when disabled, so it is obvious
    /// at a glance whether an action is available. Rounded corners, and a focus ring when reached with the keyboard.
    /// </summary>
    [DesignerCategory("Code")]
    public class FluentButton : Button
    {
        /// <summary>The colour of a button that can be clicked: Fluent UI's brand blue, or a status colour.</summary>
        public enum ButtonTone
        {
            /// <summary>Brand blue: the usual action.</summary>
            Brand,

            /// <summary>Green (Fluent's success colour), for a positive action such as Approve.</summary>
            Success,

            /// <summary>Red (Fluent's danger colour), for a negative action such as Reject.</summary>
            Danger,

            /// <summary>Dark orange (Fluent's warning palette), for stopping something, such as Cancel.</summary>
            Warning,
        }

        // Fluent UI v9 web light theme tokens and palette colours: rest, hover, pressed. All keep white text readable.
        private static readonly Color[][] Tones =
        {
            new[] { ColorTranslator.FromHtml("#0F6CBD"), ColorTranslator.FromHtml("#115EA3"), ColorTranslator.FromHtml("#0C3B5E") }, // colorBrandBackground(Hover/Pressed)
            new[] { ColorTranslator.FromHtml("#107C10"), ColorTranslator.FromHtml("#0E700E"), ColorTranslator.FromHtml("#0B5A08") }, // green: colorStatusSuccessBackground3
            new[] { ColorTranslator.FromHtml("#C50F1F"), ColorTranslator.FromHtml("#B10E1C"), ColorTranslator.FromHtml("#960B18") }, // red: colorStatusDangerBackground3
            new[] { ColorTranslator.FromHtml("#BC4B09"), ColorTranslator.FromHtml("#A94408"), ColorTranslator.FromHtml("#8A3707") }, // dark orange palette
        };

        private static readonly Color OnBrand = Color.White;                                 // colorNeutralForegroundOnBrand
        private static readonly Color DisabledBack = ColorTranslator.FromHtml("#F0F0F0");   // colorNeutralBackgroundDisabled
        private static readonly Color DisabledBorder = ColorTranslator.FromHtml("#E0E0E0"); // colorNeutralStrokeDisabled
        private static readonly Color DisabledText = ColorTranslator.FromHtml("#BDBDBD");   // colorNeutralForegroundDisabled
        private static readonly Color FocusRing = ColorTranslator.FromHtml("#000000");      // colorStrokeFocus2

        private const int CornerRadius = 4; // borderRadiusMedium

        private ButtonTone tone;
        private bool hovered;
        private bool pressed;

        public FluentButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 12F, FontStyle.Regular, GraphicsUnit.Point);
        }

        /// <summary>The colour while the button can be clicked. Disabled buttons are grey whatever their tone.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ButtonTone Tone
        {
            get => tone;
            set
            {
                tone = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            hovered = false;
            pressed = false;
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? SystemColors.Control); // behind the rounded corners
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var colours = Tones[(int)tone];
            var back = !Enabled ? DisabledBack : pressed ? colours[2] : hovered ? colours[1] : colours[0];
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var shape = RoundedRectangle(bounds, CornerRadius))
            using (var fill = new SolidBrush(back))
            {
                g.FillPath(fill, shape);
                if (!Enabled)
                {
                    using (var border = new Pen(DisabledBorder))
                    {
                        g.DrawPath(border, shape);
                    }
                }
            }

            if (Focused && ShowFocusCues && Enabled)
            {
                using (var ring = new Pen(FocusRing, 2F))
                using (var shape = RoundedRectangle(new Rectangle(1, 1, Width - 3, Height - 3), CornerRadius))
                {
                    g.DrawPath(ring, shape);
                }

                using (var inner = new Pen(OnBrand, 1F))
                using (var shape = RoundedRectangle(new Rectangle(3, 3, Width - 7, Height - 7), CornerRadius - 1))
                {
                    g.DrawPath(inner, shape);
                }
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? OnBrand : DisabledText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Max(1, radius * 2);
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

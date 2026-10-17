using System.Drawing.Drawing2D;

namespace StreamDeck.WindowsAgent.Shell;

public sealed partial class SettingsWindow
{
    private sealed class RoundedButton : Button
    {
        private bool hovered, pressed;
        internal bool ShowOutline = true;
        // Accessory painting (icons/swatches) runs after the rounded background, not native Button painting.
        public new event PaintEventHandler? Paint;
        internal RoundedButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseMnemonic = false; }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Surface);
            var bounds = Rectangle.Inflate(ClientRectangle, -1, -1); if (bounds.Width < 2 || bounds.Height < 2) return;
            using var path = CreateRoundedRectPath(bounds, Math.Min(8, bounds.Height / 2));
            using var fill = new SolidBrush(Enabled ? BackColor : SurfaceAlt); g.FillPath(fill, path);
            if (Enabled && (hovered || pressed)) { using var overlay = new SolidBrush(Color.FromArgb(pressed ? 28 : 14, clientLightTheme ? Color.Black : Color.White)); g.FillPath(overlay, path); }
            if (ShowOutline || Focused) { using var border = new Pen(Focused || FlatAppearance.BorderColor == Accent ? Accent : Color.FromArgb(clientLightTheme ? 30 : 20, clientLightTheme ? Color.Black : Color.White), Focused ? 1.5f : 1); g.DrawPath(border, path); }
            var inset = Width <= 40 ? 2 : 8; var text = Rectangle.FromLTRB(bounds.Left + inset + Padding.Left, bounds.Top, bounds.Right - inset - Padding.Right, bounds.Bottom);
            var align = TextAlign is ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight ? TextFormatFlags.Right : TextAlign is ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(g, Text, Font, text, Enabled ? ForeColor : TextMuted, align | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            Paint?.Invoke(this, e);
        }
    }
    private sealed class SegmentedGroup : TableLayoutPanel
    {
        internal SegmentedGroup() { DoubleBuffered = true; Padding = new Padding(3); Margin = Padding.Empty; }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Parent?.BackColor ?? Surface); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var path = CreateRoundedRectPath(Rectangle.Inflate(ClientRectangle, -1, -1), 8); using var fill = new SolidBrush(SegmentBackground); e.Graphics.FillPath(fill, path); }
    }
    private static Color SegmentBackground => clientLightTheme ? Color.FromArgb(239, 239, 244) : Color.FromArgb(28, 28, 30);
    private sealed class RoundedSegment : RadioButton
    {
        private bool hovered;
        internal RoundedSegment() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); Appearance = Appearance.Button; FlatStyle = FlatStyle.Flat; TextAlign = ContentAlignment.MiddleCenter; UseMnemonic = false; }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics; g.Clear(SegmentBackground); g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = Rectangle.Inflate(ClientRectangle, -1, -1); if (bounds.Width < 2 || bounds.Height < 2) return;
            using var path = CreateRoundedRectPath(bounds, 6);
            if (Checked || hovered) { using var fill = new SolidBrush(Checked ? clientLightTheme ? Color.White : Color.FromArgb(62, 62, 66) : clientLightTheme ? Color.FromArgb(228, 228, 234) : Color.FromArgb(36, 36, 40)); g.FillPath(fill, path); }
            if (Focused) { using var pen = new Pen(Accent, 1.5f); g.DrawPath(pen, path); }
            TextRenderer.DrawText(g, Text, Font, bounds, Enabled ? TextPrimary : TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }
    private sealed class RoundedInput : Panel
    {
        internal readonly Control Input;
        internal RoundedInput(Control input) { Input = input; DoubleBuffered = true; Height = 32; BackColor = SurfaceAlt; AccessibleName = input.AccessibleName + " field"; input.Dock = DockStyle.None; input.Margin = Padding.Empty; input.BackColor = SurfaceAlt; input.ForeColor = TextPrimary; if (input is TextBox text) text.BorderStyle = BorderStyle.None; Controls.Add(input); input.GotFocus += (_, _) => Invalidate(); input.LostFocus += (_, _) => Invalidate(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Input.Bounds = new Rectangle(8, Math.Max(0, (Height - Input.PreferredSize.Height) / 2), Math.Max(1, Width - 16), Input.PreferredSize.Height); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Parent?.BackColor ?? Surface); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var path = CreateRoundedRectPath(Rectangle.Inflate(ClientRectangle, -1, -1), 8); using var fill = new SolidBrush(SurfaceAlt); e.Graphics.FillPath(fill, path); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var path = CreateRoundedRectPath(Rectangle.Inflate(ClientRectangle, -1, -1), 8); using var pen = new Pen(Input.Focused ? Accent : Border, Input.Focused ? 1.5f : 1); e.Graphics.DrawPath(pen, path); }
    }
    private static void WrapDesktopInputs(Control root)
    {
        if (root is RoundedInput or NumericUpDown or ComboBox or TextBox) return;
        foreach (Control input in root.Controls.Cast<Control>().ToArray()) {
            if (root is RoundedInput) continue;
            if (input is TextBox or ComboBox && input is not TextBox { Multiline: true }) {
                var dock = input.Dock; var margin = input.Margin; var bounds = input.Bounds; var index = root.Controls.GetChildIndex(input);
                var table = root as TableLayoutPanel; var position = table?.GetPositionFromControl(input); root.Controls.Remove(input);
                var wrapper = new RoundedInput(input) { Dock = dock, Margin = margin, Bounds = bounds };
                if (table is not null && position.HasValue) table.Controls.Add(wrapper, position.Value.Column, position.Value.Row); else { root.Controls.Add(wrapper); root.Controls.SetChildIndex(wrapper, index); }
            } else WrapDesktopInputs(input);
        }
    }
}

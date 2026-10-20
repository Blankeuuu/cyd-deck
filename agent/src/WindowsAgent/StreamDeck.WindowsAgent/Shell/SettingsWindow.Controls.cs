using System.Drawing.Drawing2D;

namespace StreamDeck.WindowsAgent.Shell;

public sealed partial class SettingsWindow
{
    internal sealed class RoundedButton : Button
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

    internal sealed class CircleTransportButton : Button
    {
        private readonly bool primary;
        internal string IconSymbol;
        private bool hovered;
        private bool pressed;

        internal CircleTransportButton(string iconSymbol, string accessibleName, Func<Task> action, bool primary = false)
        {
            IconSymbol = iconSymbol;
            this.primary = primary;
            AccessibleName = accessibleName;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Surface;
            Size = primary ? new Size(54, 54) : new Size(44, 44);
            Margin = new Padding(primary ? 6 : 4, 0, primary ? 6 : 4, 0);
            Cursor = Cursors.Hand;
            Click += async (_, _) =>
            {
                if (action is not null)
                {
                    try { await action(); }
                    catch (Exception ex) { AgentDiagnosticLog.Write($"Transport button {accessibleName} error: {ex.GetType().Name}"); }
                }
            };
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Surface);

            var diameter = Math.Min(Width, Height) - 6;
            if (diameter <= 0) return;
            var circle = new Rectangle((Width - diameter) / 2, (Height - diameter) / 2, diameter, diameter);

            // Fill background
            var bg = !Enabled ? SurfaceAlt : (primary ? Accent : SurfaceAlt);
            using (var fill = new SolidBrush(bg))
            {
                g.FillEllipse(fill, circle);
            }

            // Hover / Pressed overlay
            if (Enabled && (hovered || pressed))
            {
                using var overlay = new SolidBrush(Color.FromArgb(pressed ? 40 : 20, clientLightTheme ? Color.Black : Color.White));
                g.FillEllipse(overlay, circle);
            }

            // Border or Focus ring - single clean outline, no outer rectangle
            if (Focused)
            {
                using var focusPen = new Pen(Accent, 2f);
                var focusCircle = Rectangle.Inflate(circle, 2, 2);
                g.DrawEllipse(focusPen, focusCircle);
            }
            else if (!primary)
            {
                using var borderPen = new Pen(Border, 1f);
                g.DrawEllipse(borderPen, circle);
            }

            // Centered icon
            var iconSize = primary ? 24 : 20;
            var iconBox = new Rectangle((Width - iconSize) / 2, (Height - iconSize) / 2, iconSize, iconSize);
            var iconColor = !Enabled ? TextMuted : (primary ? Color.White : TextPrimary);
            ShortcutIconCatalog.Draw(g, IconSymbol, null, iconBox, iconColor);
        }
    }

    internal sealed class DarkComboBox : ComboBox
    {
        private bool hovered;

        internal DarkComboBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            BackColor = SurfaceAlt;
            ForeColor = TextPrimary;
            ItemHeight = 24;
            Font = new Font("Segoe UI", 9.5f);
            Height = 32;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            var isEdit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            if (e.Index < 0 || e.Index >= Items.Count)
            {
                using var emptyBrush = new SolidBrush(SurfaceAlt);
                e.Graphics.FillRectangle(emptyBrush, e.Bounds);
                return;
            }

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var isSelected = (e.State & DrawItemState.Selected) != 0;
            // The closed combobox header must never show Win32 blue selection highlight
            var bg = isEdit ? SurfaceAlt : (isSelected ? Accent : SurfaceAlt);
            var fg = isEdit ? (Enabled ? TextPrimary : TextMuted) : (isSelected ? Color.White : (Enabled ? TextPrimary : TextMuted));

            using (var brush = new SolidBrush(bg))
            {
                g.FillRectangle(brush, e.Bounds);
            }

            var text = GetItemText(Items[e.Index]);
            var textRect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(1, e.Bounds.Width - 16), e.Bounds.Height);
            TextRenderer.DrawText(g, text, Font, textRect, fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0014) // WM_ERASEBKGND
            {
                m.Result = (IntPtr)1;
                return;
            }

            base.WndProc(ref m);
            // WM_PAINT = 0x000F, WM_PRINT = 0x0317, WM_PRINTCLIENT = 0x0318
            if (m.Msg is 0x000F or 0x0317 or 0x0318 && IsHandleCreated)
            {
                using var g = m.Msg == 0x000F || m.WParam == IntPtr.Zero
                    ? Graphics.FromHwnd(Handle)
                    : Graphics.FromHdc(m.WParam);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Ensure background perimeter is clean to avoid native 3D border artifacts
                using (var perimeterPen = new Pen(Parent?.BackColor ?? SurfaceAlt, 1f))
                {
                    g.DrawRectangle(perimeterPen, 0, 0, Width - 1, Height - 1);
                }

                // Chevron box on the right
                var arrowWidth = 26;
                var arrowBox = new Rectangle(Width - arrowWidth, 1, arrowWidth - 1, Height - 2);
                using (var arrowBg = new SolidBrush(SurfaceAlt))
                {
                    g.FillRectangle(arrowBg, arrowBox);
                }

                // Sleek chevron arrow
                var arrowColor = Focused || hovered ? Accent : TextMuted;
                using (var pen = new Pen(arrowColor, 1.8f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    var midX = arrowBox.X + arrowBox.Width / 2 - 1;
                    var midY = arrowBox.Y + arrowBox.Height / 2 - 1;
                    g.DrawLines(pen, new[] {
                        new Point(midX - 4, midY - 2),
                        new Point(midX, midY + 2),
                        new Point(midX + 4, midY - 2)
                    });
                }

                // Smooth outer border covering all 4 edges cleanly
                using var borderPen = new Pen(Focused ? Accent : (hovered ? Color.FromArgb(140, Accent) : Border), Focused ? 1.5f : 1f);
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    internal sealed class CompactToggle : CheckBox
    {
        internal CompactToggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Size = new Size(44, 24);
            MinimumSize = new Size(44, 24);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Surface);

            var scale = DeviceDpi / 96f;
            var trackWidth = (int)Math.Round(44 * scale);
            var trackHeight = (int)Math.Round(24 * scale);
            var trackX = Math.Max(0, (Width - trackWidth) / 2);
            var trackY = Math.Max(0, (Height - trackHeight) / 2);
            var track = new Rectangle(trackX, trackY, trackWidth, trackHeight);

            using (var trackPath = CreateRoundedRectPath(track, trackHeight / 2))
            {
                using var fill = new SolidBrush(Checked ? Accent : SurfaceAlt);
                g.FillPath(fill, trackPath);

                using var borderPen = new Pen(Checked ? Accent : Border, 1f);
                g.DrawPath(borderPen, trackPath);
            }

            // Knob: vertically and horizontally centered with symmetric margin
            var diameter = trackHeight - (int)Math.Round(6 * scale);
            var margin = (trackHeight - diameter) / 2;
            var knobY = track.Top + margin;
            var knobX = Checked ? (track.Right - margin - diameter) : (track.Left + margin);
            var knobRect = new Rectangle(knobX, knobY, diameter, diameter);

            using (var knobBrush = new SolidBrush(Color.White))
            {
                g.FillEllipse(knobBrush, knobRect);
            }

            if (Focused)
            {
                using var focusPen = new Pen(Accent, 1.5f);
                using var focusPath = CreateRoundedRectPath(Rectangle.Inflate(track, 2, 2), (trackHeight + 4) / 2);
                g.DrawPath(focusPen, focusPath);
            }

            if (!string.IsNullOrWhiteSpace(Text))
            {
                var statusText = Checked ? UiText.GetOrDefault("Shell.On", "Wł.") : UiText.GetOrDefault("Shell.Off", "Wył.");
                var textRect = new Rectangle(track.Right + 8, 0, Math.Max(0, Width - track.Right - 8), Height);
                TextRenderer.DrawText(g, $"{Text} · {statusText}", Font, textRect, ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    internal sealed class DarkProgressBar : Control
    {
        private int minimum = 0;
        private int maximum = 1000;
        private int val = 0;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Minimum
        {
            get => minimum;
            set { minimum = value; Invalidate(); }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Maximum
        {
            get => maximum;
            set { maximum = Math.Max(minimum + 1, value); Invalidate(); }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => val;
            set
            {
                var clamped = Math.Clamp(value, minimum, maximum);
                if (val != clamped)
                {
                    val = clamped;
                    Invalidate();
                }
            }
        }

        internal DarkProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 6;
            AccessibleRole = AccessibleRole.ProgressBar;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Surface);

            var trackHeight = Math.Min(Height, 6);
            var trackY = (Height - trackHeight) / 2;
            var trackRect = new Rectangle(0, trackY, Width, trackHeight);
            if (trackRect.Width <= 0 || trackRect.Height <= 0) return;

            using (var trackPath = CreateRoundedRectPath(trackRect, trackHeight / 2))
            using (var trackBrush = new SolidBrush(SurfaceAlt))
            {
                g.FillPath(trackBrush, trackPath);
            }

            var range = maximum - minimum;
            var ratio = range > 0 ? (float)(val - minimum) / range : 0f;
            var fillWidth = (int)Math.Round(Width * ratio);
            if (fillWidth > 1)
            {
                var fillRect = new Rectangle(0, trackY, fillWidth, trackHeight);
                using var fillPath = CreateRoundedRectPath(fillRect, trackHeight / 2);
                using var fillBrush = new SolidBrush(Accent);
                g.FillPath(fillBrush, fillPath);
            }
        }
    }

    internal sealed class DarkSlider : Control
    {
        private int minimum = 0;
        private int maximum = 100;
        private int val = 0;
        private bool dragging;
        private bool hovered;

        public event EventHandler? ValueChanged;
        public event EventHandler? Scroll;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Minimum
        {
            get => minimum;
            set { minimum = value; if (val < minimum) Value = minimum; Invalidate(); }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Maximum
        {
            get => maximum;
            set { maximum = value; if (val > maximum) Value = maximum; Invalidate(); }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => val;
            set
            {
                var clamped = Math.Clamp(value, minimum, maximum);
                if (val != clamped)
                {
                    val = clamped;
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                    Invalidate();
                }
            }
        }

        internal DarkSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Height = 28;
            AccessibleRole = AccessibleRole.Slider;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                Focus();
                UpdateFromPoint(e.X);
                Scroll?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                UpdateFromPoint(e.X);
                Scroll?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging)
            {
                dragging = false;
                Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            var step = e.Shift ? 10 : 1;
            if (e.KeyCode is Keys.Left or Keys.Down) { Value -= step; Scroll?.Invoke(this, EventArgs.Empty); }
            else if (e.KeyCode is Keys.Right or Keys.Up) { Value += step; Scroll?.Invoke(this, EventArgs.Empty); }
            else if (e.KeyCode == Keys.Home) { Value = Minimum; Scroll?.Invoke(this, EventArgs.Empty); }
            else if (e.KeyCode == Keys.End) { Value = Maximum; Scroll?.Invoke(this, EventArgs.Empty); }
        }

        private void UpdateFromPoint(int x)
        {
            var trackLeft = 10;
            var trackRight = Width - 10;
            var trackWidth = Math.Max(1, trackRight - trackLeft);
            var ratio = Math.Clamp((float)(x - trackLeft) / trackWidth, 0f, 1f);
            Value = (int)Math.Round(minimum + ratio * (maximum - minimum));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Surface);

            var trackHeight = 6;
            var trackLeft = 10;
            var trackRight = Width - 10;
            var trackWidth = Math.Max(1, trackRight - trackLeft);
            var trackY = (Height - trackHeight) / 2;

            // Background track
            var trackRect = new Rectangle(trackLeft, trackY, trackWidth, trackHeight);
            using (var trackPath = CreateRoundedRectPath(trackRect, 3))
            using (var trackBrush = new SolidBrush(SurfaceAlt))
            {
                g.FillPath(trackBrush, trackPath);
            }

            // Fill active track with Accent
            var range = maximum - minimum;
            var ratio = range > 0 ? (float)(val - minimum) / range : 0f;
            var fillWidth = (int)Math.Round(trackWidth * ratio);
            if (fillWidth > 0)
            {
                var fillRect = new Rectangle(trackLeft, trackY, fillWidth, trackHeight);
                using var fillPath = CreateRoundedRectPath(fillRect, 3);
                using var fillBrush = new SolidBrush(Accent);
                g.FillPath(fillBrush, fillPath);
            }

            // Thumb
            var thumbSize = 16;
            var thumbX = trackLeft + fillWidth - thumbSize / 2;
            var thumbY = (Height - thumbSize) / 2;
            var thumbRect = new Rectangle(thumbX, thumbY, thumbSize, thumbSize);

            using (var thumbBrush = new SolidBrush(Color.White))
            {
                g.FillEllipse(thumbBrush, thumbRect);
            }

            var thumbBorderColor = Focused ? Accent : (hovered || dragging ? Accent : Color.FromArgb(180, 190, 205));
            using (var thumbBorder = new Pen(thumbBorderColor, Focused ? 2f : 1.5f))
            {
                g.DrawEllipse(thumbBorder, thumbRect);
            }
        }
    }

    internal sealed class DarkNumberPicker : Panel
    {
        private int val;
        private int minimum = 0;
        private int maximum = 86400;
        private readonly CheckBox neverCheck;
        private readonly Label valueLabel;
        private readonly RoundedButton decButton;
        private readonly RoundedButton incButton;
        private int lastNonZeroValue = 60;

        public event EventHandler? ValueChanged;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Minimum
        {
            get => minimum;
            set { minimum = value; if (val < minimum) Value = minimum; }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Maximum
        {
            get => maximum;
            set { maximum = value; if (val > maximum) Value = maximum; }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => val;
            set
            {
                var clamped = Math.Clamp(value, minimum, maximum);
                if (val != clamped)
                {
                    val = clamped;
                    UpdateDisplay();
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        internal DarkNumberPicker(int initialValue = 0)
        {
            val = initialValue;
            if (initialValue > 0) lastNonZeroValue = initialValue;

            DoubleBuffered = true;
            Height = 34;
            BackColor = Surface;

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));  // "Nigdy" checkbox
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));  // [-]
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // "60 s" label
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));  // [+]

            neverCheck = new CheckBox
            {
                Text = "Nigdy",
                Dock = DockStyle.Fill,
                ForeColor = TextPrimary,
                BackColor = Surface,
                Checked = initialValue == 0,
                Font = new Font("Segoe UI", 9.5f),
                Cursor = Cursors.Hand
            };

            decButton = new RoundedButton
            {
                Text = "−",
                Dock = DockStyle.Fill,
                Height = 30,
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };

            incButton = new RoundedButton
            {
                Text = "+",
                Dock = DockStyle.Fill,
                Height = 30,
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };

            valueLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = TextPrimary,
                BackColor = SurfaceAlt,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Margin = new Padding(4, 1, 4, 1)
            };

            decButton.Click += (_, _) => {
                var step = val > 60 ? 30 : 15;
                Value = Math.Max(minimum, val - step);
            };

            incButton.Click += (_, _) => {
                var step = val >= 60 ? 30 : 15;
                Value = Math.Min(maximum, val + step);
            };

            neverCheck.CheckedChanged += (_, _) => {
                if (neverCheck.Checked)
                {
                    if (val > 0) lastNonZeroValue = val;
                    Value = 0;
                }
                else
                {
                    Value = lastNonZeroValue > 0 ? lastNonZeroValue : 60;
                }
            };

            table.Controls.Add(neverCheck, 0, 0);
            table.Controls.Add(decButton, 1, 0);
            table.Controls.Add(valueLabel, 2, 0);
            table.Controls.Add(incButton, 3, 0);
            Controls.Add(table);

            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            var isNever = val == 0;
            if (neverCheck.Checked != isNever)
            {
                neverCheck.Checked = isNever;
            }

            decButton.Enabled = !isNever && val > minimum;
            incButton.Enabled = !isNever && val < maximum;

            if (isNever)
            {
                valueLabel.Text = "Nigdy (0 s)";
                valueLabel.ForeColor = TextMuted;
            }
            else
            {
                valueLabel.Text = $"{val} s";
                valueLabel.ForeColor = TextPrimary;
            }
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
        if (root is RoundedInput or DarkNumberPicker or DarkSlider or DarkComboBox or NumericUpDown or ComboBox or TextBox) return;
        foreach (Control input in root.Controls.Cast<Control>().ToArray()) {
            if (root is RoundedInput or DarkNumberPicker or DarkSlider or DarkComboBox) continue;
            if (input is TextBox && input is not TextBox { Multiline: true }) {
                var dock = input.Dock; var margin = input.Margin; var bounds = input.Bounds; var index = root.Controls.GetChildIndex(input);
                var table = root as TableLayoutPanel; var position = table?.GetPositionFromControl(input); root.Controls.Remove(input);
                var wrapper = new RoundedInput(input) { Dock = dock, Margin = margin, Bounds = bounds };
                if (table is not null && position.HasValue) table.Controls.Add(wrapper, position.Value.Column, position.Value.Row); else { root.Controls.Add(wrapper); root.Controls.SetChildIndex(wrapper, index); }
            } else WrapDesktopInputs(input);
        }
    }
}

using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace StreamDeck.WindowsAgent.Shell;

public sealed class ModernColorPickerDialog : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private static readonly Color DialogBackground = Color.FromArgb(18, 20, 25);
    private static readonly Color DialogSurface = Color.FromArgb(28, 32, 40);
    private static readonly Color DialogBorder = Color.FromArgb(48, 54, 66);
    private static readonly Color DialogTextPrimary = Color.FromArgb(244, 246, 248);
    private static readonly Color DialogTextMuted = Color.FromArgb(156, 164, 176);

    private readonly Color initialColor;
    private Color currentColor;
    private double hue;        // 0 .. 360
    private double saturation; // 0 .. 1
    private double value;      // 0 .. 1

    private bool updatingInputs;

    private readonly Panel svCanvas;
    private readonly Panel hueSlider;
    private readonly Panel previousSwatch;
    private readonly Panel newSwatch;
    private readonly TextBox hexInput;
    private readonly TextBox rInput;
    private readonly TextBox gInput;
    private readonly TextBox bInput;

    public string SelectedHex => $"#{currentColor.R:X2}{currentColor.G:X2}{currentColor.B:X2}";

    public ModernColorPickerDialog(string initialHex)
    {
        initialColor = ParseColor(initialHex, Color.FromArgb(10, 132, 255));
        currentColor = initialColor;
        ColorToHsv(currentColor, out hue, out saturation, out value);

        Text = "Wybór koloru";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 460);
        BackColor = DialogBackground;
        ForeColor = DialogTextPrimary;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(16, 16, 16, 16),
            Margin = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180)); // SV Canvas
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));  // Hue Slider
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));  // Swatches preview & Palette
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));  // HEX & RGB Inputs
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Spacer / Palette
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // Actions: Zastosuj / Anuluj

        // 1. SV Canvas
        svCanvas = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            Cursor = Cursors.Cross
        };
        svCanvas.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(svCanvas, true);
        svCanvas.Paint += PaintSvCanvas;
        bool draggingSv = false;
        svCanvas.MouseDown += (_, e) => { draggingSv = true; UpdateFromSvPoint(e.X, e.Y); };
        svCanvas.MouseMove += (_, e) => { if (draggingSv) UpdateFromSvPoint(e.X, e.Y); };
        svCanvas.MouseUp += (_, _) => draggingSv = false;
        root.Controls.Add(svCanvas, 0, 0);

        // 2. Hue Slider
        hueSlider = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 24,
            Margin = new Padding(0, 4, 0, 8),
            Cursor = Cursors.Hand
        };
        hueSlider.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(hueSlider, true);
        hueSlider.Paint += PaintHueSlider;
        bool draggingHue = false;
        hueSlider.MouseDown += (_, e) => { draggingHue = true; UpdateFromHuePoint(e.X); };
        hueSlider.MouseMove += (_, e) => { if (draggingHue) UpdateFromHuePoint(e.X); };
        hueSlider.MouseUp += (_, _) => draggingHue = false;
        root.Controls.Add(hueSlider, 0, 1);

        // 3. Preview & Preset Swatches
        var previewRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty
        };
        previewRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); // Swatches comparison
        previewRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12));  // Spacer
        previewRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // Quick swatches

        var swatchBox = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty
        };
        swatchBox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        swatchBox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        swatchBox.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        swatchBox.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var prevLabel = new Label { Text = "Poprzedni", Dock = DockStyle.Fill, ForeColor = DialogTextMuted, Font = new Font(Font.FontFamily, 7.5f), TextAlign = ContentAlignment.MiddleCenter };
        var newLabel = new Label { Text = "Nowy", Dock = DockStyle.Fill, ForeColor = DialogTextMuted, Font = new Font(Font.FontFamily, 7.5f), TextAlign = ContentAlignment.MiddleCenter };
        previousSwatch = new Panel { Dock = DockStyle.Fill, BackColor = initialColor, Margin = new Padding(2), Cursor = Cursors.Hand };
        previousSwatch.Click += (_, _) => SetColor(initialColor);
        newSwatch = new Panel { Dock = DockStyle.Fill, BackColor = currentColor, Margin = new Padding(2) };

        swatchBox.Controls.Add(prevLabel, 0, 0);
        swatchBox.Controls.Add(newLabel, 1, 0);
        swatchBox.Controls.Add(previousSwatch, 0, 1);
        swatchBox.Controls.Add(newSwatch, 1, 1);
        previewRow.Controls.Add(swatchBox, 0, 0);

        // Quick palette presets
        var palette = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 0)
        };
        var presets = new[] { "#0A84FF", "#6366F1", "#0891B2", "#059669", "#FF9F0A", "#FF453A", "#BF5AF2", "#64748B", "#101216", "#FFFFFF" };
        foreach (var hex in presets)
        {
            var btn = new Button
            {
                Size = new Size(20, 20),
                BackColor = ColorTranslator.FromHtml(hex),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(2, 0, 2, 0),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = DialogBorder;
            btn.Click += (_, _) => SetColor(ColorTranslator.FromHtml(hex));
            palette.Controls.Add(btn);
        }
        previewRow.Controls.Add(palette, 2, 0);
        root.Controls.Add(previewRow, 0, 2);

        // 4. HEX & RGB inputs
        var inputsRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 8,
            RowCount = 1,
            Margin = Padding.Empty
        };
        inputsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));  // "HEX"
        inputsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));  // hex textbox
        inputsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));  // "R"
        inputsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));// r textbox
        inputsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));  // "G"
        inputsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));// g textbox
        inputsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));  // "B"
        inputsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.4f));// b textbox

        var hexLbl = new Label { Text = "HEX", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = DialogTextMuted, Font = new Font(Font.FontFamily, 8.5f) };
        hexInput = new TextBox { Dock = DockStyle.Fill, BackColor = DialogSurface, ForeColor = DialogTextPrimary, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9f) };
        hexInput.TextChanged += (_, _) =>
        {
            if (updatingInputs) return;
            var text = hexInput.Text.Trim();
            if (!text.StartsWith('#')) text = "#" + text;
            if (text.Length == 7 && IsValidHex(text))
            {
                SetColor(ColorTranslator.FromHtml(text), updateHexInput: false);
            }
        };

        var rLbl = new Label { Text = "R", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = DialogTextMuted, Font = new Font(Font.FontFamily, 8.5f) };
        rInput = new TextBox { Dock = DockStyle.Fill, BackColor = DialogSurface, ForeColor = DialogTextPrimary, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9f), TextAlign = HorizontalAlignment.Center };
        var gLbl = new Label { Text = "G", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = DialogTextMuted, Font = new Font(Font.FontFamily, 8.5f) };
        gInput = new TextBox { Dock = DockStyle.Fill, BackColor = DialogSurface, ForeColor = DialogTextPrimary, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9f), TextAlign = HorizontalAlignment.Center };
        var bLbl = new Label { Text = "B", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = DialogTextMuted, Font = new Font(Font.FontFamily, 8.5f) };
        bInput = new TextBox { Dock = DockStyle.Fill, BackColor = DialogSurface, ForeColor = DialogTextPrimary, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9f), TextAlign = HorizontalAlignment.Center };

        void OnRgbChanged()
        {
            if (updatingInputs) return;
            if (byte.TryParse(rInput.Text, out var r) && byte.TryParse(gInput.Text, out var g) && byte.TryParse(bInput.Text, out var b))
            {
                SetColor(Color.FromArgb(r, g, b), updateRgbInputs: false);
            }
        }
        rInput.TextChanged += (_, _) => OnRgbChanged();
        gInput.TextChanged += (_, _) => OnRgbChanged();
        bInput.TextChanged += (_, _) => OnRgbChanged();

        inputsRow.Controls.Add(hexLbl, 0, 0);
        inputsRow.Controls.Add(hexInput, 1, 0);
        inputsRow.Controls.Add(rLbl, 2, 0);
        inputsRow.Controls.Add(rInput, 3, 0);
        inputsRow.Controls.Add(gLbl, 4, 0);
        inputsRow.Controls.Add(gInput, 5, 0);
        inputsRow.Controls.Add(bLbl, 6, 0);
        inputsRow.Controls.Add(bInput, 7, 0);
        root.Controls.Add(inputsRow, 0, 3);

        // 6. Action buttons
        var actionsRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 0)
        };
        actionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var cancelButton = new Button
        {
            Text = "Anuluj",
            Dock = DockStyle.Fill,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = DialogSurface,
            ForeColor = DialogTextPrimary,
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.Cancel
        };
        cancelButton.FlatAppearance.BorderColor = DialogBorder;

        var applyButton = new Button
        {
            Text = "Zastosuj",
            Dock = DockStyle.Fill,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(10, 132, 255),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.OK
        };
        applyButton.FlatAppearance.BorderSize = 0;

        actionsRow.Controls.Add(cancelButton, 0, 0);
        actionsRow.Controls.Add(applyButton, 1, 0);
        root.Controls.Add(actionsRow, 0, 5);

        Controls.Add(root);
        AcceptButton = applyButton;
        CancelButton = cancelButton;

        UpdateInputs();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int darkMode = 1;
            DwmSetWindowAttribute(Handle, 20, ref darkMode, sizeof(int));
        }
        catch { }
    }

    private void SetColor(Color newColor, bool updateHexInput = true, bool updateRgbInputs = true)
    {
        currentColor = newColor;
        ColorToHsv(currentColor, out hue, out saturation, out value);
        newSwatch.BackColor = currentColor;
        svCanvas.Invalidate();
        hueSlider.Invalidate();
        UpdateInputs(updateHexInput, updateRgbInputs);
    }

    private void UpdateInputs(bool updateHex = true, bool updateRgb = true)
    {
        updatingInputs = true;
        try
        {
            if (updateHex) hexInput.Text = $"#{currentColor.R:X2}{currentColor.G:X2}{currentColor.B:X2}";
            if (updateRgb)
            {
                rInput.Text = currentColor.R.ToString();
                gInput.Text = currentColor.G.ToString();
                bInput.Text = currentColor.B.ToString();
            }
        }
        finally
        {
            updatingInputs = false;
        }
    }

    private void UpdateFromSvPoint(int x, int y)
    {
        saturation = Math.Clamp((double)x / svCanvas.Width, 0.0, 1.0);
        value = Math.Clamp(1.0 - ((double)y / svCanvas.Height), 0.0, 1.0);
        currentColor = HsvToColor(hue, saturation, value);
        newSwatch.BackColor = currentColor;
        svCanvas.Invalidate();
        UpdateInputs();
    }

    private void UpdateFromHuePoint(int x)
    {
        hue = Math.Clamp((double)x / hueSlider.Width * 360.0, 0.0, 360.0);
        currentColor = HsvToColor(hue, saturation, value);
        newSwatch.BackColor = currentColor;
        svCanvas.Invalidate();
        hueSlider.Invalidate();
        UpdateInputs();
    }

    private void PaintSvCanvas(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        var w = svCanvas.Width;
        var h = svCanvas.Height;
        if (w <= 0 || h <= 0) return;

        // Render Hue at full brightness as base, then draw white-to-transparent horizontal and black-to-transparent vertical
        var baseColor = HsvToColor(hue, 1.0, 1.0);

        using (var brush = new SolidBrush(baseColor))
        {
            g.FillRectangle(brush, 0, 0, w, h);
        }

        // Horizontal gradient: White to Transparent
        using (var hBrush = new LinearGradientBrush(new Point(0, 0), new Point(w, 0), Color.White, Color.Transparent))
        {
            g.FillRectangle(hBrush, 0, 0, w, h);
        }

        // Vertical gradient: Transparent to Black
        using (var vBrush = new LinearGradientBrush(new Point(0, 0), new Point(0, h), Color.Transparent, Color.Black))
        {
            g.FillRectangle(vBrush, 0, 0, w, h);
        }

        // Border around canvas
        using (var borderPen = new Pen(DialogBorder, 1f))
        {
            g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
        }

        // Selector ring
        var selX = (int)Math.Round(saturation * w);
        var selY = (int)Math.Round((1.0 - value) * h);
        selX = Math.Clamp(selX, 5, w - 5);
        selY = Math.Clamp(selY, 5, h - 5);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var whiteRing = new Pen(Color.White, 2f);
        using var blackRing = new Pen(Color.Black, 1f);
        g.DrawEllipse(blackRing, selX - 5, selY - 5, 10, 10);
        g.DrawEllipse(whiteRing, selX - 4, selY - 4, 8, 8);
    }

    private void PaintHueSlider(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        var w = hueSlider.Width;
        var h = hueSlider.Height;
        if (w <= 0 || h <= 0) return;

        using (var path = new GraphicsPath())
        {
            path.AddRectangle(new RectangleF(0, 0, w, h));
            using var brush = new LinearGradientBrush(new Point(0, 0), new Point(w, 0), Color.Red, Color.Red)
            {
                InterpolationColors = new ColorBlend(7)
                {
                    Colors = new[] {
                        Color.Red,
                        Color.Yellow,
                        Color.Lime,
                        Color.Cyan,
                        Color.Blue,
                        Color.Magenta,
                        Color.Red
                    },
                    Positions = new[] { 0f, 1/6f, 2/6f, 3/6f, 4/6f, 5/6f, 1f }
                }
            };
            g.FillPath(brush, path);
        }

        // Border
        using (var borderPen = new Pen(DialogBorder, 1f))
        {
            g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
        }

        // Indicator thumb
        var thumbX = (int)Math.Round((hue / 360.0) * w);
        thumbX = Math.Clamp(thumbX, 3, w - 4);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var thumbPen = new Pen(Color.White, 2f);
        using var thumbBack = new Pen(Color.Black, 3f);
        g.DrawLine(thumbBack, thumbX, 0, thumbX, h);
        g.DrawLine(thumbPen, thumbX, 0, thumbX, h);
    }

    public static bool ShowColorPicker(IWin32Window? owner, string initialHex, out string resultHex)
    {
        using var dlg = new ModernColorPickerDialog(initialHex);
        if (dlg.ShowDialog(owner) == DialogResult.OK)
        {
            resultHex = dlg.SelectedHex;
            return true;
        }
        resultHex = initialHex;
        return false;
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return ColorTranslator.FromHtml(hex); }
        catch { return fallback; }
    }

    private static bool IsValidHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return false;
        if (hex.StartsWith('#')) hex = hex[1..];
        return hex.Length == 6 && hex.All(c => "0123456789ABCDEFabcdef".Contains(c));
    }

    private static void ColorToHsv(Color color, out double h, out double s, out double v)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        v = max;
        s = max == 0 ? 0 : delta / max;

        if (delta == 0) h = 0;
        else if (max == r) h = 60 * (((g - b) / delta) % 6);
        else if (max == g) h = 60 * (((b - r) / delta) + 2);
        else h = 60 * (((r - g) / delta) + 4);

        if (h < 0) h += 360;
    }

    private static Color HsvToColor(double h, double s, double v)
    {
        h = Math.Clamp(h, 0.0, 360.0);
        s = Math.Clamp(s, 0.0, 1.0);
        v = Math.Clamp(v, 0.0, 1.0);

        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0 % 2) - 1));
        double m = v - c;

        double r1, g1, b1;
        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        int r = (int)Math.Round((r1 + m) * 255.0);
        int g = (int)Math.Round((g1 + m) * 255.0);
        int b = (int)Math.Round((b1 + m) * 255.0);
        return Color.FromArgb(Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
    }
}

namespace StreamDeck.WindowsAgent.Shell;

internal static class UiTokens
{
    public static class Space
    {
        public const int Xs = 4, Sm = 8, Md = 12, Lg = 16, Xl = 24, Xxl = 32;
    }
    public static class Radius
    {
        public const int Control = 8, Card = 12, Dialog = 16;
    }
    public static class Type
    {
        public const float Title = 18, Heading = 12, Body = 11, Label = 10.5f, Caption = 10, Mono = 10;
        public static Font Create(float points = Body, FontStyle style = FontStyle.Regular) => new("Segoe UI", points, style, GraphicsUnit.Point);
    }
    public static Color Background => ColorTranslator.FromHtml("#0D0F14");
    public static Color Surface => ColorTranslator.FromHtml("#16181D");
    public static Color Elevated => ColorTranslator.FromHtml("#1E2028");
    public static Color Border => ColorTranslator.FromHtml("#455064");
    public static Color Text => ColorTranslator.FromHtml("#F4F6F8");
    public static Color Secondary => ColorTranslator.FromHtml("#ADB8C8");
    public static Color Accent => ColorTranslator.FromHtml("#5299E0");
    public static Color Success => ColorTranslator.FromHtml("#70DB9B");
    public static Color Warning => ColorTranslator.FromHtml("#F5C06B");
    public static Color Danger => ColorTranslator.FromHtml("#FF8A85");

    public static double Luminance(Color color)
    {
        static double Linear(byte value) { var c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
    public static double Contrast(Color first, Color second)
    {
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    public static Color OnAccent(Color accent) => Contrast(accent, Color.White) >= Contrast(accent, Color.Black) ? Color.White : Color.Black;
}

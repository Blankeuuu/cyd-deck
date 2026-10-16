using System.Globalization;
using System.Text;
using System.Text.Json;
using SkiaSharp;

if (args.Length is not (2 or 3)) throw new ArgumentException("Usage: IconFontGenerator <FontAwesome.woff> <output.c> [preview.json]");
using var face = SKTypeface.FromFile(args[0]) ?? throw new InvalidDataException("Cannot load Font Awesome.");
using var font = new SKFont(face, 20) { Edging = SKFontEdging.Antialias, Subpixel = false };
using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
int[] codes = [0xF001, 0xF005, 0xF00C, 0xF013, 0xF015, 0xF023, 0xF026, 0xF028, 0xF030,
    0xF048, 0xF04B, 0xF04C, 0xF051, 0xF054, 0xF07B, 0xF0AC, 0xF11C, 0xF120, 0xF130, 0xF15B, 0xF2D0];
var names = new Dictionary<int, string> {
 [0xF001]="audio", [0xF005]="star", [0xF00C]="check", [0xF013]="gear", [0xF015]="home", [0xF023]="lock", [0xF026]="mute", [0xF028]="volume", [0xF030]="camera", [0xF048]="prev", [0xF04B]="play", [0xF04C]="pause", [0xF051]="next", [0xF054]="right", [0xF07B]="folder", [0xF0AC]="browser", [0xF11C]="hotkey", [0xF120]="terminal", [0xF130]="mic", [0xF15B]="shortcut", [0xF2D0]="app"
};
var previews = new List<object>();
var bytes = new List<byte>();
var descriptors = new StringBuilder("    {0},\n");
var offsets = new List<string>();
foreach (var code in codes)
{
    var text = char.ConvertFromUtf32(code);
    if (!font.ContainsGlyphs(text)) throw new InvalidDataException($"Missing glyph U+{code:X}.");
    font.Size = 20;
    var advance = font.MeasureText(text, out var bounds, paint);
    while ((Math.Ceiling(bounds.Right) - Math.Floor(bounds.Left) > 24 || Math.Ceiling(bounds.Bottom) - Math.Floor(bounds.Top) > 24) && font.Size > 12)
    {
        font.Size -= 1;
        advance = font.MeasureText(text, out bounds, paint);
    }
    var left = (int)Math.Floor(bounds.Left);
    var top = (int)Math.Floor(bounds.Top);
    var width = (int)Math.Ceiling(bounds.Right) - left;
    var height = (int)Math.Ceiling(bounds.Bottom) - top;
    if (width > 24 || height > 24) throw new InvalidDataException($"Glyph U+{code:X} exceeds tile icon box.");
    using var bitmap = new SKBitmap(Math.Max(1, width), Math.Max(1, height), SKColorType.Rgba8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent);
    canvas.DrawText(text, -left, -top, SKTextAlign.Left, font, paint);
    descriptors.AppendLine(FormattableString.Invariant($"    {{.bitmap_index={bytes.Count}, .adv_w={(int)Math.Round(advance * 16)}, .box_w={width}, .box_h={height}, .ofs_x={left}, .ofs_y={-(top + height)}}}, /* U+{code:X} */"));
    for (var pixel = 0; pixel < width * height; pixel += 2)
    {
        int Alpha(int index) => index < width * height ? (bitmap.GetPixel(index % width, index / width).Alpha * 15 + 127) / 255 : 0;
        bytes.Add((byte)((Alpha(pixel) << 4) | Alpha(pixel + 1)));
    }
    var alpha = new byte[576];
    for (var pixel = 0; pixel < width * height; ++pixel) {
        var x = (24 - width) / 2 + pixel % width; var y = 20 + top + pixel / width;
        if (x >= 0 && x < 24 && y >= 0 && y < 24) alpha[y * 24 + x] = (byte)(((bitmap.GetPixel(pixel % width, pixel / width).Alpha * 15 + 127) / 255) * 17);
    }
    previews.Add(new { Key = names[code], Alpha = Convert.ToBase64String(alpha) });
    offsets.Add((code - codes[0]).ToString(CultureInfo.InvariantCulture));
}
var output = new StringBuilder("/* Generated from LVGL's FontAwesome5-Solid+Brands+Regular.woff.\n * Regenerate with scripts/icon-font-generator; Font Awesome Free license in\n * firmware/src/ui/fonts/FONT-AWESOME-LICENSE.txt.\n * Up to 20px, grayscale 4bpp, uncompressed. */\n#include \"lvgl.h\"\n\nstatic const uint8_t bitmap[] = {\n");
for (var index = 0; index < bytes.Count; index += 16)
    output.AppendLine("    " + string.Join(",", bytes.Skip(index).Take(16).Select(value => $"0x{value:X2}")) + ",");
output.AppendLine("};\nstatic const lv_font_fmt_txt_glyph_dsc_t glyphs[] = {\n" + descriptors + "};");
output.AppendLine("static const uint16_t unicode_list[] = {" + string.Join(",", offsets) + "};");
output.AppendLine($"static const lv_font_fmt_txt_cmap_t cmaps[] = {{{{.range_start={codes[0]}, .range_length={codes[^1] - codes[0] + 1}, .glyph_id_start=1, .unicode_list=unicode_list, .glyph_id_ofs_list=NULL, .list_length={codes.Length}, .type=LV_FONT_FMT_TXT_CMAP_SPARSE_TINY}}}};");
output.AppendLine("static const lv_font_fmt_txt_dsc_t descriptor = {.glyph_bitmap=bitmap, .glyph_dsc=glyphs, .cmaps=cmaps, .kern_dsc=NULL, .kern_scale=0, .cmap_num=1, .bpp=4, .kern_classes=0, .bitmap_format=0};");
output.AppendLine("const lv_font_t cyd_icons_20 = {.get_glyph_dsc=lv_font_get_glyph_dsc_fmt_txt, .get_glyph_bitmap=lv_font_get_bitmap_fmt_txt, .line_height=24, .base_line=4, .subpx=LV_FONT_SUBPX_NONE, .underline_position=-1, .underline_thickness=1, .dsc=&descriptor, .fallback=NULL, .user_data=NULL};");
File.WriteAllText(args[1], output.ToString(), new UTF8Encoding(false));
if (args.Length == 3) File.WriteAllText(args[2], JsonSerializer.Serialize(previews), new UTF8Encoding(false));
Console.WriteLine($"Generated {codes.Length} glyphs, {bytes.Count} bitmap bytes.");

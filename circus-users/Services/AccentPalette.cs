using Windows.UI;

namespace Circus.Users.App.Services;

/// <summary>
/// Набор оттенков акцентного цвета — тот же состав, что Windows держит в
/// SystemAccentColorLight1..3 / SystemAccentColorDark1..3.
/// Оттенки считаются сдвигом светлоты в HSL; это приближение к алгоритму Windows,
/// визуально совпадающее с системной палитрой.
/// </summary>
internal readonly record struct AccentPalette(
    Color Base,
    Color Light1,
    Color Light2,
    Color Light3,
    Color Dark1,
    Color Dark2,
    Color Dark3)
{
    private const double Step = 0.09;

    public static AccentPalette From(Color accent)
    {
        var (h, s, l) = ToHsl(accent);

        return new AccentPalette(
            Base: accent,
            Light1: FromHsl(h, s, l + Step),
            Light2: FromHsl(h, s, l + Step * 2),
            Light3: FromHsl(h, s, l + Step * 3),
            Dark1: FromHsl(h, s, l - Step),
            Dark2: FromHsl(h, s, l - Step * 2),
            Dark3: FromHsl(h, s, l - Step * 3));
    }

    private static (double H, double S, double L) ToHsl(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        var l = (max + min) / 2.0;

        if (delta == 0)
        {
            return (0, 0, l);
        }

        var s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

        double h;
        if (max == r)
        {
            h = (g - b) / delta + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            h = (b - r) / delta + 2;
        }
        else
        {
            h = (r - g) / delta + 4;
        }

        return (h / 6.0, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        l = Math.Clamp(l, 0.0, 1.0);

        if (s == 0)
        {
            var gray = (byte)Math.Round(l * 255);
            return Color.FromArgb(255, gray, gray, gray);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;

        return Color.FromArgb(
            255,
            ToByte(HueToChannel(p, q, h + 1.0 / 3.0)),
            ToByte(HueToChannel(p, q, h)),
            ToByte(HueToChannel(p, q, h - 1.0 / 3.0)));
    }

    private static double HueToChannel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;

        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;

        return p;
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}

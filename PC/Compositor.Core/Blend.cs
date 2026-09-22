namespace Compositor;

public enum BlendMode
{
    Normal,
    Darken,
    Multiply,
    ColorBurn,
    LinearBurn,
    Lighten,
    Screen,
    ColorDodge,
    LinearDodge,
    Overlay,
    SoftLight,
    HardLight,
    VividLight,
    LinearLight,
    PinLight,
    HardMix,
    Difference,
    Exclusion,
    Subtract,
    Divide,
    Hue,
    Saturation,
    Color,
    Luminosity
}

public static class BlendModes
{
    public static BlendMode Parse(string? name) => name switch
    {
        "Darken" => BlendMode.Darken,
        "Multiply" => BlendMode.Multiply,
        "Color Burn" => BlendMode.ColorBurn,
        "Linear Burn" => BlendMode.LinearBurn,
        "Lighten" => BlendMode.Lighten,
        "Screen" => BlendMode.Screen,
        "Color Dodge" => BlendMode.ColorDodge,
        "Linear Dodge (Add)" => BlendMode.LinearDodge,
        "Overlay" => BlendMode.Overlay,
        "Soft Light" => BlendMode.SoftLight,
        "Hard Light" => BlendMode.HardLight,
        "Vivid Light" => BlendMode.VividLight,
        "Linear Light" => BlendMode.LinearLight,
        "Pin Light" => BlendMode.PinLight,
        "Hard Mix" => BlendMode.HardMix,
        "Difference" => BlendMode.Difference,
        "Exclusion" => BlendMode.Exclusion,
        "Subtract" => BlendMode.Subtract,
        "Divide" => BlendMode.Divide,
        "Hue" => BlendMode.Hue,
        "Saturation" => BlendMode.Saturation,
        "Color" => BlendMode.Color,
        "Luminosity" => BlendMode.Luminosity,
        _ => BlendMode.Normal
    };

    public static string Name(BlendMode mode) => mode switch
    {
        BlendMode.Darken => "Darken",
        BlendMode.Multiply => "Multiply",
        BlendMode.ColorBurn => "Color Burn",
        BlendMode.LinearBurn => "Linear Burn",
        BlendMode.Lighten => "Lighten",
        BlendMode.Screen => "Screen",
        BlendMode.ColorDodge => "Color Dodge",
        BlendMode.LinearDodge => "Linear Dodge (Add)",
        BlendMode.Overlay => "Overlay",
        BlendMode.SoftLight => "Soft Light",
        BlendMode.HardLight => "Hard Light",
        BlendMode.VividLight => "Vivid Light",
        BlendMode.LinearLight => "Linear Light",
        BlendMode.PinLight => "Pin Light",
        BlendMode.HardMix => "Hard Mix",
        BlendMode.Difference => "Difference",
        BlendMode.Exclusion => "Exclusion",
        BlendMode.Subtract => "Subtract",
        BlendMode.Divide => "Divide",
        BlendMode.Hue => "Hue",
        BlendMode.Saturation => "Saturation",
        BlendMode.Color => "Color",
        BlendMode.Luminosity => "Luminosity",
        _ => "Normal"
    };

    public static readonly BlendMode[] All =
    [
        BlendMode.Normal,
        BlendMode.Darken, BlendMode.Multiply, BlendMode.ColorBurn, BlendMode.LinearBurn,
        BlendMode.Lighten, BlendMode.Screen, BlendMode.ColorDodge, BlendMode.LinearDodge,
        BlendMode.Overlay, BlendMode.SoftLight, BlendMode.HardLight, BlendMode.VividLight, BlendMode.LinearLight, BlendMode.PinLight, BlendMode.HardMix,
        BlendMode.Difference, BlendMode.Exclusion, BlendMode.Subtract, BlendMode.Divide,
        BlendMode.Hue, BlendMode.Saturation, BlendMode.Color, BlendMode.Luminosity
    ];

    /// <summary>Porter-Duff source-over of a blended source color. Opacity is 0–1.</summary>
    public static void Composite(BlendMode mode, byte sr, byte sg, byte sb, byte sa, float opacity,
        ref byte dr, ref byte dg, ref byte db, ref byte da)
    {
        float srcA = sa / 255f * Math.Clamp(opacity, 0f, 1f);
        if (srcA <= 0f) return;
        float dstA = da / 255f;
        float outA = srcA + dstA * (1f - srcA);
        float sR = sr / 255f, sG = sg / 255f, sB = sb / 255f;
        float dR = dr / 255f, dG = dg / 255f, dB = db / 255f;
        float bR, bG, bB;
        if (dstA <= 0f || mode == BlendMode.Normal)
        {
            bR = sR; bG = sG; bB = sB;
        }
        else
        {
            BlendColor(mode, sR, sG, sB, dR, dG, dB, out bR, out bG, out bB);
        }
        float inv = 1f - srcA;
        dr = Channel((bR * srcA + dR * dstA * inv) / outA);
        dg = Channel((bG * srcA + dG * dstA * inv) / outA);
        db = Channel((bB * srcA + dB * dstA * inv) / outA);
        da = Channel(outA);
    }

    public static void BlendColor(BlendMode mode, float sR, float sG, float sB, float dR, float dG, float dB,
        out float r, out float g, out float b)
    {
        switch (mode)
        {
            case BlendMode.Hue:
                HslBlend(sR, sG, sB, dR, dG, dB, takeHue: true, takeSat: false, out r, out g, out b);
                return;
            case BlendMode.Saturation:
                HslBlend(sR, sG, sB, dR, dG, dB, takeHue: false, takeSat: true, out r, out g, out b);
                return;
            case BlendMode.Color:
                HslBlend(sR, sG, sB, dR, dG, dB, takeHue: true, takeSat: true, out r, out g, out b);
                return;
            case BlendMode.Luminosity:
                r = SetLuminosity(dR, dG, dB, Luminosity(sR, sG, sB), out g, out b);
                return;
        }
        r = ChannelF(BlendChannel(mode, sR, dR));
        g = ChannelF(BlendChannel(mode, sG, dG));
        b = ChannelF(BlendChannel(mode, sB, dB));
    }

    static float BlendChannel(BlendMode mode, float src, float dst) => mode switch
    {
        BlendMode.Multiply => src * dst,
        BlendMode.Screen => 1f - (1f - src) * (1f - dst),
        BlendMode.Overlay => dst < 0.5f ? 2f * src * dst : 1f - 2f * (1f - src) * (1f - dst),
        BlendMode.Darken => Math.Min(src, dst),
        BlendMode.Lighten => Math.Max(src, dst),
        BlendMode.Difference => Math.Abs(src - dst),
        BlendMode.Exclusion => src + dst - 2f * src * dst,
        BlendMode.ColorDodge => Dodge(src, dst),
        BlendMode.ColorBurn => Burn(src, dst),
        BlendMode.LinearBurn => Math.Max(0f, src + dst - 1f),
        BlendMode.LinearDodge => Math.Min(1f, src + dst),
        BlendMode.SoftLight => Soft(src, dst),
        BlendMode.HardLight => src < 0.5f ? 2f * src * dst : 1f - 2f * (1f - src) * (1f - dst),
        BlendMode.VividLight => src < 0.5f ? Burn(2f * src, dst) : Dodge(2f * src - 1f, dst),
        BlendMode.LinearLight => Math.Clamp(dst + 2f * src - 1f, 0f, 1f),
        BlendMode.PinLight => src < 0.5f ? Math.Min(dst, 2f * src) : Math.Max(dst, 2f * src - 1f),
        BlendMode.HardMix => Vivid(src, dst) < 0.5f ? 0f : 1f,
        BlendMode.Subtract => Math.Max(0f, dst - src),
        BlendMode.Divide => src == 0f ? 1f : Math.Min(1f, dst / src),
        _ => src
    };

    static float Vivid(float src, float dst) => src < 0.5f ? Burn(2f * src, dst) : Dodge(2f * src - 1f, dst);

    static float Dodge(float src, float dst)
    {
        if (dst <= 0f) return 0f;
        if (src >= 1f) return 1f;
        return Math.Min(1f, dst / (1f - src));
    }

    static float Burn(float src, float dst)
    {
        if (dst >= 1f) return 1f;
        if (src <= 0f) return 0f;
        return 1f - Math.Min(1f, (1f - dst) / src);
    }

    static float Soft(float src, float dst)
    {
        if (src <= 0.5f) return dst - (1f - 2f * src) * dst * (1f - dst);
        float d = dst <= 0.25f ? ((16f * dst - 12f) * dst + 4f) * dst : MathF.Sqrt(dst);
        return dst + (2f * src - 1f) * (d - dst);
    }

    static void HslBlend(float sR, float sG, float sB, float dR, float dG, float dB, bool takeHue, bool takeSat,
        out float r, out float g, out float b)
    {
        RgbToHsl(sR, sG, sB, out float sH, out float sS, out float sL);
        RgbToHsl(dR, dG, dB, out float dH, out float dS, out float dL);
        HslToRgb(takeHue ? sH : dH, takeSat ? sS : dS, dL, out r, out g, out b);
        _ = sL;
    }

    static float SetLuminosity(float r0, float g0, float b0, float lum, out float g, out float b)
    {
        float delta = lum - Luminosity(r0, g0, b0);
        float r = r0 + delta;
        g = g0 + delta;
        b = b0 + delta;
        ClampLuminosity(ref r, ref g, ref b, lum);
        return r;
    }

    static void ClampLuminosity(ref float r, ref float g, ref float b, float lum)
    {
        float min = Math.Min(r, Math.Min(g, b));
        float max = Math.Max(r, Math.Max(g, b));
        if (min < 0f)
        {
            r = lum + (r - lum) * lum / (lum - min);
            g = lum + (g - lum) * lum / (lum - min);
            b = lum + (b - lum) * lum / (lum - min);
        }
        if (max > 1f)
        {
            r = lum + (r - lum) * (1f - lum) / (max - lum);
            g = lum + (g - lum) * (1f - lum) / (max - lum);
            b = lum + (b - lum) * (1f - lum) / (max - lum);
        }
    }

    public static float Luminosity(float r, float g, float b) => 0.3f * r + 0.59f * g + 0.11f * b;

    public static void RgbToHsl(float r, float g, float b, out float h, out float s, out float l)
    {
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2f;
        if (Math.Abs(max - min) < 0.00001f) { h = 0; s = 0; return; }
        float d = max - min;
        s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
        if (max == r) h = (g - b) / d + (g < b ? 6f : 0f);
        else if (max == g) h = (b - r) / d + 2f;
        else h = (r - g) / d + 4f;
        h /= 6f;
    }

    public static void HslToRgb(float h, float s, float l, out float r, out float g, out float b)
    {
        if (s <= 0f) { r = g = b = l; return; }
        float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
        float p = 2f * l - q;
        r = Hue(p, q, h + 1f / 3f);
        g = Hue(p, q, h);
        b = Hue(p, q, h - 1f / 3f);
    }

    static float Hue(float p, float q, float t)
    {
        if (t < 0f) t += 1f;
        if (t > 1f) t -= 1f;
        if (t < 1f / 6f) return p + (q - p) * 6f * t;
        if (t < 0.5f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
        return p;
    }

    static byte Channel(float value) => (byte)Math.Clamp((int)Math.Round(value * 255f), 0, 255);
    static float ChannelF(float value) => Math.Clamp(value, 0f, 1f);
}

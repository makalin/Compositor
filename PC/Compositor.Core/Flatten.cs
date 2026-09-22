namespace Compositor;

public static class CompositorEngine
{
    public static PixelBuffer Flatten(CanvasDocument document)
    {
        var output = new PixelBuffer(document.Width, document.Height);
        var byId = document.ById();
        foreach (var layer in document.Layers)
        {
            if (layer.IsGroup || layer.Pixels == null || !document.EffectivelyVisible(layer)) continue;
            DrawLayer(output, layer, document.EffectiveOpacity(layer), byId);
        }
        return output;
    }

    public static PixelBuffer CompositeOver(PixelBuffer source, Rgba backdrop)
    {
        var output = new PixelBuffer(source.Width, source.Height);
        output.Clear(backdrop);
        for (int i = 0; i < source.Rgba.Length; i += 4)
        {
            byte r = backdrop.R, g = backdrop.G, b = backdrop.B, a = backdrop.A;
            BlendModes.Composite(BlendMode.Normal, source.Rgba[i], source.Rgba[i + 1], source.Rgba[i + 2], source.Rgba[i + 3], 1f, ref r, ref g, ref b, ref a);
            output.Rgba[i] = r;
            output.Rgba[i + 1] = g;
            output.Rgba[i + 2] = b;
            output.Rgba[i + 3] = a;
        }
        return output;
    }

    static void DrawLayer(PixelBuffer output, Layer layer, float opacity, Dictionary<Guid, Layer> byId)
    {
        var pixels = layer.Pixels!;
        var bounds = Bounds(layer.Transform, output.Width, output.Height);
        if (bounds.Width <= 0) return;
        var colorOverlay = ColorOverlay(layer);
        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            for (int x = bounds.X; x < bounds.Right; x++)
            {
                if (!TrySample(layer.Transform, pixels, x + 0.5, y + 0.5, out byte r, out byte g, out byte b, out byte a)) continue;
                a = ApplyMask(layer, x + 0.5, y + 0.5, a);
                a = ApplyClipping(layer, byId, x + 0.5, y + 0.5, a);
                if (a == 0) continue;
                if (colorOverlay is { } overlay)
                {
                    float cover = overlay.Opacity;
                    r = (byte)Math.Clamp((int)Math.Round(r * (1 - cover) + overlay.R * 255 * cover), 0, 255);
                    g = (byte)Math.Clamp((int)Math.Round(g * (1 - cover) + overlay.G * 255 * cover), 0, 255);
                    b = (byte)Math.Clamp((int)Math.Round(b * (1 - cover) + overlay.B * 255 * cover), 0, 255);
                }
                int o = output.Offset(x, y);
                byte dr = output.Rgba[o], dg = output.Rgba[o + 1], db = output.Rgba[o + 2], da = output.Rgba[o + 3];
                BlendModes.Composite(layer.BlendMode, r, g, b, a, opacity, ref dr, ref dg, ref db, ref da);
                output.Rgba[o] = dr;
                output.Rgba[o + 1] = dg;
                output.Rgba[o + 2] = db;
                output.Rgba[o + 3] = da;
            }
        }
    }

    static byte ApplyMask(Layer layer, double docX, double docY, byte alpha)
    {
        if (layer.Mask == null || !layer.MaskEnabled || alpha == 0) return alpha;
        var placement = layer.MaskLinked || layer.MaskPlacement == null ? layer.Transform : layer.MaskPlacement.Value;
        if (!placement.TryUnit(docX, docY, out double u, out double v)) return 0;
        double sx = u * layer.Mask.Width - 0.5;
        double sy = v * layer.Mask.Height - 0.5;
        if (!layer.Mask.Sample(sx, sy, out byte r, out _, out _, out byte maskA)) return 0;
        int coverage = maskA == 0 && r == 0 ? 0 : r;
        if (maskA > 0 && r == maskA) coverage = r;
        return (byte)(alpha * coverage / 255);
    }

    static byte ApplyClipping(Layer layer, Dictionary<Guid, Layer> byId, double docX, double docY, byte alpha)
    {
        Guid? sourceId = layer.MaskSourceId;
        int depth = 0;
        while (sourceId is Guid id && depth++ < 256 && alpha > 0)
        {
            if (!byId.TryGetValue(id, out var source) || source.IsGroup || source.Pixels == null) return 0;
            if (!TrySample(source.Transform, source.Pixels, docX, docY, out _, out _, out _, out byte sourceA)) return 0;
            sourceA = ApplyMask(source, docX, docY, sourceA);
            float cover = sourceA / 255f * Math.Clamp((float)source.Opacity, 0f, 1f);
            alpha = (byte)Math.Clamp((int)Math.Round(alpha * cover), 0, 255);
            sourceId = source.MaskSourceId;
        }
        return alpha;
    }

    static bool TrySample(LayerTransform transform, PixelBuffer pixels, double docX, double docY, out byte r, out byte g, out byte b, out byte a)
    {
        r = g = b = a = 0;
        if (!transform.TryUnit(docX, docY, out double u, out double v)) return false;
        double sx = u * pixels.Width - 0.5;
        double sy = v * pixels.Height - 0.5;
        if (transform.Sampling == Sampling.Nearest)
        {
            int ix = Math.Clamp((int)Math.Round(sx), 0, pixels.Width - 1);
            int iy = Math.Clamp((int)Math.Round(sy), 0, pixels.Height - 1);
            var color = pixels.Get(ix, iy);
            r = color.R; g = color.G; b = color.B; a = color.A;
            return true;
        }
        return pixels.Sample(sx, sy, out r, out g, out b, out a);
    }

    public readonly record struct Overlay(float R, float G, float B, float Opacity);

    public static Overlay? ColorOverlay(Layer layer)
    {
        if (layer.Extras?["effects"] is not System.Text.Json.Nodes.JsonObject effects
            || effects["colorOverlay"] is not System.Text.Json.Nodes.JsonObject overlay)
            return null;
        if (overlay["enabled"] is System.Text.Json.Nodes.JsonValue enabled && enabled.TryGetValue<bool>(out bool on) && !on)
            return null;
        float red = ReadFloat(overlay, "red");
        float green = ReadFloat(overlay, "green");
        float blue = ReadFloat(overlay, "blue");
        float opacity = overlay["opacity"] == null ? 1f : ReadFloat(overlay, "opacity");
        return new Overlay(red, green, blue, Math.Clamp(opacity, 0f, 1f));
    }

    static float ReadFloat(System.Text.Json.Nodes.JsonObject obj, string key) =>
        obj[key] is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<double>(out double number) ? (float)number : 0f;

    public static (int X, int Y, int Width, int Height, int Right, int Bottom) Bounds(LayerTransform transform, int docWidth, int docHeight)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var (x, y) in transform.Corners())
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
        int left = Math.Clamp((int)Math.Floor(minX), 0, docWidth);
        int top = Math.Clamp((int)Math.Floor(minY), 0, docHeight);
        int right = Math.Clamp((int)Math.Ceiling(maxX), 0, docWidth);
        int bottom = Math.Clamp((int)Math.Ceiling(maxY), 0, docHeight);
        return (left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), right, bottom);
    }
}

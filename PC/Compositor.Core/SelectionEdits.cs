namespace Compositor;

public static class SelectionEdits
{
    public static SelectionMask All(int width, int height)
    {
        var mask = new SelectionMask(width, height);
        Array.Fill(mask.Values, (byte)255);
        return mask;
    }

    public static void Invert(SelectionMask mask)
    {
        for (int i = 0; i < mask.Values.Length; i++)
            mask.Values[i] = (byte)(255 - mask.Values[i]);
        mask.Touch();
    }

    public static SelectionMask FromLayerAlpha(Layer layer, CanvasDocument document)
    {
        var mask = new SelectionMask(document.Width, document.Height);
        if (layer.Pixels == null) return mask;
        var bounds = CompositorEngine.Bounds(layer.Transform, document.Width, document.Height);
        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            for (int x = bounds.X; x < bounds.Right; x++)
            {
                if (!layer.Transform.TryUnit(x + 0.5, y + 0.5, out double u, out double v)) continue;
                int sx = Math.Clamp((int)(u * layer.Pixels.Width), 0, layer.Pixels.Width - 1);
                int sy = Math.Clamp((int)(v * layer.Pixels.Height), 0, layer.Pixels.Height - 1);
                byte alpha = layer.Pixels.Get(sx, sy).A;
                if (alpha > 8) mask.Values[y * mask.Width + x] = alpha;
            }
        }
        mask.Touch();
        return mask;
    }

    public static void Rectangle(SelectionMask mask, int x0, int y0, int x1, int y1, SelectionCombine combine, bool ellipse, SelectionMask? previous)
    {
        Prepare(mask, combine, previous);
        int left = Math.Clamp(Math.Min(x0, x1), 0, mask.Width - 1);
        int right = Math.Clamp(Math.Max(x0, x1), 0, mask.Width - 1);
        int top = Math.Clamp(Math.Min(y0, y1), 0, mask.Height - 1);
        int bottom = Math.Clamp(Math.Max(y0, y1), 0, mask.Height - 1);
        double cx = (Math.Min(x0, x1) + Math.Max(x0, x1)) / 2.0;
        double cy = (Math.Min(y0, y1) + Math.Max(y0, y1)) / 2.0;
        double rx = Math.Abs(x1 - x0) / 2.0;
        double ry = Math.Abs(y1 - y0) / 2.0;
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                if (ellipse)
                {
                    if (rx < 0.5 || ry < 0.5) continue;
                    double dx = (x + 0.5 - cx) / rx;
                    double dy = (y + 0.5 - cy) / ry;
                    if (dx * dx + dy * dy > 1) continue;
                }
                Write(mask, x, y, combine);
            }
        }
        mask.Touch();
    }

    public static void Polygon(SelectionMask mask, IReadOnlyList<(double X, double Y)> points, SelectionCombine combine, SelectionMask? previous)
    {
        if (points.Count < 3) return;
        Prepare(mask, combine, previous);
        double minY = points.Min(point => point.Y);
        double maxY = points.Max(point => point.Y);
        int top = Math.Clamp((int)Math.Floor(minY), 0, mask.Height - 1);
        int bottom = Math.Clamp((int)Math.Ceiling(maxY), 0, mask.Height - 1);
        for (int y = top; y <= bottom; y++)
        {
            var hits = new List<double>();
            double scan = y + 0.5;
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                if (Math.Abs(a.Y - b.Y) < 0.0001) continue;
                if (scan < Math.Min(a.Y, b.Y) || scan >= Math.Max(a.Y, b.Y)) continue;
                double t = (scan - a.Y) / (b.Y - a.Y);
                hits.Add(a.X + (b.X - a.X) * t);
            }
            hits.Sort();
            for (int i = 0; i + 1 < hits.Count; i += 2)
            {
                int left = Math.Clamp((int)Math.Ceiling(hits[i] - 0.5), 0, mask.Width - 1);
                int right = Math.Clamp((int)Math.Floor(hits[i + 1] - 0.5), 0, mask.Width - 1);
                for (int x = left; x <= right; x++) Write(mask, x, y, combine);
            }
        }
        mask.Touch();
    }

    public static SelectionMask MagicWand(PixelBuffer image, int x, int y, int tolerance, bool contiguous, SelectionCombine combine, SelectionMask? previous)
    {
        var mask = previous?.Clone() ?? new SelectionMask(image.Width, image.Height);
        if (!image.Contains(x, y)) return mask;
        if (combine == SelectionCombine.Replace) Array.Clear(mask.Values, 0, mask.Values.Length);
        var seed = image.Get(x, y);
        if (!contiguous)
        {
            for (int i = 0; i < image.Width * image.Height; i++)
            {
                int px = i % image.Width;
                int py = i / image.Width;
                if (Close(seed, image.Get(px, py), tolerance)) Write(mask, px, py, combine);
            }
            return mask;
        }
        var seen = new bool[image.Width * image.Height];
        var stack = new Stack<int>();
        stack.Push(y * image.Width + x);
        while (stack.Count > 0)
        {
            int index = stack.Pop();
            if (seen[index]) continue;
            seen[index] = true;
            int px = index % image.Width;
            int py = index / image.Width;
            if (!Close(seed, image.Get(px, py), tolerance)) continue;
            Write(mask, px, py, combine);
            if (px > 0) stack.Push(index - 1);
            if (px + 1 < image.Width) stack.Push(index + 1);
            if (py > 0) stack.Push(index - image.Width);
            if (py + 1 < image.Height) stack.Push(index + image.Width);
        }
        return mask;
    }

    public static SelectionMask Translate(SelectionMask source, int dx, int dy)
    {
        var moved = new SelectionMask(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                byte value = source.Values[y * source.Width + x];
                if (value == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (moved.Contains(nx, ny)) moved.Values[ny * moved.Width + nx] = value;
            }
        }
        return moved;
    }

    public static void Expand(SelectionMask mask, int radius, bool contract)
    {
        radius = Math.Clamp(radius, 1, 64);
        var source = (byte[])mask.Values.Clone();
        for (int y = 0; y < mask.Height; y++)
        {
            for (int x = 0; x < mask.Width; x++)
            {
                byte best = contract ? (byte)255 : (byte)0;
                for (int oy = -radius; oy <= radius; oy++)
                {
                    for (int ox = -radius; ox <= radius; ox++)
                    {
                        if (ox * ox + oy * oy > radius * radius) continue;
                        int sx = x + ox, sy = y + oy;
                        byte value = mask.Contains(sx, sy) ? source[sy * mask.Width + sx] : (byte)0;
                        best = contract ? Math.Min(best, value) : Math.Max(best, value);
                    }
                }
                mask.Values[y * mask.Width + x] = best;
            }
        }
        mask.Touch();
    }

    public static void Feather(SelectionMask mask, float radius)
    {
        if (radius < 0.5f) return;
        var buffer = new PixelBuffer(mask.Width, mask.Height);
        for (int i = 0; i < mask.Values.Length; i++)
        {
            byte value = mask.Values[i];
            buffer.Rgba[i * 4] = value;
            buffer.Rgba[i * 4 + 1] = value;
            buffer.Rgba[i * 4 + 2] = value;
            buffer.Rgba[i * 4 + 3] = 255;
        }
        var layer = new Layer { Pixels = buffer, Transform = new LayerTransform { Width = mask.Width, Height = mask.Height } };
        var document = new CanvasDocument { Width = mask.Width, Height = mask.Height };
        PixelEdits.GaussianBlur(layer, document, null, radius);
        for (int i = 0; i < mask.Values.Length; i++)
            mask.Values[i] = layer.Pixels!.Rgba[i * 4];
        mask.Touch();
    }

    public static PixelBuffer Copy(PixelBuffer source, SelectionMask? selection)
    {
        if (selection == null || selection.IsEmpty()) return source.Clone();
        var bounds = selection.Bounds() ?? (0, 0, source.Width, source.Height);
        var copy = new PixelBuffer(bounds.Width, bounds.Height);
        for (int y = 0; y < bounds.Height; y++)
        {
            for (int x = 0; x < bounds.Width; x++)
            {
                int sx = bounds.X + x, sy = bounds.Y + y;
                if (!source.Contains(sx, sy)) continue;
                float cover = selection.Coverage(sx, sy) / 255f;
                if (cover <= 0) continue;
                var color = source.Get(sx, sy);
                copy.Set(x, y, color with { A = (byte)Math.Round(color.A * cover) });
            }
        }
        return copy;
    }

    static bool Close(Rgba seed, Rgba color, int tolerance)
    {
        return Math.Abs(seed.R - color.R) <= tolerance
            && Math.Abs(seed.G - color.G) <= tolerance
            && Math.Abs(seed.B - color.B) <= tolerance
            && Math.Abs(seed.A - color.A) <= tolerance;
    }

    static void Prepare(SelectionMask mask, SelectionCombine combine, SelectionMask? previous)
    {
        if (combine == SelectionCombine.Replace || previous == null) Array.Clear(mask.Values, 0, mask.Values.Length);
        else System.Buffer.BlockCopy(previous.Values, 0, mask.Values, 0, mask.Values.Length);
    }

    static void Write(SelectionMask mask, int x, int y, SelectionCombine combine)
    {
        int index = y * mask.Width + x;
        if (combine == SelectionCombine.Subtract) mask.Values[index] = 0;
        else mask.Values[index] = 255;
    }
}

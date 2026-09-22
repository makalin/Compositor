namespace Compositor;

public static class PixelEdits
{
    public static void PaintDab(Layer layer, CanvasDocument document, SelectionMask? selection, double docX, double docY,
        int radius, float hardness, float opacity, Rgba color, bool erase)
    {
        if (layer.Pixels == null || radius < 1) return;
        layer.ForgetVectorMetadata();
        var pixels = layer.Pixels;
        float scale = (float)(layer.Transform.Width / pixels.Width + layer.Transform.Height / pixels.Height) / 2f;
        int reach = Math.Max(1, (int)Math.Ceiling(radius / Math.Max(0.01f, scale)) + 2);
        if (!layer.Transform.TryUnit(docX, docY, out double unitX, out double unitY)) return;
        int cx = (int)(unitX * pixels.Width);
        int cy = (int)(unitY * pixels.Height);
        hardness = Math.Clamp(hardness, 0f, 1f);
        for (int y = Math.Max(0, cy - reach); y <= Math.Min(pixels.Height - 1, cy + reach); y++)
        {
            for (int x = Math.Max(0, cx - reach); x <= Math.Min(pixels.Width - 1, cx + reach); x++)
            {
                var (px, py) = layer.Transform.ToDocument((x + 0.5) / pixels.Width, (y + 0.5) / pixels.Height);
                float dist = (float)Math.Sqrt((px - docX) * (px - docX) + (py - docY) * (py - docY));
                if (dist > radius) continue;
                float edge = hardness >= 0.999f ? 1f : (1f - dist / radius) / (1f - hardness);
                if (hardness >= 0.999f) edge = dist <= radius ? 1f : 0f;
                else edge = dist / radius <= hardness ? 1f : (1f - dist / radius) / Math.Max(0.0001f, 1f - hardness);
                float stamp = Math.Clamp(edge, 0f, 1f) * opacity;
                if (stamp <= 0f) continue;
                stamp *= SelectionCoverage(selection, px, py);
                if (stamp <= 0f) continue;
                var existing = pixels.Get(x, y);
                if (erase)
                {
                    byte alpha = (byte)Math.Clamp((int)Math.Round(existing.A * (1f - stamp)), 0, 255);
                    pixels.Set(x, y, existing with { A = alpha });
                }
                else
                {
                    byte r = existing.R, g = existing.G, b = existing.B, a = existing.A;
                    BlendModes.Composite(BlendMode.Normal, color.R, color.G, color.B, color.A, stamp, ref r, ref g, ref b, ref a);
                    pixels.Set(x, y, new Rgba(r, g, b, a));
                }
            }
        }
    }

    public static void CloneDab(Layer layer, CanvasDocument document, SelectionMask? selection, PixelBuffer source,
        double docX, double docY, double srcX, double srcY, int radius, float hardness, float opacity)
    {
        if (layer.Pixels == null) return;
        layer.ForgetVectorMetadata();
        var pixels = layer.Pixels;
        for (int y = (int)Math.Floor(docY - radius); y <= (int)Math.Ceiling(docY + radius); y++)
        {
            for (int x = (int)Math.Floor(docX - radius); x <= (int)Math.Ceiling(docX + radius); x++)
            {
                float dist = (float)Math.Sqrt((x + 0.5 - docX) * (x + 0.5 - docX) + (y + 0.5 - docY) * (y + 0.5 - docY));
                float stamp = Falloff(dist, radius, hardness) * opacity * SelectionCoverage(selection, x + 0.5, y + 0.5);
                if (stamp <= 0f) continue;
                double sampleX = srcX + (x + 0.5 - docX);
                double sampleY = srcY + (y + 0.5 - docY);
                if (!TryLayerPixel(layer, sampleX, sampleY, out int sx, out int sy) || !source.Contains(sx, sy)) continue;
                if (!TryLayerPixel(layer, x + 0.5, y + 0.5, out int dx, out int dy)) continue;
                var from = source.Get(sx, sy);
                var existing = pixels.Get(dx, dy);
                byte r = existing.R, g = existing.G, b = existing.B, a = existing.A;
                BlendModes.Composite(BlendMode.Normal, from.R, from.G, from.B, from.A, stamp, ref r, ref g, ref b, ref a);
                pixels.Set(dx, dy, new Rgba(r, g, b, a));
            }
        }
    }

    public static void HealDab(Layer layer, CanvasDocument document, SelectionMask? selection, PixelBuffer source,
        double docX, double docY, int radius, float hardness)
    {
        if (layer.Pixels == null) return;
        layer.ForgetVectorMetadata();
        int outer = Math.Max(radius + 2, (int)(radius * 1.6));
        long rSum = 0, gSum = 0, bSum = 0, count = 0;
        for (int y = (int)docY - outer; y <= (int)docY + outer; y++)
        {
            for (int x = (int)docX - outer; x <= (int)docX + outer; x++)
            {
                float dist = (float)Math.Sqrt((x - docX) * (x - docX) + (y - docY) * (y - docY));
                if (dist < radius || dist > outer) continue;
                if (!TryLayerPixel(layer, x + 0.5, y + 0.5, out int sx, out int sy) || !source.Contains(sx, sy)) continue;
                var color = source.Get(sx, sy);
                if (color.A < 8) continue;
                rSum += color.R; gSum += color.G; bSum += color.B; count++;
            }
        }
        if (count == 0) return;
        var fill = new Rgba((byte)(rSum / count), (byte)(gSum / count), (byte)(bSum / count), 255);
        PaintDab(layer, document, selection, docX, docY, radius, hardness, 1f, fill, erase: false);
    }

    public static void BlurDab(Layer layer, double docX, double docY, int radius, float strength, SelectionMask? selection)
    {
        if (layer.Pixels == null || radius < 1) return;
        layer.ForgetVectorMetadata();
        var pixels = layer.Pixels;
        var original = pixels.Clone();
        int reach = radius;
        for (int y = (int)Math.Floor(docY - radius); y <= (int)Math.Ceiling(docY + radius); y++)
        {
            for (int x = (int)Math.Floor(docX - radius); x <= (int)Math.Ceiling(docX + radius); x++)
            {
                float stamp = Falloff((float)Math.Sqrt((x + 0.5 - docX) * (x + 0.5 - docX) + (y + 0.5 - docY) * (y + 0.5 - docY)), radius, 0.2f);
                stamp *= strength * SelectionCoverage(selection, x + 0.5, y + 0.5);
                if (stamp <= 0.01f || !TryLayerPixel(layer, x + 0.5, y + 0.5, out int dx, out int dy)) continue;
                int r = 0, g = 0, b = 0, a = 0, n = 0;
                for (int oy = -reach; oy <= reach; oy++)
                    for (int ox = -reach; ox <= reach; ox++)
                    {
                        if (ox * ox + oy * oy > reach * reach) continue;
                        if (!original.Contains(dx + ox, dy + oy)) continue;
                        var c = original.Get(dx + ox, dy + oy);
                        r += c.R; g += c.G; b += c.B; a += c.A; n++;
                    }
                if (n == 0) continue;
                var blurred = new Rgba((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
                var existing = pixels.Get(dx, dy);
                pixels.Set(dx, dy, Lerp(existing, blurred, stamp));
            }
        }
    }

    public static void ApplyLevels(Layer layer, CanvasDocument document, SelectionMask? selection,
        float inBlack, float inWhite, float gamma, float outBlack, float outWhite)
    {
        MapLayer(layer, document, selection, color =>
        {
            float gma = Math.Clamp(gamma, 0.1f, 10f);
            return color with
            {
                R = Level(color.R, inBlack, inWhite, gma, outBlack, outWhite),
                G = Level(color.G, inBlack, inWhite, gma, outBlack, outWhite),
                B = Level(color.B, inBlack, inWhite, gma, outBlack, outWhite)
            };
        });
    }

    public static void ApplyHueSaturation(Layer layer, CanvasDocument document, SelectionMask? selection, float hueShift, float saturation, float lightness)
    {
        MapLayer(layer, document, selection, color =>
        {
            BlendModes.RgbToHsl(color.R / 255f, color.G / 255f, color.B / 255f, out float h, out float s, out float l);
            h = (h + hueShift / 360f) % 1f;
            if (h < 0) h += 1f;
            s = Math.Clamp(s + saturation, 0f, 1f);
            l = Math.Clamp(l + lightness, 0f, 1f);
            BlendModes.HslToRgb(h, s, l, out float r, out float g, out float b);
            return new Rgba((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255), color.A);
        });
    }

    public static void ApplyExposure(Layer layer, CanvasDocument document, SelectionMask? selection, float ev)
    {
        float gain = MathF.Pow(2f, ev);
        MapLayer(layer, document, selection, color => new Rgba(Scale(color.R, gain), Scale(color.G, gain), Scale(color.B, gain), color.A));
    }

    public static void ApplyBlackAndWhite(Layer layer, CanvasDocument document, SelectionMask? selection)
    {
        MapLayer(layer, document, selection, color =>
        {
            byte y = (byte)Math.Clamp((int)Math.Round(0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B), 0, 255);
            return new Rgba(y, y, y, color.A);
        });
    }

    public static void Invert(Layer layer, CanvasDocument document, SelectionMask? selection)
    {
        MapLayer(layer, document, selection, color => new Rgba((byte)(255 - color.R), (byte)(255 - color.G), (byte)(255 - color.B), color.A));
    }

    public static void Fill(Layer layer, CanvasDocument document, SelectionMask? selection, Rgba color)
    {
        MapLayer(layer, document, selection, existing =>
        {
            if (selection == null)
            {
                byte r = existing.R, g = existing.G, b = existing.B, a = existing.A;
                BlendModes.Composite(BlendMode.Normal, color.R, color.G, color.B, color.A, 1f, ref r, ref g, ref b, ref a);
                return new Rgba(r, g, b, a);
            }
            return color;
        }, requireSelectionCoverage: selection != null);
    }

    public static void Clear(Layer layer, CanvasDocument document, SelectionMask selection)
    {
        MapLayer(layer, document, selection, color => color with { A = 0 }, requireSelectionCoverage: true);
    }

    public static void GaussianBlur(Layer layer, CanvasDocument document, SelectionMask? selection, float radius)
    {
        if (layer.Pixels == null || radius < 0.5f) return;
        layer.ForgetVectorMetadata();
        var blurred = BlurBuffer(layer.Pixels, radius);
        MixSelection(layer, document, selection, blurred);
    }

    public static void MotionBlur(Layer layer, CanvasDocument document, SelectionMask? selection, double angleDegrees, int distance)
    {
        if (layer.Pixels == null || distance < 1) return;
        layer.ForgetVectorMetadata();
        var source = layer.Pixels;
        var blurred = source.Clone();
        double radians = angleDegrees * Math.PI / 180;
        double stepX = Math.Cos(radians);
        double stepY = Math.Sin(radians);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                int r = 0, g = 0, b = 0, a = 0, n = 0;
                for (int i = -distance; i <= distance; i++)
                {
                    int sx = Math.Clamp((int)Math.Round(x + stepX * i), 0, source.Width - 1);
                    int sy = Math.Clamp((int)Math.Round(y + stepY * i), 0, source.Height - 1);
                    var c = source.Get(sx, sy);
                    r += c.R; g += c.G; b += c.B; a += c.A; n++;
                }
                blurred.Set(x, y, new Rgba((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n)));
            }
        }
        MixSelection(layer, document, selection, blurred);
    }

    public static void AddNoise(Layer layer, CanvasDocument document, SelectionMask? selection, int amount, int seed)
    {
        if (amount <= 0) return;
        var random = new Random(seed);
        int span = Math.Clamp(amount, 0, 255);
        MapLayer(layer, document, selection, color =>
        {
            if (color.A == 0) return color;
            return new Rgba(Jitter(color.R), Jitter(color.G), Jitter(color.B), color.A);
        });

        byte Jitter(byte channel)
        {
            int delta = random.Next(-span, span + 1);
            return (byte)Math.Clamp(channel + delta, 0, 255);
        }
    }

    public static PixelBuffer CreateShape(int width, int height, ShapeKind kind, Rgba color, int lineWidth = 4)
    {
        var buffer = new PixelBuffer(Math.Max(1, width), Math.Max(1, height));
        double cx = (buffer.Width - 1) / 2.0;
        double cy = (buffer.Height - 1) / 2.0;
        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                bool inside = kind switch
                {
                    ShapeKind.Ellipse => Ellipse(x, y, buffer.Width, buffer.Height),
                    ShapeKind.Line => Math.Abs(y - cy) <= lineWidth / 2.0,
                    _ => true
                };
                if (inside) buffer.Set(x, y, color);
            }
        }
        return buffer;
    }

    public static void ApplyLinearGradient(Layer layer, CanvasDocument document, SelectionMask? selection,
        double x0, double y0, double x1, double y1, Rgba start, Rgba end)
    {
        double dx = x1 - x0, dy = y1 - y0;
        double length = dx * dx + dy * dy;
        if (length < 0.001 || layer.Pixels == null) return;
        layer.ForgetVectorMetadata();
        MapLayer(layer, document, selection, (color, x, y) =>
        {
            var (docX, docY) = layer.Transform.ToDocument((x + 0.5) / layer.Pixels!.Width, (y + 0.5) / layer.Pixels.Height);
            float t = Math.Clamp((float)(((docX - x0) * dx + (docY - y0) * dy) / length), 0f, 1f);
            var graded = Lerp(start, end, t);
            byte r = color.R, g = color.G, b = color.B, a = color.A;
            BlendModes.Composite(BlendMode.Normal, graded.R, graded.G, graded.B, graded.A, 1f, ref r, ref g, ref b, ref a);
            return new Rgba(r, g, b, a);
        });
    }

    public static void MergeDown(CanvasDocument document, Guid upperId)
    {
        int index = document.Layers.FindIndex(layer => layer.Id == upperId);
        if (index <= 0) return;
        var upper = document.Layers[index];
        Layer? lower = null;
        for (int i = index - 1; i >= 0; i--)
        {
            if (document.Layers[i].ParentId == upper.ParentId && !document.Layers[i].IsGroup && document.Layers[i].Pixels != null)
            {
                lower = document.Layers[i];
                break;
            }
        }
        if (lower?.Pixels == null || upper.Pixels == null || upper.IsGroup) return;
        upper.ForgetVectorMetadata();
        lower.ForgetVectorMetadata();
        var pixels = lower.Pixels;
        float opacity = (float)Math.Clamp(upper.Opacity, 0, 1);
        for (int y = 0; y < pixels.Height; y++)
        {
            for (int x = 0; x < pixels.Width; x++)
            {
                var (docX, docY) = lower.Transform.ToDocument((x + 0.5) / pixels.Width, (y + 0.5) / pixels.Height);
                if (!upper.Transform.TryUnit(docX, docY, out double u, out double v)) continue;
                double sx = u * upper.Pixels.Width - 0.5;
                double sy = v * upper.Pixels.Height - 0.5;
                if (!upper.Pixels.Sample(sx, sy, out byte r, out byte g, out byte b, out byte a) || a == 0) continue;
                var existing = pixels.Get(x, y);
                byte dr = existing.R, dg = existing.G, db = existing.B, da = existing.A;
                BlendModes.Composite(upper.BlendMode, r, g, b, a, opacity, ref dr, ref dg, ref db, ref da);
                pixels.Set(x, y, new Rgba(dr, dg, db, da));
            }
        }
        document.Layers.RemoveAt(index);
        document.ActiveLayerId = lower.Id;
    }

    public static void Crop(CanvasDocument document, int x, int y, int width, int height)
    {
        if (!CanvasDocument.ValidDimension(width) || !CanvasDocument.ValidDimension(height))
            throw new ArgumentOutOfRangeException(nameof(width));
        foreach (var layer in document.Layers)
        {
            var transform = layer.Transform;
            transform.OriginX -= x;
            transform.OriginY -= y;
            layer.Transform = transform;
            if (layer.MaskPlacement is { } mask)
            {
                mask.OriginX -= x;
                mask.OriginY -= y;
                layer.MaskPlacement = mask;
            }
        }
        foreach (var guide in document.Guides)
            guide.Position -= guide.Horizontal ? y : x;
        document.Width = width;
        document.Height = height;
    }

    public static SelectionMask CropSelection(SelectionMask selection, int x, int y, int width, int height)
    {
        var cropped = new SelectionMask(width, height);
        for (int row = 0; row < height; row++)
            for (int col = 0; col < width; col++)
                cropped.Values[row * width + col] = selection.Coverage(col + x, row + y);
        return cropped;
    }

    public static void CanvasResize(CanvasDocument document, int width, int height, int anchor)
    {
        int shiftX = anchor % 3 == 0 ? 0 : anchor % 3 == 2 ? width - document.Width : (width - document.Width) / 2;
        int shiftY = anchor / 3 == 0 ? 0 : anchor / 3 == 2 ? height - document.Height : (height - document.Height) / 2;
        foreach (var layer in document.Layers)
        {
            var transform = layer.Transform;
            transform.OriginX += shiftX;
            transform.OriginY += shiftY;
            layer.Transform = transform;
        }
        foreach (var guide in document.Guides)
            guide.Position += guide.Horizontal ? shiftY : shiftX;
        document.Width = width;
        document.Height = height;
    }

    public static void ImageResize(CanvasDocument document, int width, int height)
    {
        double sx = width / (double)document.Width;
        double sy = height / (double)document.Height;
        foreach (var layer in document.Layers)
        {
            if (layer.Pixels != null)
                layer.Pixels = ImageFiles.Resize(layer.Pixels, Math.Max(1, (int)Math.Round(layer.Pixels.Width * sx)), Math.Max(1, (int)Math.Round(layer.Pixels.Height * sy)));
            if (layer.Mask != null)
                layer.Mask = ImageFiles.Resize(layer.Mask, Math.Max(1, (int)Math.Round(layer.Mask.Width * sx)), Math.Max(1, (int)Math.Round(layer.Mask.Height * sy)));
            var transform = layer.Transform;
            transform.OriginX *= sx;
            transform.OriginY *= sy;
            transform.Width *= sx;
            transform.Height *= sy;
            layer.Transform = transform;
        }
        foreach (var guide in document.Guides)
            guide.Position *= guide.Horizontal ? sy : sx;
        document.Width = width;
        document.Height = height;
    }

    public static (int X, int Y, int Width, int Height)? ContentBounds(PixelBuffer image)
    {
        int minX = image.Width, minY = image.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (image.Get(x, y).A > 0)
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0) return null;
        return (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    public static void FlipCanvas(CanvasDocument document, bool horizontal)
    {
        foreach (var layer in document.Layers)
            FlipLayerPlacement(document, layer, horizontal);
        foreach (var guide in document.Guides)
        {
            if (guide.Horizontal != horizontal)
                guide.Position = (horizontal ? document.Width : document.Height) - guide.Position;
        }
    }

    public static void FlipLayer(Layer layer)
    {
        var transform = layer.Transform;
        transform.FlipX = !transform.FlipX;
        layer.Transform = transform;
    }

    public static void FlipLayerVertical(Layer layer)
    {
        var transform = layer.Transform;
        transform.FlipY = !transform.FlipY;
        layer.Transform = transform;
    }

    static void FlipLayerPlacement(CanvasDocument document, Layer layer, bool horizontal)
    {
        var transform = layer.Transform;
        if (horizontal)
        {
            double center = transform.OriginX + transform.Width / 2;
            transform.OriginX = document.Width - center - transform.Width / 2;
            transform.FlipX = !transform.FlipX;
            transform.Rotation = -transform.Rotation;
        }
        else
        {
            double center = transform.OriginY + transform.Height / 2;
            transform.OriginY = document.Height - center - transform.Height / 2;
            transform.FlipY = !transform.FlipY;
            transform.Rotation = -transform.Rotation;
        }
        layer.Transform = transform;
    }

    static void MapLayer(Layer layer, CanvasDocument document, SelectionMask? selection, Func<Rgba, Rgba> map, bool requireSelectionCoverage = false)
    {
        MapLayer(layer, document, selection, (color, _, _) => map(color), requireSelectionCoverage);
    }

    static void MapLayer(Layer layer, CanvasDocument document, SelectionMask? selection, Func<Rgba, int, int, Rgba> map, bool requireSelectionCoverage = false)
    {
        if (layer.Pixels == null) return;
        layer.ForgetVectorMetadata();
        var pixels = layer.Pixels;
        for (int y = 0; y < pixels.Height; y++)
        {
            for (int x = 0; x < pixels.Width; x++)
            {
                var (docX, docY) = layer.Transform.ToDocument((x + 0.5) / pixels.Width, (y + 0.5) / pixels.Height);
                float cover = SelectionCoverage(selection, docX, docY);
                if (requireSelectionCoverage && cover <= 0f) continue;
                if (selection != null && cover <= 0f) continue;
                var updated = map(pixels.Get(x, y), x, y);
                if (selection != null && cover < 1f)
                    updated = Lerp(pixels.Get(x, y), updated, cover);
                pixels.Set(x, y, updated);
            }
        }
    }

    static void MixSelection(Layer layer, CanvasDocument document, SelectionMask? selection, PixelBuffer replacement)
    {
        if (selection == null || selection.IsEmpty())
        {
            layer.Pixels = replacement;
            return;
        }
        var original = layer.Pixels!;
        for (int y = 0; y < original.Height; y++)
        {
            for (int x = 0; x < original.Width; x++)
            {
                var (docX, docY) = layer.Transform.ToDocument((x + 0.5) / original.Width, (y + 0.5) / original.Height);
                float cover = SelectionCoverage(selection, docX, docY);
                if (cover <= 0f) continue;
                original.Set(x, y, Lerp(original.Get(x, y), replacement.Get(x, y), cover));
            }
        }
    }

    static PixelBuffer BlurBuffer(PixelBuffer source, float radius)
    {
        int r = Math.Clamp((int)Math.Ceiling(radius), 1, 48);
        double sigma = Math.Max(0.5, radius / 2);
        var kernel = new double[r * 2 + 1];
        double sum = 0;
        for (int i = -r; i <= r; i++)
        {
            double value = Math.Exp(-(i * i) / (2 * sigma * sigma));
            kernel[i + r] = value;
            sum += value;
        }
        for (int i = 0; i < kernel.Length; i++) kernel[i] /= sum;
        var horizontal = new float[source.Rgba.Length];
        PremultiplyBlur(source, horizontal, kernel, r, horizontalPass: true);
        var output = source.Clone();
        WriteBlur(horizontal, output, kernel, r, horizontalPass: false);
        return output;
    }

    static void PremultiplyBlur(PixelBuffer source, float[] destination, double[] kernel, int radius, bool horizontalPass)
    {
        int w = source.Width, h = source.Height;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double r = 0, g = 0, b = 0, a = 0;
                for (int i = -radius; i <= radius; i++)
                {
                    int sx = horizontalPass ? Math.Clamp(x + i, 0, w - 1) : x;
                    int sy = horizontalPass ? y : Math.Clamp(y + i, 0, h - 1);
                    int o = source.Offset(sx, sy);
                    double k = kernel[i + radius];
                    double alpha = source.Rgba[o + 3] / 255.0;
                    r += source.Rgba[o] * alpha * k;
                    g += source.Rgba[o + 1] * alpha * k;
                    b += source.Rgba[o + 2] * alpha * k;
                    a += alpha * k;
                }
                int d = (y * w + x) * 4;
                destination[d] = (float)r;
                destination[d + 1] = (float)g;
                destination[d + 2] = (float)b;
                destination[d + 3] = (float)a;
            }
        }
    }

    static void WriteBlur(float[] source, PixelBuffer destination, double[] kernel, int radius, bool horizontalPass)
    {
        int w = destination.Width, h = destination.Height;
        var temp = new float[source.Length];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double r = 0, g = 0, b = 0, a = 0;
                for (int i = -radius; i <= radius; i++)
                {
                    int sx = Math.Clamp(x, 0, w - 1);
                    int sy = Math.Clamp(y + i, 0, h - 1);
                    int o = (sy * w + sx) * 4;
                    double k = kernel[i + radius];
                    r += source[o] * k;
                    g += source[o + 1] * k;
                    b += source[o + 2] * k;
                    a += source[o + 3] * k;
                }
                int d = (y * w + x) * 4;
                temp[d] = (float)r;
                temp[d + 1] = (float)g;
                temp[d + 2] = (float)b;
                temp[d + 3] = (float)a;
            }
        }
        for (int i = 0; i < destination.Rgba.Length; i += 4)
        {
            float a = temp[i + 3];
            destination.Rgba[i + 3] = (byte)Math.Clamp((int)Math.Round(a * 255f), 0, 255);
            if (a <= 0.0001f) { destination.Rgba[i] = destination.Rgba[i + 1] = destination.Rgba[i + 2] = 0; continue; }
            destination.Rgba[i] = (byte)Math.Clamp((int)Math.Round(temp[i] / a * 255f), 0, 255);
            destination.Rgba[i + 1] = (byte)Math.Clamp((int)Math.Round(temp[i + 1] / a * 255f), 0, 255);
            destination.Rgba[i + 2] = (byte)Math.Clamp((int)Math.Round(temp[i + 2] / a * 255f), 0, 255);
        }
        _ = horizontalPass;
    }

    static bool TryLayerPixel(Layer layer, double docX, double docY, out int x, out int y)
    {
        x = y = 0;
        if (layer.Pixels == null || !layer.Transform.TryUnit(docX, docY, out double u, out double v)) return false;
        x = Math.Clamp((int)(u * layer.Pixels.Width), 0, layer.Pixels.Width - 1);
        y = Math.Clamp((int)(v * layer.Pixels.Height), 0, layer.Pixels.Height - 1);
        return true;
    }

    public static float SelectionCoverage(SelectionMask? selection, double docX, double docY)
    {
        if (selection == null) return 1f;
        return selection.Coverage((int)Math.Floor(docX), (int)Math.Floor(docY)) / 255f;
    }

    static float Falloff(float dist, int radius, float hardness)
    {
        if (dist > radius || radius <= 0) return 0f;
        if (hardness >= 0.999f) return 1f;
        return dist / radius <= hardness ? 1f : (1f - dist / radius) / Math.Max(0.0001f, 1f - hardness);
    }

    static byte Level(byte channel, float inBlack, float inWhite, float gamma, float outBlack, float outWhite)
    {
        float value = channel / 255f;
        float span = Math.Max(0.001f, inWhite - inBlack);
        float t = Math.Clamp((value - inBlack) / span, 0f, 1f);
        t = MathF.Pow(t, 1f / gamma);
        return (byte)Math.Clamp((int)Math.Round((outBlack + t * (outWhite - outBlack)) * 255f), 0, 255);
    }

    static byte Scale(byte channel, float gain) => (byte)Math.Clamp((int)Math.Round(channel * gain), 0, 255);

    static Rgba Lerp(Rgba a, Rgba b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Rgba(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t),
            (byte)Math.Round(a.A + (b.A - a.A) * t));
    }

    static bool Ellipse(int x, int y, int width, int height)
    {
        double rx = width / 2.0;
        double ry = height / 2.0;
        if (rx <= 0 || ry <= 0) return false;
        double dx = (x + 0.5 - rx) / rx;
        double dy = (y + 0.5 - ry) / ry;
        return dx * dx + dy * dy <= 1;
    }
}

public enum ShapeKind
{
    Rectangle,
    Ellipse,
    Line
}

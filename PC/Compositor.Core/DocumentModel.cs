namespace Compositor;

public enum Sampling
{
    Nearest,
    Smooth,
    High
}

/// <summary>Unrotated bounds in document pixels. Rotation is clockwise around the center, matching the Mac app.</summary>
public struct LayerTransform : IEquatable<LayerTransform>
{
    public double OriginX { get; set; }
    public double OriginY { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Rotation { get; set; }
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    public Sampling Sampling { get; set; } = Sampling.High;

    public LayerTransform()
    {
        Width = 1;
        Height = 1;
    }

    public double CenterX => OriginX + Width / 2;
    public double CenterY => OriginY + Height / 2;
    public double Radians => (Rotation % 360) * Math.PI / 180;

    public bool IsValid =>
        double.IsFinite(OriginX) && double.IsFinite(OriginY) && double.IsFinite(Width) && double.IsFinite(Height) && double.IsFinite(Rotation)
        && Width is >= 1 and <= 300_000 && Height is >= 1 and <= 300_000
        && Math.Abs(OriginX) <= 1_000_000 && Math.Abs(OriginY) <= 1_000_000;

    public static string SamplingName(Sampling sampling) => sampling switch
    {
        Sampling.Nearest => "Nearest",
        Sampling.Smooth => "Smooth",
        _ => "High quality"
    };

    public static Sampling ParseSampling(string? name) => name switch
    {
        "Nearest" => Sampling.Nearest,
        "Smooth" => Sampling.Smooth,
        _ => Sampling.High
    };

    public (double X, double Y) ToDocument(double unitX, double unitY)
    {
        double x = (unitX - 0.5) * Width;
        double y = (unitY - 0.5) * Height;
        if (FlipX) x = -x;
        if (FlipY) y = -y;
        double cos = Math.Cos(Radians);
        double sin = Math.Sin(Radians);
        return (CenterX + x * cos - y * sin, CenterY + x * sin + y * cos);
    }

    public bool TryUnit(double docX, double docY, out double unitX, out double unitY)
    {
        double dx = docX - CenterX;
        double dy = docY - CenterY;
        double cos = Math.Cos(Radians);
        double sin = Math.Sin(Radians);
        double x = dx * cos + dy * sin;
        double y = -dx * sin + dy * cos;
        if (FlipX) x = -x;
        if (FlipY) y = -y;
        unitX = Width == 0 ? 0 : x / Width + 0.5;
        unitY = Height == 0 ? 0 : y / Height + 0.5;
        return unitX >= 0 && unitY >= 0 && unitX <= 1 && unitY <= 1;
    }

    public bool Contains(double docX, double docY) => TryUnit(docX, docY, out _, out _);

    public (double X, double Y)[] Corners()
    {
        (double, double)[] units = [(0, 0), (1, 0), (1, 1), (0, 1)];
        var corners = new (double X, double Y)[4];
        for (int i = 0; i < units.Length; i++) corners[i] = ToDocument(units[i].Item1, units[i].Item2);
        return corners;
    }

    public bool Equals(LayerTransform other) =>
        OriginX.Equals(other.OriginX) && OriginY.Equals(other.OriginY) && Width.Equals(other.Width) && Height.Equals(other.Height)
        && Rotation.Equals(other.Rotation) && FlipX == other.FlipX && FlipY == other.FlipY && Sampling == other.Sampling;

    public override bool Equals(object? obj) => obj is LayerTransform other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(OriginX, OriginY, Width, Height, Rotation, FlipX, FlipY, Sampling);
}

public sealed class Guide
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Horizontal { get; set; }
    public double Position { get; set; }
}

public sealed class Layer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Layer";
    public bool Visible { get; set; } = true;
    public double Opacity { get; set; } = 1;
    public BlendMode BlendMode { get; set; } = BlendMode.Normal;
    public LayerTransform Transform { get; set; } = new() { Width = 1, Height = 1 };
    public PixelBuffer? Pixels { get; set; }
    public PixelBuffer? Mask { get; set; }
    public bool MaskEnabled { get; set; } = true;
    public LayerTransform? MaskPlacement { get; set; }
    public bool MaskLinked { get; set; } = true;
    public Guid? MaskSourceId { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsGroup { get; set; }
    /// <summary>Project fields this build preserves but does not edit, so Mac files round-trip.</summary>
    public System.Text.Json.Nodes.JsonObject? Extras { get; set; }

    public Layer Clone() => new()
    {
        Id = Id,
        Name = Name,
        Visible = Visible,
        Opacity = Opacity,
        BlendMode = BlendMode,
        Transform = Transform,
        Pixels = Pixels?.Clone(),
        Mask = Mask?.Clone(),
        MaskEnabled = MaskEnabled,
        MaskPlacement = MaskPlacement,
        MaskLinked = MaskLinked,
        MaskSourceId = MaskSourceId,
        ParentId = ParentId,
        IsGroup = IsGroup,
        Extras = Extras?.DeepClone() as System.Text.Json.Nodes.JsonObject
    };

    public void ForgetVectorMetadata()
    {
        Extras?.Remove("text");
        Extras?.Remove("shape");
    }
}

public sealed class CanvasDocument
{
    public const int MaxSide = 30_000;

    public Guid Id { get; set; } = Guid.NewGuid();
    public int Width { get; set; }
    public int Height { get; set; }
    public double Resolution { get; set; } = 72;
    public int FormatVersion { get; set; } = 8;
    public List<Layer> Layers { get; } = [];
    public List<Guide> Guides { get; } = [];
    public Guid? ActiveLayerId { get; set; }

    public Layer? ActiveLayer => Layers.FirstOrDefault(layer => layer.Id == ActiveLayerId);

    public static bool ValidDimension(int value) => value is >= 1 and <= MaxSide;

    public CanvasDocument Clone()
    {
        var copy = new CanvasDocument
        {
            Id = Id,
            Width = Width,
            Height = Height,
            Resolution = Resolution,
            FormatVersion = FormatVersion,
            ActiveLayerId = ActiveLayerId
        };
        copy.Layers.AddRange(Layers.Select(layer => layer.Clone()));
        copy.Guides.AddRange(Guides.Select(guide => new Guide { Id = guide.Id, Horizontal = guide.Horizontal, Position = guide.Position }));
        return copy;
    }

    public Layer AddPixelLayer(string name, PixelBuffer pixels, double x = 0, double y = 0)
    {
        var layer = new Layer
        {
            Name = name,
            Pixels = pixels,
            Transform = new LayerTransform { OriginX = x, OriginY = y, Width = pixels.Width, Height = pixels.Height, Sampling = Sampling.High }
        };
        Layers.Add(layer);
        ActiveLayerId = layer.Id;
        return layer;
    }

    public Layer AddEmptyLayer(string? name = null)
    {
        var layer = new Layer
        {
            Name = name ?? NextLayerName(),
            Pixels = new PixelBuffer(Width, Height),
            Transform = new LayerTransform { Width = Width, Height = Height, Sampling = Sampling.High }
        };
        Layers.Add(layer);
        ActiveLayerId = layer.Id;
        return layer;
    }

    public string NextLayerName()
    {
        int n = Layers.Count(layer => !layer.IsGroup) + 1;
        string name;
        do { name = $"Layer {n}"; n++; }
        while (Layers.Any(layer => layer.Name == name));
        return name;
    }

    public Dictionary<Guid, Layer> ById() => Layers.ToDictionary(layer => layer.Id);

    public bool EffectivelyVisible(Layer layer)
    {
        var byId = ById();
        var current = layer;
        int depth = 0;
        while (true)
        {
            if (!current.Visible) return false;
            if (current.ParentId is not Guid parent || !byId.TryGetValue(parent, out var folder) || depth++ > 64) return current.ParentId is null;
            current = folder;
        }
    }

    public float EffectiveOpacity(Layer layer)
    {
        var byId = ById();
        double opacity = layer.Opacity;
        Guid? parent = layer.ParentId;
        int depth = 0;
        while (parent is Guid id && depth++ < 64 && byId.TryGetValue(id, out var folder))
        {
            opacity *= folder.Opacity;
            parent = folder.ParentId;
        }
        return (float)Math.Clamp(opacity, 0, 1);
    }

    public int Depth(Layer layer)
    {
        var byId = ById();
        int depth = 0;
        Guid? parent = layer.ParentId;
        while (parent is Guid id && depth < 64 && byId.TryGetValue(id, out var folder))
        {
            depth++;
            parent = folder.ParentId;
        }
        return depth;
    }
}

public enum SelectionCombine
{
    Replace,
    Add,
    Subtract
}

public sealed class SelectionMask
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Values { get; }
    public int ChangeCount { get; private set; }
    public void Touch() => ChangeCount++;

    public SelectionMask(int width, int height, byte[]? values = null)
    {
        Width = width;
        Height = height;
        Values = values ?? new byte[width * height];
        if (Values.Length != width * height) throw new ArgumentException("Selection storage does not match the canvas.", nameof(values));
    }

    public SelectionMask Clone()
    {
        var copy = new byte[Values.Length];
        System.Buffer.BlockCopy(Values, 0, copy, 0, Values.Length);
        return new SelectionMask(Width, Height, copy);
    }

    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public byte Coverage(int x, int y) => Contains(x, y) ? Values[y * Width + x] : (byte)0;

    public bool IsEmpty()
    {
        foreach (var value in Values)
            if (value != 0) return false;
        return true;
    }

    public (int X, int Y, int Width, int Height)? Bounds()
    {
        int minX = Width, minY = Height, maxX = -1, maxY = -1;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (Values[y * Width + x] != 0)
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0) return null;
        return (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}

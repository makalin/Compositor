using System.Text.Json.Nodes;

namespace Compositor;

public sealed class ProjectException : Exception
{
    public ProjectException(string message) : base(message) { }
}

/// <summary>Reads and writes the Mac <c>.comp</c> package: <c>manifest.json</c> plus <c>images</c>.</summary>
public static class ProjectPackage
{
    public static CanvasDocument Load(string path)
    {
        string package = ResolvePackage(path);
        string manifestPath = Path.Combine(package, "manifest.json");
        if (!File.Exists(manifestPath)) throw new ProjectException("This folder does not contain a Compositor project.");
        var root = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject
            ?? throw new ProjectException("This project file is damaged.");
        if (root["format"]?.GetValue<string>() != "com.compositor.project")
            throw new ProjectException("This is not a Compositor project.");
        int version = root["version"]?.GetValue<int>() ?? 0;
        if (version is < 1 or > 9) throw new ProjectException($"This project uses format version {version}. This app supports versions 1–9.");
        if (root["colorSpace"]?.GetValue<string>() != "sRGB") throw new ProjectException("This project uses an unsupported color space.");
        int width = root["width"]?.GetValue<int>() ?? 0;
        int height = root["height"]?.GetValue<int>() ?? 0;
        if (!CanvasDocument.ValidDimension(width) || !CanvasDocument.ValidDimension(height))
            throw new ProjectException("This project exceeds the supported canvas size.");
        var document = new CanvasDocument
        {
            Id = ReadGuid(root["documentID"]) ?? Guid.NewGuid(),
            Width = width,
            Height = height,
            Resolution = root["resolution"]?.GetValue<double>() ?? 72,
            FormatVersion = version,
            ActiveLayerId = ReadGuid(root["activeLayerID"])
        };
        if (root["layers"] is not JsonArray layers) throw new ProjectException("This project is missing its layers.");
        long pixels = 0;
        foreach (var node in layers)
        {
            if (node is not JsonObject record) throw new ProjectException("A layer in this project is damaged.");
            document.Layers.Add(ReadLayer(record, package, ref pixels));
        }
        if (root["guides"] is JsonArray guides)
        {
            foreach (var node in guides)
            {
                if (node is not JsonObject guide) continue;
                document.Guides.Add(new Guide
                {
                    Id = ReadGuid(guide["id"]) ?? Guid.NewGuid(),
                    Horizontal = guide["axis"]?.GetValue<string>() != "vertical",
                    Position = guide["position"]?.GetValue<double>() ?? 0
                });
            }
        }
        if (document.ActiveLayerId is not Guid active || document.Layers.All(layer => layer.Id != active))
            document.ActiveLayerId = document.Layers.LastOrDefault()?.Id;
        return document;
    }

    public static void Save(CanvasDocument document, string packagePath)
    {
        string package = Path.GetFullPath(packagePath);
        var temp = package + ".writing";
        if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        Directory.CreateDirectory(Path.Combine(temp, "images"));
        int version = Math.Max(document.FormatVersion, RequiredVersion(document));
        var layers = new JsonArray();
        foreach (var layer in document.Layers)
            layers.Add(WriteLayer(layer, Path.Combine(temp, "images")));
        var root = new JsonObject
        {
            ["format"] = "com.compositor.project",
            ["version"] = version,
            ["colorSpace"] = "sRGB",
            ["resolution"] = document.Resolution,
            ["documentID"] = Id(document.Id),
            ["width"] = document.Width,
            ["height"] = document.Height,
            ["activeLayerID"] = document.ActiveLayerId is Guid active ? Id(active) : JsonValue.Create((string?)null),
            ["layers"] = layers,
            ["guides"] = new JsonArray(document.Guides.Select(guide => (JsonNode)new JsonObject
            {
                ["id"] = Id(guide.Id),
                ["axis"] = guide.Horizontal ? "horizontal" : "vertical",
                ["position"] = guide.Position
            }).ToArray())
        };
        File.WriteAllText(Path.Combine(temp, "manifest.json"), root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (Directory.Exists(package)) Directory.Delete(package, recursive: true);
        Directory.Move(temp, package);
        document.FormatVersion = version;
    }

    static int RequiredVersion(CanvasDocument document)
    {
        int version = 3;
        if (document.Layers.Any(layer => layer.ParentId != null || layer.IsGroup)) version = Math.Max(version, 3);
        if (document.Layers.Any(layer => layer.Mask != null && !layer.IsGroup)) version = Math.Max(version, 4);
        if (document.Layers.Any(layer => layer.MaskSourceId != null)) version = Math.Max(version, 5);
        if (document.Layers.Any(layer => layer.IsGroup && layer.Mask != null)) version = Math.Max(version, 6);
        if (document.Guides.Count > 0 || document.Layers.Any(layer => layer.IsGroup && Math.Abs(layer.Opacity - 1) > 0.0001))
            version = Math.Max(version, 8);
        return version;
    }

    static Layer ReadLayer(JsonObject record, string package, ref long pixels)
    {
        var id = ReadGuid(record["id"]) ?? throw new ProjectException("A layer is missing its id.");
        var layer = new Layer
        {
            Id = id,
            Name = record["name"]?.GetValue<string>() ?? "Layer",
            Visible = record["isVisible"]?.GetValue<bool>() ?? true,
            Opacity = record["opacity"]?.GetValue<double>() ?? 1,
            BlendMode = BlendModes.Parse(record["blendMode"]?.GetValue<string>()),
            Transform = ReadTransform(record["transform"] as JsonObject),
            ParentId = ReadGuid(record["parentID"]),
            IsGroup = record["isGroup"]?.GetValue<bool>() ?? false,
            MaskEnabled = record["maskEnabled"]?.GetValue<bool>() ?? true,
            MaskLinked = record["maskLinked"]?.GetValue<bool>() ?? true,
            MaskSourceId = ReadGuid(record["maskSourceID"])
        };
        if (record["maskPlacement"] is JsonObject placement) layer.MaskPlacement = ReadTransform(placement);
        if (record["imageFile"]?.GetValue<string>() is string imageFile)
            layer.Pixels = ReadAsset(package, imageFile, id, mask: false, ref pixels);
        if (record["maskFile"]?.GetValue<string>() is string maskFile)
            layer.Mask = ReadAsset(package, maskFile, id, mask: true, ref pixels);
        var extras = new JsonObject();
        foreach (var key in new[] { "adjustment", "shape", "effects", "text" })
        {
            if (record[key] != null) extras[key] = record[key]!.DeepClone();
        }
        layer.Extras = extras.Count > 0 ? extras : null;
        return layer;
    }

    static JsonObject WriteLayer(Layer layer, string imagesDirectory)
    {
        var id = Id(layer.Id);
        string? imageFile = null;
        if (!layer.IsGroup && layer.Pixels is { } pixels && pixels.HasVisiblePixels())
        {
            imageFile = id + ".png";
            ImageFiles.SavePng(pixels, Path.Combine(imagesDirectory, imageFile));
        }
        string? maskFile = null;
        if (layer.Mask is { } mask)
        {
            maskFile = id + ".mask.png";
            ImageFiles.SavePng(mask, Path.Combine(imagesDirectory, maskFile));
        }
        var record = new JsonObject
        {
            ["id"] = id,
            ["name"] = layer.Name,
            ["isVisible"] = layer.Visible,
            ["transform"] = WriteTransform(layer.Transform),
            ["opacity"] = layer.Opacity,
            ["blendMode"] = layer.IsGroup ? "Normal" : BlendModes.Name(layer.BlendMode)
        };
        if (imageFile != null) record["imageFile"] = imageFile;
        if (layer.ParentId is Guid parent) record["parentID"] = Id(parent);
        if (layer.IsGroup) record["isGroup"] = true;
        if (maskFile != null)
        {
            record["maskFile"] = maskFile;
            record["maskEnabled"] = layer.MaskEnabled;
            if (!layer.MaskLinked) record["maskLinked"] = false;
            if (layer.MaskPlacement is { } placement) record["maskPlacement"] = WriteTransform(placement);
        }
        if (layer.MaskSourceId is Guid source) record["maskSourceID"] = Id(source);
        if (layer.Extras != null)
        {
            foreach (var pair in layer.Extras)
                record[pair.Key] = pair.Value?.DeepClone();
        }
        return record;
    }

    static PixelBuffer ReadAsset(string package, string fileName, Guid id, bool mask, ref long pixels)
    {
        string expected = Id(id) + (mask ? ".mask.png" : ".png");
        if (!string.Equals(fileName, expected, StringComparison.OrdinalIgnoreCase))
            throw new ProjectException("A layer image path is not valid.");
        string path = Path.Combine(package, "images", expected);
        var root = Path.GetFullPath(package) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            throw new ProjectException("An image inside the project is missing.");
        var buffer = ImageFiles.Load(full);
        pixels += (long)buffer.Width * buffer.Height;
        if (pixels > 100_000_000) throw new ProjectException("This project exceeds the 100-megapixel image limit.");
        return buffer;
    }

    static LayerTransform ReadTransform(JsonObject? node)
    {
        if (node == null) return new LayerTransform { Width = 1, Height = 1 };
        var (x, y) = ReadPair(node["origin"], "x", "y");
        var (w, h) = ReadPair(node["size"], "width", "height");
        return new LayerTransform
        {
            OriginX = x,
            OriginY = y,
            Width = Math.Max(1, w),
            Height = Math.Max(1, h),
            Rotation = node["rotation"]?.GetValue<double>() ?? 0,
            FlipX = node["flipX"]?.GetValue<bool>() ?? false,
            FlipY = node["flipY"]?.GetValue<bool>() ?? false,
            Sampling = LayerTransform.ParseSampling(node["sampling"]?.GetValue<string>())
        };
    }

    static JsonObject WriteTransform(LayerTransform transform) => new()
    {
        ["origin"] = new JsonArray(JsonValue.Create(transform.OriginX), JsonValue.Create(transform.OriginY)),
        ["size"] = new JsonArray(JsonValue.Create(transform.Width), JsonValue.Create(transform.Height)),
        ["rotation"] = transform.Rotation,
        ["flipX"] = transform.FlipX,
        ["flipY"] = transform.FlipY,
        ["sampling"] = LayerTransform.SamplingName(transform.Sampling)
    };

    static (double, double) ReadPair(JsonNode? node, string a, string b)
    {
        if (node is JsonArray array && array.Count >= 2)
            return (array[0]!.GetValue<double>(), array[1]!.GetValue<double>());
        if (node is JsonObject obj)
            return (obj[a]?.GetValue<double>() ?? 0, obj[b]?.GetValue<double>() ?? 0);
        return (0, 1);
    }

    static Guid? ReadGuid(JsonNode? node)
    {
        var text = node?.GetValue<string>();
        return Guid.TryParse(text, out var id) ? id : null;
    }

    static string Id(Guid id) => id.ToString("D").ToUpperInvariant();

    static string ResolvePackage(string path)
    {
        if (File.Exists(path) && string.Equals(Path.GetFileName(path), "manifest.json", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(Path.GetFullPath(path)) ?? path;
        if (Directory.Exists(path)) return Path.GetFullPath(path);
        throw new ProjectException("Choose a Compositor project folder.");
    }
}

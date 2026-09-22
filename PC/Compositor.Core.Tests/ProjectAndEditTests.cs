namespace Compositor.Tests;

public class ProjectAndEditTests
{
    [Fact]
    public void ProjectRoundTripKeepsLayersAndSwiftPoints()
    {
        var document = new CanvasDocument { Width = 8, Height = 6, Resolution = 144 };
        var pixels = new PixelBuffer(4, 4);
        pixels.Set(1, 1, new Rgba(10, 20, 30, 255));
        var layer = document.AddPixelLayer("Photo", pixels, 2, 1);
        layer.Opacity = 0.5;
        layer.BlendMode = BlendMode.Multiply;
        layer.Mask = new PixelBuffer(4, 4);
        layer.Mask.Clear(Rgba.White);
        document.Guides.Add(new Guide { Horizontal = true, Position = 3 });
        var folder = new Layer { Name = "Group", IsGroup = true, Transform = new LayerTransform { Width = 8, Height = 6 } };
        document.Layers.Insert(0, folder);
        layer.ParentId = folder.Id;

        string folderPath = Path.Combine(Path.GetTempPath(), "compositor-test-" + Guid.NewGuid().ToString("N"));
        ProjectPackage.Save(document, folderPath);
        var loaded = ProjectPackage.Load(folderPath);

        Assert.Equal(8, loaded.Width);
        Assert.Equal(144, loaded.Resolution);
        Assert.Equal(2, loaded.Layers.Count);
        var photo = loaded.Layers.Single(item => item.Name == "Photo");
        Assert.Equal(BlendMode.Multiply, photo.BlendMode);
        Assert.Equal(0.5, photo.Opacity, 3);
        Assert.Equal(2, photo.Transform.OriginX, 3);
        Assert.Equal(new Rgba(10, 20, 30, 255), photo.Pixels!.Get(1, 1));
        Assert.NotNull(photo.Mask);
        Assert.Equal(folder.Id, photo.ParentId);
        Assert.Single(loaded.Guides);
        Directory.Delete(folderPath, recursive: true);
    }

    [Fact]
    public void ReadsArrayAndObjectTransforms()
    {
        string package = Path.Combine(Path.GetTempPath(), "compositor-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(package, "images"));
        var id = Guid.NewGuid();
        var pixels = new PixelBuffer(2, 2);
        pixels.Clear(new Rgba(1, 2, 3, 255));
        ImageFiles.SavePng(pixels, Path.Combine(package, "images", id.ToString("D").ToUpperInvariant() + ".png"));
        string manifest = $$"""
        {
          "format": "com.compositor.project",
          "version": 3,
          "colorSpace": "sRGB",
          "documentID": "{{Guid.NewGuid()}}",
          "width": 2,
          "height": 2,
          "activeLayerID": "{{id}}",
          "layers": [{
            "id": "{{id}}",
            "name": "Layer 1",
            "isVisible": true,
            "transform": { "origin": [4, 5], "size": {"width": 2, "height": 2}, "rotation": 0, "flipX": false, "flipY": false, "sampling": "High quality" },
            "imageFile": "{{id.ToString("D").ToUpperInvariant()}}.png",
            "opacity": 1,
            "blendMode": "Screen"
          }]
        }
        """;
        File.WriteAllText(Path.Combine(package, "manifest.json"), manifest);
        var loaded = ProjectPackage.Load(package);
        Assert.Equal(4, loaded.Layers[0].Transform.OriginX, 3);
        Assert.Equal(5, loaded.Layers[0].Transform.OriginY, 3);
        Assert.Equal(BlendMode.Screen, loaded.Layers[0].BlendMode);
        Directory.Delete(package, recursive: true);
    }

    [Fact]
    public void MagicWandSelectsConnectedColor()
    {
        var image = new PixelBuffer(3, 1);
        image.Set(0, 0, Rgba.White);
        image.Set(1, 0, Rgba.White);
        image.Set(2, 0, Rgba.Black);
        var mask = SelectionEdits.MagicWand(image, 0, 0, 0, contiguous: true, SelectionCombine.Replace, null);
        Assert.Equal(255, mask.Coverage(0, 0));
        Assert.Equal(255, mask.Coverage(1, 0));
        Assert.Equal(0, mask.Coverage(2, 0));
    }

    [Fact]
    public void LevelsLiftsBlack()
    {
        var document = new CanvasDocument { Width = 1, Height = 1 };
        var pixels = new PixelBuffer(1, 1);
        pixels.Set(0, 0, Rgba.Black);
        var layer = document.AddPixelLayer("Layer", pixels);
        PixelEdits.ApplyLevels(layer, document, null, 0f, 1f, 1f, 0.5f, 1f);
        Assert.InRange(layer.Pixels!.Get(0, 0).R, 120, 135);
    }

    [Fact]
    public void BrushPaintsOpaqueCenter()
    {
        var document = new CanvasDocument { Width = 20, Height = 20 };
        var layer = document.AddEmptyLayer();
        PixelEdits.PaintDab(layer, document, null, 10, 10, 4, 1f, 1f, Rgba.Black, erase: false);
        Assert.Equal(255, layer.Pixels!.Get(10, 10).A);
        Assert.Equal(0, layer.Pixels.Get(0, 0).A);
    }
}

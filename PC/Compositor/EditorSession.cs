using System.IO;

namespace Compositor;

public enum EditorTool
{
    Move, Marquee, Lasso, Wand, Crop, Brush, SpotHealing, CloneStamp, Blur, Gradient, Shape, Type, Eyedropper, Hand, Zoom
}

public sealed partial class EditorSession
{
    public CanvasDocument? Document { get; private set; }
    public SelectionMask? Selection { get; private set; }
    public EditorTool Tool { get; private set; } = EditorTool.Move;
    public Rgba Foreground { get; set; } = Rgba.Black;
    public Rgba Background { get; set; } = Rgba.White;
    public double Zoom { get; private set; } = 1;
    public double PanX { get; private set; }
    public double PanY { get; private set; }
    public bool ShowRulers { get; set; } = true;
    public bool ShowGrid { get; set; }
    public bool ShowGuides { get; set; } = true;
    public bool SnapEnabled { get; set; } = true;
    public bool ShowPixelGrid { get; set; } = true;
    public bool Dirty { get; private set; }
    public string? ProjectPath { get; private set; }
    public string StatusText { get; private set; } = "Ready when you are";
    public bool Hovering { get; private set; }
    public double HoverX { get; private set; }
    public double HoverY { get; private set; }
    public int Structure { get; private set; }

    public int BrushSize { get; set; } = 24;
    public int BrushHardness { get; set; } = 80;
    public int BrushOpacity { get; set; } = 100;
    public bool Erase { get; set; }
    public bool EllipseMarquee { get; set; }
    public bool PolygonalLasso { get; set; }
    public int Tolerance { get; set; } = 32;
    public bool Contiguous { get; set; } = true;
    public ShapeKind Shape { get; set; } = ShapeKind.Rectangle;
    public bool PaintMask { get; set; }
    public bool SpacePan { get; set; }
    public bool FitRequested { get; set; }

    public (int X, int Y, int Width, int Height)? CropRect { get; private set; }
    public (double X, double Y, double Width, double Height)? MarqueeRect { get; private set; }
    public IReadOnlyList<(double X, double Y)>? LassoPoints => _lasso;
    public (double X0, double Y0, double X1, double Y1)? GradientLine { get; private set; }
    public (double X, double Y, double Width, double Height)? ShapeRect { get; private set; }

    public bool CanUndo => _undo.Count > 0 && !_changing;
    public bool CanRedo => _redo.Count > 0 && !_changing;
    public bool HasDocument => Document != null;
    public string Title => Document == null ? "Compositor" : Path.GetFileNameWithoutExtension(ProjectPath ?? "") is { Length: > 0 } name ? name + (Dirty ? " •" : "") : "Untitled" + (Dirty ? " •" : "");

    public event Action? Changed;
    public event Action? Painted;
    public event Action<double, double>? TextRequested;

    readonly List<Snapshot> _undo = [];
    readonly List<Snapshot> _redo = [];
    bool _changing;
    PixelBuffer? _composite;
    bool _compositeDirty = true;
    PixelBuffer? _clipboard;
    (int X, int Y)? _clipboardOrigin;

    sealed record Snapshot(CanvasDocument Document, SelectionMask? Selection);

    public PixelBuffer? Composite()
    {
        if (Document == null) return null;
        if (_compositeDirty || _composite == null || _composite.Width != Document.Width || _composite.Height != Document.Height)
        {
            _composite = CompositorEngine.Flatten(Document);
            _compositeDirty = false;
        }
        return _composite;
    }

    public void NewCanvas(int width, int height)
    {
        Document = new CanvasDocument { Width = width, Height = height };
        Document.AddEmptyLayer();
        ProjectPath = null;
        ResetViewState();
        StatusText = "Transparent canvas · sRGB";
        Publish();
    }

    public void OpenProject(string path)
    {
        Document = ProjectPackage.Load(path);
        ResetViewState();
        ProjectPath = Directory.Exists(path) ? Path.GetFullPath(path) : Path.GetDirectoryName(Path.GetFullPath(path));
        Dirty = false;
        StatusText = "Opened project";
        Publish();
    }

    public void SaveProject(string path)
    {
        if (Document == null) return;
        ProjectPackage.Save(Document, path);
        ProjectPath = path;
        Dirty = false;
        StatusText = "Saved";
        Publish();
    }

    public void ImportImage(string path)
    {
        var pixels = ImageFiles.Load(path);
        bool created = Document == null;
        if (created)
        {
            Document = new CanvasDocument { Width = pixels.Width, Height = pixels.Height };
            ProjectPath = null;
            FitRequested = true;
            _undo.Clear();
            _redo.Clear();
        }
        else BeginChange();
        Document!.AddPixelLayer(Path.GetFileNameWithoutExtension(path), pixels);
        if (!created) EndChange();
        Dirty = true;
        StatusText = "Imported " + Path.GetFileName(path);
        Publish();
    }

    public void ExportPng(string path)
    {
        var flat = Composite();
        if (flat == null) return;
        ImageFiles.SavePng(flat, path);
        StatusText = "Exported PNG";
        Painted?.Invoke();
    }

    public void ExportJpeg(string path, int quality)
    {
        var flat = Composite();
        if (flat == null) return;
        ImageFiles.SaveJpeg(flat, path, quality);
        StatusText = "Exported JPEG";
        Painted?.Invoke();
    }

    public void CloseDocument()
    {
        Document = null;
        Selection = null;
        ProjectPath = null;
        Dirty = false;
        _undo.Clear();
        _redo.Clear();
        _composite = null;
        CropRect = null;
        StatusText = "Ready when you are";
        Structure++;
        Changed?.Invoke();
    }

    public void SelectTool(EditorTool tool)
    {
        if (Tool == tool && tool == EditorTool.Brush) Erase = false;
        Tool = tool;
        if (tool != EditorTool.Crop) CropRect = null;
        CancelDrag();
        StatusText = ToolHint();
        Publish();
    }

    public void UseEraser()
    {
        Tool = EditorTool.Brush;
        Erase = true;
        StatusText = ToolHint();
        Publish();
    }

    public void Undo() => Restore(pop: _undo, push: _redo);
    public void Redo() => Restore(pop: _redo, push: _undo);

    public void Fit(double viewWidth, double viewHeight)
    {
        if (Document == null || viewWidth < 8 || viewHeight < 8) return;
        double ruler = ShowRulers ? 18 : 0;
        Zoom = Math.Clamp(Math.Min((viewWidth - ruler) / Document.Width, (viewHeight - ruler) / Document.Height), 0.02, 32);
        PanX = ruler + Math.Max(0, (viewWidth - ruler - Document.Width * Zoom) / 2);
        PanY = ruler + Math.Max(0, (viewHeight - ruler - Document.Height * Zoom) / 2);
        FitRequested = false;
        Painted?.Invoke();
    }

    public void ZoomBy(double factor, double anchorX, double anchorY)
    {
        if (Document == null) return;
        double next = Math.Clamp(Zoom * factor, 0.02, 32);
        PanX = anchorX - (anchorX - PanX) * (next / Zoom);
        PanY = anchorY - (anchorY - PanY) * (next / Zoom);
        Zoom = next;
        Painted?.Invoke();
    }

    public void PanBy(double dx, double dy)
    {
        PanX += dx;
        PanY += dy;
        Painted?.Invoke();
    }

    public void SelectAll()
    {
        if (Document == null) return;
        Selection = SelectionEdits.All(Document.Width, Document.Height);
        Painted?.Invoke();
    }

    public void Deselect()
    {
        Selection = null;
        Painted?.Invoke();
    }

    public void InvertSelection()
    {
        if (Document == null) return;
        Selection ??= new SelectionMask(Document.Width, Document.Height);
        SelectionEdits.Invert(Selection);
        Painted?.Invoke();
    }

    public void SelectLayerPixels()
    {
        if (Document?.ActiveLayer is not { } layer) return;
        Selection = SelectionEdits.FromLayerAlpha(layer, Document);
        Painted?.Invoke();
    }

    public void ModifySelection(bool contract, int radius, bool feather)
    {
        if (Selection == null || Selection.IsEmpty()) return;
        if (feather) SelectionEdits.Feather(Selection, radius);
        else SelectionEdits.Expand(Selection, radius, contract);
        Painted?.Invoke();
    }

    public void Copy(bool merged)
    {
        if (Document == null) return;
        if (merged)
        {
            var flat = Composite();
            if (flat == null) return;
            _clipboard = SelectionEdits.Copy(flat, Selection);
            _clipboardOrigin = Selection?.Bounds() is { } bounds ? (bounds.X, bounds.Y) : (0, 0);
        }
        else if (Document.ActiveLayer?.Pixels != null)
        {
            _clipboard = CopyActiveLayer();
        }
        StatusText = "Copied";
    }

    public void Cut()
    {
        if (Document?.ActiveLayer == null || Selection == null) return;
        Copy(merged: false);
        Edit(() => PixelEdits.Clear(Document.ActiveLayer, Document, Selection));
    }

    public void Paste(double x, double y)
    {
        if (Document == null || _clipboard == null) return;
        var image = _clipboard.Clone();
        Edit(() =>
        {
            var layer = Document.AddPixelLayer("Pasted", image, x, y);
            layer.Name = Document.NextLayerName();
        });
    }

    public void Fill(bool foreground)
    {
        if (Document?.ActiveLayer == null) return;
        var color = foreground ? Foreground : Background;
        Edit(() => PixelEdits.Fill(Document.ActiveLayer, Document, Selection, color));
    }

    public void ClearSelectedPixels()
    {
        if (Document?.ActiveLayer == null || Selection == null) return;
        Edit(() => PixelEdits.Clear(Document.ActiveLayer, Document, Selection));
    }

    public void ApplyLevels(float inBlack, float inWhite, float gamma, float outBlack, float outWhite) =>
        OnActive(layer => PixelEdits.ApplyLevels(layer, Document!, Selection, inBlack, inWhite, gamma, outBlack, outWhite));

    public void ApplyHue(float hue, float saturation, float lightness) =>
        OnActive(layer => PixelEdits.ApplyHueSaturation(layer, Document!, Selection, hue, saturation, lightness));

    public void ApplyExposure(float ev) => OnActive(layer => PixelEdits.ApplyExposure(layer, Document!, Selection, ev));

    public void ApplyBlackAndWhite() => OnActive(layer => PixelEdits.ApplyBlackAndWhite(layer, Document!, Selection));

    public void InvertPixels() => OnActive(layer => PixelEdits.Invert(layer, Document!, Selection));

    public void ApplyBlur(float radius) => OnActive(layer => PixelEdits.GaussianBlur(layer, Document!, Selection, radius));

    public void ApplyMotionBlur(double angle, int distance) =>
        OnActive(layer => PixelEdits.MotionBlur(layer, Document!, Selection, angle, distance));

    public void ApplyNoise(int amount) =>
        OnActive(layer => PixelEdits.AddNoise(layer, Document!, Selection, amount, Random.Shared.Next()));

    public void ResizeCanvas(int width, int height, int anchor)
    {
        if (Document == null) return;
        Edit(() =>
        {
            PixelEdits.CanvasResize(Document, width, height, anchor);
            Selection = null;
        });
        FitRequested = true;
    }

    public void ResizeImage(int width, int height)
    {
        if (Document == null) return;
        Edit(() =>
        {
            PixelEdits.ImageResize(Document, width, height);
            Selection = null;
        });
        FitRequested = true;
    }

    public void Trim()
    {
        var flat = Composite();
        if (Document == null || flat == null) return;
        var bounds = PixelEdits.ContentBounds(flat);
        if (bounds == null) return;
        var (x, y, w, h) = bounds.Value;
        Edit(() =>
        {
            PixelEdits.Crop(Document, x, y, w, h);
            Selection = null;
        });
        FitRequested = true;
    }

    public void FlipCanvas(bool horizontal)
    {
        if (Document == null) return;
        Edit(() => PixelEdits.FlipCanvas(Document, horizontal));
    }

    public void FlipActive(bool horizontal)
    {
        if (Document?.ActiveLayer == null) return;
        Edit(() =>
        {
            if (horizontal) PixelEdits.FlipLayer(Document.ActiveLayer);
            else PixelEdits.FlipLayerVertical(Document.ActiveLayer);
        });
    }

    public void AddLayer()
    {
        if (Document == null) return;
        Edit(() => Document.AddEmptyLayer());
    }

    public void AddGroup()
    {
        if (Document == null) return;
        Edit(() =>
        {
            var group = new Layer
            {
                Name = "Group " + (Document.Layers.Count(layer => layer.IsGroup) + 1),
                IsGroup = true,
                Transform = new LayerTransform { Width = Document.Width, Height = Document.Height }
            };
            var active = Document.ActiveLayer;
            int index = active == null ? Document.Layers.Count : Document.Layers.IndexOf(active);
            group.ParentId = active?.ParentId;
            Document.Layers.Insert(Math.Max(0, index), group);
            if (active is { IsGroup: false }) active.ParentId = group.Id;
            Document.ActiveLayerId = group.Id;
        });
    }

    public void DuplicateActive()
    {
        if (Document?.ActiveLayer == null) return;
        Edit(() =>
        {
            var copy = Document.ActiveLayer.Clone();
            copy.Id = Guid.NewGuid();
            copy.Name = Document.ActiveLayer.Name + " copy";
            var transform = copy.Transform;
            transform.OriginX += 16;
            transform.OriginY += 16;
            copy.Transform = transform;
            Document.Layers.Insert(Document.Layers.IndexOf(Document.ActiveLayer) + 1, copy);
            Document.ActiveLayerId = copy.Id;
        });
    }

    public void DeleteActive()
    {
        if (Document?.ActiveLayer is not { } layer) return;
        Edit(() =>
        {
            foreach (var child in Document.Layers.Where(item => item.ParentId == layer.Id))
                child.ParentId = layer.ParentId;
            int index = Document.Layers.IndexOf(layer);
            Document.Layers.RemoveAt(index);
            Document.ActiveLayerId = Document.Layers.ElementAtOrDefault(Math.Clamp(index - 1, 0, Math.Max(0, Document.Layers.Count - 1)))?.Id;
        });
    }

    public void MergeDown()
    {
        if (Document?.ActiveLayerId is not Guid id) return;
        Edit(() => PixelEdits.MergeDown(Document, id));
    }

    public void AddMask()
    {
        if (Document?.ActiveLayer is not { Pixels: { } pixels } layer) return;
        Edit(() =>
        {
            layer.Mask = new PixelBuffer(pixels.Width, pixels.Height);
            layer.Mask.Clear(Rgba.White);
            layer.MaskEnabled = true;
            PaintMask = true;
        });
    }

    public void InvertMask()
    {
        if (Document?.ActiveLayer?.Mask == null) return;
        Edit(() => PixelEdits.Invert(new Layer
        {
            Pixels = Document.ActiveLayer.Mask,
            Transform = Document.ActiveLayer.Transform
        }, Document, null));
    }

    public void RenameActive(string name)
    {
        if (Document?.ActiveLayer == null || string.IsNullOrWhiteSpace(name)) return;
        Edit(() => Document.ActiveLayer.Name = name.Trim());
    }

    public void SetActive(Guid id)
    {
        if (Document == null || Document.ActiveLayerId == id) return;
        Document.ActiveLayerId = id;
        Publish();
    }

    public void ToggleVisible(Guid id)
    {
        var layer = Document?.Layers.FirstOrDefault(item => item.Id == id);
        if (layer == null || Document == null) return;
        Edit(() => layer.Visible = !layer.Visible);
    }

    public void SetOpacity(double opacity)
    {
        if (Document?.ActiveLayer == null) return;
        Document.ActiveLayer.Opacity = Math.Clamp(opacity, 0, 1);
        Dirty = true;
        Invalidate();
        Painted?.Invoke();
    }

    public void SetBlend(BlendMode mode)
    {
        if (Document?.ActiveLayer == null) return;
        Edit(() => Document.ActiveLayer.BlendMode = mode);
    }

    public void MoveActive(int direction)
    {
        if (Document?.ActiveLayer is not { } layer) return;
        int index = Document.Layers.IndexOf(layer);
        int next = index + direction;
        if (next < 0 || next >= Document.Layers.Count) return;
        if (Document.Layers[next].ParentId != layer.ParentId) return;
        Edit(() =>
        {
            (Document.Layers[index], Document.Layers[next]) = (Document.Layers[next], Document.Layers[index]);
        });
    }

    public void AddText(string text, int size, double x, double y, PixelBuffer raster)
    {
        if (Document == null || string.IsNullOrEmpty(text)) return;
        Edit(() =>
        {
            var layer = Document.AddPixelLayer("Text", raster, x, y);
            layer.Name = text.Length > 24 ? text[..24] : text;
        });
    }

    public void ApplyCrop()
    {
        if (Document == null || CropRect is not { } rect) return;
        if (rect.Width < 1 || rect.Height < 1) return;
        Edit(() =>
        {
            PixelEdits.Crop(Document, rect.X, rect.Y, rect.Width, rect.Height);
            Selection = Selection == null ? null : PixelEdits.CropSelection(Selection, rect.X, rect.Y, rect.Width, rect.Height);
            CropRect = null;
        });
        FitRequested = true;
    }

    public void CancelTransient()
    {
        CropRect = null;
        MarqueeRect = null;
        _lasso = null;
        GradientLine = null;
        ShapeRect = null;
        CancelDrag();
        Painted?.Invoke();
    }

    public void Hover(double x, double y, bool inside)
    {
        Hovering = inside;
        HoverX = x;
        HoverY = y;
        Painted?.Invoke();
    }

    public string ToolHint() => Tool switch
    {
        EditorTool.Move => "Drag to move · Handles to resize · Circle to rotate · Space to pan",
        EditorTool.Marquee => EllipseMarquee
            ? "Drag an ellipse · Shift add · Alt subtract · Delete clears"
            : "Drag a rectangle · Shift add · Alt subtract · Delete clears",
        EditorTool.Lasso => PolygonalLasso ? "Click corners · Enter closes · Escape cancels" : "Drag to select · Shift add · Alt subtract",
        EditorTool.Wand => "Click to select similar colors · Shift add · Alt subtract",
        EditorTool.Crop => "Drag to crop · Enter applies · Escape cancels",
        EditorTool.Brush => (Erase ? "Drag to erase" : "Drag to paint") + " · [ ] size · Space to pan",
        EditorTool.SpotHealing => "Drag over blemishes to heal · [ ] size",
        EditorTool.CloneStamp => "Alt-click sets the source · Drag to clone",
        EditorTool.Blur => "Drag to soften · [ ] size",
        EditorTool.Gradient => "Drag to draw a gradient · Shift constrains",
        EditorTool.Shape => "Drag to draw a shape on a new layer · Shift constrains",
        EditorTool.Type => "Click to place text",
        EditorTool.Eyedropper => "Click to sample · Alt samples the background",
        EditorTool.Hand => "Drag to pan",
        EditorTool.Zoom => "Click to zoom in · Alt-click to zoom out",
        _ => ""
    };

    void OnActive(Action<Layer> action)
    {
        if (Document?.ActiveLayer == null) return;
        Edit(() => action(Document.ActiveLayer));
    }

    void Edit(Action action)
    {
        BeginChange();
        action();
        EndChange();
        Dirty = true;
        Invalidate();
        Structure++;
        StatusText = ToolHint();
        Changed?.Invoke();
    }

    void BeginChange()
    {
        if (_changing || Document == null) return;
        _undo.Add(new Snapshot(Document.Clone(), Selection?.Clone()));
        if (_undo.Count > 40) _undo.RemoveAt(0);
        _redo.Clear();
        _changing = true;
    }

    void EndChange() => _changing = false;

    void Restore(List<Snapshot> pop, List<Snapshot> push)
    {
        if (pop.Count == 0 || Document == null || _changing) return;
        push.Add(new Snapshot(Document.Clone(), Selection?.Clone()));
        var snapshot = pop[^1];
        pop.RemoveAt(pop.Count - 1);
        Document = snapshot.Document;
        Selection = snapshot.Selection;
        Dirty = true;
        Invalidate();
        Structure++;
        StatusText = pop == _undo ? "Undo" : "Redo";
        Changed?.Invoke();
    }

    void ResetViewState()
    {
        Selection = null;
        Dirty = false;
        _undo.Clear();
        _redo.Clear();
        _changing = false;
        CropRect = null;
        FitRequested = true;
        Zoom = 1;
        Invalidate();
        Structure++;
    }

    void Invalidate() => _compositeDirty = true;

    void Publish()
    {
        Invalidate();
        Structure++;
        Changed?.Invoke();
    }

    PixelBuffer? CopyActiveLayer()
    {
        var layer = Document?.ActiveLayer;
        if (layer?.Pixels == null || Document == null) return null;
        if (Selection == null || Selection.IsEmpty())
        {
            _clipboardOrigin = ((int)Math.Round(layer.Transform.OriginX), (int)Math.Round(layer.Transform.OriginY));
            return layer.Pixels.Clone();
        }
        var flat = new PixelBuffer(Document.Width, Document.Height);
        var bounds = CompositorEngine.Bounds(layer.Transform, Document.Width, Document.Height);
        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            for (int x = bounds.X; x < bounds.Right; x++)
            {
                if (!layer.Transform.TryUnit(x + 0.5, y + 0.5, out double u, out double v)) continue;
                int sx = Math.Clamp((int)(u * layer.Pixels.Width), 0, layer.Pixels.Width - 1);
                int sy = Math.Clamp((int)(v * layer.Pixels.Height), 0, layer.Pixels.Height - 1);
                flat.Set(x, y, layer.Pixels.Get(sx, sy));
            }
        }
        _clipboardOrigin = Selection.Bounds() is { } box ? (box.X, box.Y) : (0, 0);
        return SelectionEdits.Copy(flat, Selection);
    }
}

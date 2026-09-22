namespace Compositor;

public sealed partial class EditorSession
{
    enum DragKind { None, Pan, Marquee, Lasso, Brush, Clone, Heal, Blur, Crop, Shape, Gradient, Move, Scale, Rotate, MoveSelection, Guide }

    DragKind _drag;
    double _lastX, _lastY, _grabX, _grabY;
    bool _shift, _alt;
    LayerTransform _startTransform;
    int _scaleHandle;
    SelectionCombine _combine;
    SelectionMask? _selectionBefore;
    List<(double X, double Y)>? _lasso;
    PixelBuffer? _strokeSource;
    bool _hasCloneSource;
    double _cloneX, _cloneY;
    bool _guideHorizontal;
    double _panAnchorX, _panAnchorY, _panStartX, _panStartY;

    public void PointerDown(double x, double y, double deviceX, double deviceY, bool shift, bool alt, bool doubleClick)
    {
        if (Document == null) return;
        _shift = shift;
        _alt = alt;
        _lastX = _grabX = x;
        _lastY = _grabY = y;
        if (SpacePan || Tool == EditorTool.Hand)
        {
            _drag = DragKind.Pan;
            _panAnchorX = deviceX;
            _panAnchorY = deviceY;
            _panStartX = PanX;
            _panStartY = PanY;
            return;
        }
        switch (Tool)
        {
            case EditorTool.Zoom:
                ZoomBy(alt ? 1 / 1.25 : 1.25, deviceX, deviceY);
                break;
            case EditorTool.Eyedropper:
                Pick(x, y, alt);
                break;
            case EditorTool.Type:
                TextRequested?.Invoke(x, y);
                break;
            case EditorTool.Marquee:
                StartMarquee(x, y, shift, alt);
                break;
            case EditorTool.Lasso:
                StartLasso(x, y, shift, alt);
                break;
            case EditorTool.Wand:
                Wand(x, y, shift, alt);
                break;
            case EditorTool.Crop:
                _drag = DragKind.Crop;
                CropRect = ((int)x, (int)y, 0, 0);
                Painted?.Invoke();
                break;
            case EditorTool.Brush:
            case EditorTool.SpotHealing:
            case EditorTool.Blur:
                StartPaint(Tool == EditorTool.SpotHealing ? DragKind.Heal : Tool == EditorTool.Blur ? DragKind.Blur : DragKind.Brush, x, y);
                break;
            case EditorTool.CloneStamp:
                if (alt) { _hasCloneSource = true; _cloneX = x; _cloneY = y; StatusText = "Clone source set"; Painted?.Invoke(); }
                else StartPaint(DragKind.Clone, x, y);
                break;
            case EditorTool.Gradient:
                _drag = DragKind.Gradient;
                GradientLine = (x, y, x, y);
                Painted?.Invoke();
                break;
            case EditorTool.Shape:
                _drag = DragKind.Shape;
                ShapeRect = (x, y, 0, 0);
                Painted?.Invoke();
                break;
            default:
                StartMove(x, y);
                break;
        }
        _ = doubleClick;
    }

    public void PointerMove(double x, double y, double deviceX, double deviceY, bool shift)
    {
        if (Document == null || _drag == DragKind.None) return;
        _shift = shift;
        switch (_drag)
        {
            case DragKind.Pan:
                PanX = _panStartX + (deviceX - _panAnchorX);
                PanY = _panStartY + (deviceY - _panAnchorY);
                Painted?.Invoke();
                return;
            case DragKind.Marquee:
                UpdateMarquee(x, y);
                return;
            case DragKind.Lasso when !PolygonalLasso:
                if (Math.Abs(x - _lastX) + Math.Abs(y - _lastY) > 1.5) { _lasso!.Add((x, y)); _lastX = x; _lastY = y; }
                Painted?.Invoke();
                return;
            case DragKind.Brush:
                Stroke(x, y, (px, py) => PaintAt(px, py));
                return;
            case DragKind.Heal:
                Stroke(x, y, (px, py) => PixelEdits.HealDab(PaintTarget()!, Document, Selection, _strokeSource!, px, py, BrushSize, BrushHardness / 100f));
                AfterDab();
                return;
            case DragKind.Blur:
                Stroke(x, y, (px, py) => PixelEdits.BlurDab(PaintTarget()!, px, py, Math.Max(1, BrushSize / 3), BrushOpacity / 100f, Selection));
                AfterDab();
                return;
            case DragKind.Clone when _hasCloneSource && _strokeSource != null:
                Stroke(x, y, (px, py) => PixelEdits.CloneDab(PaintTarget()!, Document, Selection, _strokeSource, px, py, _cloneX + (px - _grabX), _cloneY + (py - _grabY), BrushSize, BrushHardness / 100f, BrushOpacity / 100f));
                AfterDab();
                return;
            case DragKind.Crop:
                CropRect = NormRect(_grabX, _grabY, x, y);
                Painted?.Invoke();
                return;
            case DragKind.Gradient:
                var end = Constrain(_grabX, _grabY, x, y, shift);
                GradientLine = (_grabX, _grabY, end.X, end.Y);
                Painted?.Invoke();
                return;
            case DragKind.Shape:
                ShapeRect = NormDouble(_grabX, _grabY, x, y, shift);
                Painted?.Invoke();
                return;
            case DragKind.Move:
                MoveLayer(x, y);
                return;
            case DragKind.Scale:
                ScaleLayer(x, y);
                return;
            case DragKind.Rotate:
                RotateLayer(x, y);
                return;
            case DragKind.MoveSelection when Selection != null && _selectionBefore != null:
                Selection = SelectionEdits.Translate(_selectionBefore, (int)Math.Round(x - _grabX), (int)Math.Round(y - _grabY));
                Painted?.Invoke();
                return;
            case DragKind.Guide:
                if (Document.Guides.Count > 0)
                    Document.Guides[^1].Position = _guideHorizontal ? y : x;
                Painted?.Invoke();
                return;
        }
    }

    public void PointerUp(double x, double y)
    {
        if (Document == null) return;
        switch (_drag)
        {
            case DragKind.Marquee:
                CommitMarquee(x, y);
                break;
            case DragKind.Lasso when !PolygonalLasso:
                _lasso!.Add((x, y));
                CommitLasso();
                break;
            case DragKind.Brush:
            case DragKind.Heal:
            case DragKind.Blur:
            case DragKind.Clone:
                EndChange();
                Dirty = true;
                Structure++;
                Changed?.Invoke();
                break;
            case DragKind.Gradient:
                CommitGradient(x, y);
                break;
            case DragKind.Shape:
                CommitShape(x, y);
                break;
            case DragKind.Move:
            case DragKind.Scale:
            case DragKind.Rotate:
            case DragKind.Guide:
                EndChange();
                Dirty = true;
                Changed?.Invoke();
                break;
        }
        if (_drag is not DragKind.Lasso || !PolygonalLasso) _drag = DragKind.None;
        _strokeSource = null;
    }

    public void CloseLasso()
    {
        if (_lasso == null || Document == null) return;
        CommitLasso();
        _drag = DragKind.None;
    }

    public void StartGuide(bool horizontal, double position)
    {
        if (Document == null) return;
        BeginChange();
        _drag = DragKind.Guide;
        _guideHorizontal = horizontal;
        Document.Guides.Add(new Guide { Horizontal = horizontal, Position = position });
        ShowGuides = true;
        Dirty = true;
        Painted?.Invoke();
    }

    void StartMarquee(double x, double y, bool shift, bool alt)
    {
        _combine = shift ? SelectionCombine.Add : alt ? SelectionCombine.Subtract : SelectionCombine.Replace;
        if (_combine == SelectionCombine.Replace && Selection != null && Selection.Coverage((int)x, (int)y) > 0)
        {
            _drag = DragKind.MoveSelection;
            _selectionBefore = Selection.Clone();
            return;
        }
        _drag = DragKind.Marquee;
        _selectionBefore = Selection?.Clone();
        MarqueeRect = (x, y, 0, 0);
        Painted?.Invoke();
    }

    void UpdateMarquee(double x, double y)
    {
        var rect = NormDouble(_grabX, _grabY, x, y, _shift);
        MarqueeRect = rect;
        ApplyMarquee(rect);
    }

    void CommitMarquee(double x, double y)
    {
        var rect = NormDouble(_grabX, _grabY, x, y, _shift);
        ApplyMarquee(rect);
        MarqueeRect = null;
        if (Selection != null && Selection.IsEmpty()) Selection = null;
        Painted?.Invoke();
    }

    void ApplyMarquee((double X, double Y, double Width, double Height) rect)
    {
        if (Document == null) return;
        Selection ??= new SelectionMask(Document.Width, Document.Height);
        if (Selection.Width != Document.Width || Selection.Height != Document.Height)
            Selection = new SelectionMask(Document.Width, Document.Height);
        SelectionEdits.Rectangle(Selection, (int)Math.Round(rect.X), (int)Math.Round(rect.Y),
            (int)Math.Round(rect.X + rect.Width), (int)Math.Round(rect.Y + rect.Height), _combine, EllipseMarquee, _selectionBefore);
        Painted?.Invoke();
    }

    void StartLasso(double x, double y, bool shift, bool alt)
    {
        _combine = shift ? SelectionCombine.Add : alt ? SelectionCombine.Subtract : SelectionCombine.Replace;
        _selectionBefore = Selection?.Clone();
        _lasso = [(x, y)];
        _drag = DragKind.Lasso;
        Painted?.Invoke();
    }

    void CommitLasso()
    {
        if (Document == null || _lasso == null || _lasso.Count < 3) { _lasso = null; return; }
        Selection ??= new SelectionMask(Document.Width, Document.Height);
        SelectionEdits.Polygon(Selection, _lasso, _combine, _selectionBefore);
        _lasso = null;
        if (Selection.IsEmpty()) Selection = null;
        Painted?.Invoke();
    }

    void Wand(double x, double y, bool shift, bool alt)
    {
        var image = Composite();
        if (image == null || Document == null) return;
        var combine = shift ? SelectionCombine.Add : alt ? SelectionCombine.Subtract : SelectionCombine.Replace;
        Selection = SelectionEdits.MagicWand(image, (int)Math.Floor(x), (int)Math.Floor(y), Tolerance, Contiguous, combine, Selection);
        if (Selection.IsEmpty()) Selection = null;
        Painted?.Invoke();
    }

    void StartPaint(DragKind kind, double x, double y)
    {
        var layer = PaintTarget();
        if (layer == null) return;
        BeginChange();
        _strokeSource = layer.Pixels?.Clone();
        _drag = kind;
        if (kind == DragKind.Brush) PaintAt(x, y);
        else if (kind == DragKind.Heal && _strokeSource != null) PixelEdits.HealDab(layer, Document!, Selection, _strokeSource, x, y, BrushSize, BrushHardness / 100f);
        else if (kind == DragKind.Blur) PixelEdits.BlurDab(layer, x, y, Math.Max(1, BrushSize / 3), BrushOpacity / 100f, Selection);
        else if (kind == DragKind.Clone && _hasCloneSource && _strokeSource != null)
            PixelEdits.CloneDab(layer, Document!, Selection, _strokeSource, x, y, _cloneX, _cloneY, BrushSize, BrushHardness / 100f, BrushOpacity / 100f);
        AfterDab();
    }

    void PaintAt(double x, double y)
    {
        var layer = PaintTarget();
        if (layer == null || Document == null) return;
        if (PaintMask && Document.ActiveLayer?.Mask != null)
        {
            byte luminance = (byte)Math.Clamp((int)Math.Round(0.2126 * Foreground.R + 0.7152 * Foreground.G + 0.0722 * Foreground.B), 0, 255);
            var color = Erase ? Rgba.White : new Rgba(luminance, luminance, luminance, 255);
            PixelEdits.PaintDab(layer, Document, Selection, x, y, BrushSize, BrushHardness / 100f, BrushOpacity / 100f, color, erase: false);
        }
        else
        {
            PixelEdits.PaintDab(layer, Document, Selection, x, y, BrushSize, BrushHardness / 100f, BrushOpacity / 100f, Foreground, Erase);
        }
    }

    Layer? PaintTarget()
    {
        var layer = Document?.ActiveLayer;
        if (layer == null || layer.IsGroup) return null;
        if (PaintMask && layer.Mask != null)
        {
            return new Layer
            {
                Pixels = layer.Mask,
                Transform = layer.MaskLinked || layer.MaskPlacement == null ? layer.Transform : layer.MaskPlacement.Value
            };
        }
        layer.Pixels ??= new PixelBuffer(Math.Max(1, (int)Math.Round(layer.Transform.Width)), Math.Max(1, (int)Math.Round(layer.Transform.Height)));
        return layer;
    }

    void StartMove(double x, double y)
    {
        if (Document == null) return;
        var active = Document.ActiveLayer;
        int handle = HitHandle(x, y);
        if (handle == 8 && active != null)
        {
            BeginChange();
            _drag = DragKind.Rotate;
            _startTransform = active.Transform;
            return;
        }
        if (handle >= 0 && active != null)
        {
            BeginChange();
            _drag = DragKind.Scale;
            _scaleHandle = handle;
            _startTransform = active.Transform;
            return;
        }
        var hit = active != null && !active.IsGroup && active.Transform.Contains(x, y) ? active : TopLayerAt(x, y);
        if (hit == null || hit.IsGroup) return;
        Document.ActiveLayerId = hit.Id;
        BeginChange();
        _drag = DragKind.Move;
        _startTransform = hit.Transform;
        _grabX = x - hit.Transform.OriginX;
        _grabY = y - hit.Transform.OriginY;
        Painted?.Invoke();
    }

    void MoveLayer(double x, double y)
    {
        if (Document?.ActiveLayer is not { } layer) return;
        var transform = layer.Transform;
        transform.OriginX = x - _grabX;
        transform.OriginY = y - _grabY;
        if (SnapEnabled) SnapMove(ref transform);
        layer.Transform = Round(transform);
        Dirty = true;
        Invalidate();
        Painted?.Invoke();
    }

    void ScaleLayer(double x, double y)
    {
        if (Document?.ActiveLayer is not { } layer) return;
        var transform = _startTransform;
        if (Math.Abs(transform.Rotation) > 0.5)
        {
            double start = Math.Max(1, Math.Sqrt(Math.Pow(_grabX - transform.CenterX, 2) + Math.Pow(_grabY - transform.CenterY, 2)));
            double now = Math.Max(1, Math.Sqrt(Math.Pow(x - transform.CenterX, 2) + Math.Pow(y - transform.CenterY, 2)));
            double scale = now / start;
            transform.Width = Math.Max(1, _startTransform.Width * scale);
            transform.Height = Math.Max(1, _startTransform.Height * scale);
            transform.OriginX = _startTransform.CenterX - transform.Width / 2;
            transform.OriginY = _startTransform.CenterY - transform.Height / 2;
        }
        else
        {
            double left = _startTransform.OriginX;
            double top = _startTransform.OriginY;
            double right = left + _startTransform.Width;
            double bottom = top + _startTransform.Height;
            if (_scaleHandle is 0 or 3 or 4) left = Math.Min(x, right - 1);
            if (_scaleHandle is 1 or 2 or 5) right = Math.Max(x, left + 1);
            if (_scaleHandle is 0 or 1 or 6) top = Math.Min(y, bottom - 1);
            if (_scaleHandle is 2 or 3 or 7) bottom = Math.Max(y, top + 1);
            if (_shift)
            {
                double size = Math.Max(right - left, bottom - top);
                right = left + size;
                bottom = top + size;
            }
            transform.OriginX = left;
            transform.OriginY = top;
            transform.Width = Math.Max(1, right - left);
            transform.Height = Math.Max(1, bottom - top);
        }
        layer.Transform = Round(transform);
        Dirty = true;
        Invalidate();
        Painted?.Invoke();
    }

    void RotateLayer(double x, double y)
    {
        if (Document?.ActiveLayer is not { } layer) return;
        var transform = _startTransform;
        double angle = Math.Atan2(y - transform.CenterY, x - transform.CenterX) * 180 / Math.PI;
        double start = Math.Atan2(_grabY - transform.CenterY, _grabX - transform.CenterX) * 180 / Math.PI;
        transform.Rotation = _startTransform.Rotation + (angle - start);
        if (_shift) transform.Rotation = Math.Round(transform.Rotation / 15) * 15;
        layer.Transform = transform;
        Dirty = true;
        Invalidate();
        Painted?.Invoke();
    }

    void CommitGradient(double x, double y)
    {
        var end = Constrain(_grabX, _grabY, x, y, _shift);
        var layer = Document?.ActiveLayer;
        if (layer != null && Document != null)
        {
            var transparent = Foreground with { A = 0 };
            Edit(() => PixelEdits.ApplyLinearGradient(layer, Document, Selection, _grabX, _grabY, end.X, end.Y, Foreground, _alt ? transparent : Background));
        }
        GradientLine = null;
        _drag = DragKind.None;
    }

    void CommitShape(double x, double y)
    {
        var rect = NormDouble(_grabX, _grabY, x, y, _shift);
        ShapeRect = null;
        if (Document == null || rect.Width < 1 || rect.Height < 1) { _drag = DragKind.None; return; }
        int w = Math.Max(1, (int)Math.Round(rect.Width));
        int h = Math.Max(1, (int)Math.Round(rect.Height));
        var pixels = PixelEdits.CreateShape(w, h, Shape, Foreground);
        Edit(() => Document.AddPixelLayer(Shape.ToString(), pixels, rect.X, rect.Y));
        _drag = DragKind.None;
    }

    void Pick(double x, double y, bool background)
    {
        var flat = Composite();
        if (flat == null || !flat.Contains((int)x, (int)y)) return;
        var color = flat.Get((int)x, (int)y);
        if (background) Background = color; else Foreground = color with { A = 255 };
        Changed?.Invoke();
    }

    int HitHandle(double x, double y)
    {
        if (Tool != EditorTool.Move || Document?.ActiveLayer is not { IsGroup: false } layer) return -1;
        double reach = 8 / Math.Max(Zoom, 0.05);
        var points = HandlePoints(layer.Transform);
        for (int i = 0; i < points.Length; i++)
            if (Math.Abs(points[i].X - x) <= reach && Math.Abs(points[i].Y - y) <= reach) return i;
        return -1;
    }

    public (double X, double Y)[] ActiveHandles() =>
        Document?.ActiveLayer is { IsGroup: false } layer && Tool == EditorTool.Move ? HandlePoints(layer.Transform) : [];

    static (double X, double Y)[] HandlePoints(LayerTransform transform)
    {
        var corners = new (double X, double Y)[9];
        (double u, double v)[] units = [(0, 0), (1, 0), (1, 1), (0, 1), (0, 0.5), (1, 0.5), (0.5, 0), (0.5, 1), (0.5, -0.18)];
        for (int i = 0; i < units.Length; i++) corners[i] = transform.ToDocument(units[i].u, units[i].v);
        return corners;
    }

    Layer? TopLayerAt(double x, double y)
    {
        if (Document == null) return null;
        for (int i = Document.Layers.Count - 1; i >= 0; i--)
        {
            var layer = Document.Layers[i];
            if (!layer.IsGroup && Document.EffectivelyVisible(layer) && layer.Transform.Contains(x, y)) return layer;
        }
        return null;
    }

    void SnapMove(ref LayerTransform transform)
    {
        if (Document == null) return;
        double tol = 6 / Math.Max(Zoom, 0.05);
        var xs = new List<double> { 0, Document.Width };
        var ys = new List<double> { 0, Document.Height };
        foreach (var guide in Document.Guides)
        {
            if (guide.Horizontal) ys.Add(guide.Position);
            else xs.Add(guide.Position);
        }
        SnapEdge(ref transform, xs, horizontal: true, tol);
        SnapEdge(ref transform, ys, horizontal: false, tol);
    }

    static void SnapEdge(ref LayerTransform transform, List<double> targets, bool horizontal, double tolerance)
    {
        double origin = horizontal ? transform.OriginX : transform.OriginY;
        double size = horizontal ? transform.Width : transform.Height;
        double[] edges = [origin, origin + size / 2, origin + size];
        double[] anchors = [0, size / 2, size];
        foreach (var edge in edges)
        {
            foreach (var target in targets)
            {
                if (Math.Abs(edge - target) > tolerance) continue;
                if (horizontal) transform.OriginX = target - (edge - origin);
                else transform.OriginY = target - (edge - origin);
                return;
            }
        }
        _ = anchors;
    }

    void Stroke(double x, double y, Action<double, double> dab)
    {
        double dx = x - _lastX, dy = y - _lastY;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        int steps = Math.Max(1, (int)(distance / Math.Max(1, BrushSize / 5.0)));
        for (int i = 1; i <= steps; i++) dab(_lastX + dx * i / steps, _lastY + dy * i / steps);
        _lastX = x;
        _lastY = y;
        AfterDab();
    }

    void AfterDab()
    {
        Dirty = true;
        Invalidate();
        Painted?.Invoke();
    }

    void CancelDrag() => _drag = DragKind.None;

    static LayerTransform Round(LayerTransform transform)
    {
        transform.OriginX = Math.Round(transform.OriginX);
        transform.OriginY = Math.Round(transform.OriginY);
        transform.Width = Math.Max(1, Math.Round(transform.Width));
        transform.Height = Math.Max(1, Math.Round(transform.Height));
        transform.Rotation = Math.Round(transform.Rotation);
        return transform;
    }

    static (int X, int Y, int Width, int Height) NormRect(double x0, double y0, double x1, double y1)
    {
        int left = (int)Math.Floor(Math.Min(x0, x1));
        int top = (int)Math.Floor(Math.Min(y0, y1));
        int right = (int)Math.Ceiling(Math.Max(x0, x1));
        int bottom = (int)Math.Ceiling(Math.Max(y0, y1));
        return (left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    static (double X, double Y, double Width, double Height) NormDouble(double x0, double y0, double x1, double y1, bool square)
    {
        if (square)
        {
            double size = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            x1 = x0 + Math.Sign(x1 - x0) * size;
            y1 = y0 + Math.Sign(y1 - y0) * size;
        }
        double left = Math.Min(x0, x1), top = Math.Min(y0, y1);
        return (left, top, Math.Abs(x1 - x0), Math.Abs(y1 - y0));
    }

    static (double X, double Y) Constrain(double x0, double y0, double x1, double y1, bool shift)
    {
        if (!shift) return (x1, y1);
        double angle = Math.Atan2(y1 - y0, x1 - x0);
        double snapped = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
        double length = Math.Sqrt(Math.Pow(x1 - x0, 2) + Math.Pow(y1 - y0, 2));
        return (x0 + Math.Cos(snapped) * length, y0 + Math.Sin(snapped) * length);
    }
}

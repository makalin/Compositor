using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Compositor;

public sealed class EditorCanvas : FrameworkElement
{
    public EditorSession? Session { get; set; }
    WriteableBitmap? _bitmap;
    PixelBuffer? _cachedFlat;
    SelectionMask? _cachedSelection;
    int _cachedSelectionChange = -1;
    static readonly Brush CheckerBrush = CreateChecker();

    public EditorCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public (double Width, double Height) DeviceSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return (ActualWidth * dpi.DpiScaleX, ActualHeight * dpi.DpiScaleY);
    }

    public (double X, double Y) ViewCenter()
    {
        if (Session?.Document == null) return (0, 0);
        var (w, h) = DeviceSize();
        return ((w / 2 - Session.PanX) / Session.Zoom, (h / 2 - Session.PanY) / Session.Zoom);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var session = Session;
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(36, 36, 36)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (session?.Document == null) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        double ruler = session.ShowRulers ? 18 : 0;
        UpdateBitmap(session);
        double dip = 1 / dpi.DpiScaleX;
        var dest = new Rect((session.PanX + 0) * dip, session.PanY * dip, session.Document.Width * session.Zoom * dip, session.Document.Height * session.Zoom * dip);
        context.DrawRectangle(CheckerBrush, null, dest);
        if (_bitmap != null)
        {
            context.PushTransform(new TranslateTransform(dest.X, dest.Y));
            context.PushTransform(new ScaleTransform(session.Zoom * dip, session.Zoom * dip));
            context.DrawImage(_bitmap, new Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight));
            context.Pop();
            context.Pop();
        }
        DrawOverlays(context, session, dpi.DpiScaleX);
        if (session.ShowRulers) DrawRulers(context, session, ruler, dpi.DpiScaleX);
    }

    void UpdateBitmap(EditorSession session)
    {
        var flat = session.Composite();
        if (flat == null) { _bitmap = null; _cachedFlat = null; return; }
        var selection = session.Selection;
        int change = selection?.ChangeCount ?? 0;
        if (_bitmap != null && ReferenceEquals(flat, _cachedFlat) && ReferenceEquals(selection, _cachedSelection) && change == _cachedSelectionChange
            && _bitmap.PixelWidth == flat.Width && _bitmap.PixelHeight == flat.Height)
            return;
        _cachedFlat = flat;
        _cachedSelection = selection;
        _cachedSelectionChange = change;
        if (_bitmap == null || _bitmap.PixelWidth != flat.Width || _bitmap.PixelHeight != flat.Height)
            _bitmap = new WriteableBitmap(flat.Width, flat.Height, 96, 96, PixelFormats.Bgra32, null);
        var bgra = new byte[flat.Rgba.Length];
        for (int y = 0, i = 0; y < flat.Height; y++)
        {
            for (int x = 0; x < flat.Width; x++, i += 4)
            {
                byte r = flat.Rgba[i], g = flat.Rgba[i + 1], b = flat.Rgba[i + 2], a = flat.Rgba[i + 3];
                if (selection != null && selection.Coverage(x, y) > 0)
                {
                    r = (byte)(r * 0.75 + 80);
                    g = (byte)(g * 0.75 + 120);
                    b = (byte)Math.Min(255, b * 0.75 + 180);
                }
                bgra[i] = b;
                bgra[i + 1] = g;
                bgra[i + 2] = r;
                bgra[i + 3] = a;
            }
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, flat.Width, flat.Height), bgra, flat.Width * 4, 0);
    }

    static Brush CreateChecker()
    {
        var dark = new SolidColorBrush(Color.FromRgb(46, 46, 46));
        var light = new SolidColorBrush(Color.FromRgb(58, 58, 58));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        group.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        group.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 16, 16),
            ViewboxUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    void DrawOverlays(DrawingContext context, EditorSession session, double dpi)
    {
        var pen = new Pen(Brushes.White, 1);
        pen.Freeze();
        if (session.ShowGrid && session.Zoom * 64 >= 8)
        {
            var grid = new Pen(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), 1);
            for (int x = 0; x <= session.Document!.Width; x += 64) Line(context, session, dpi, x, 0, x, session.Document.Height, grid);
            for (int y = 0; y <= session.Document.Height; y += 64) Line(context, session, dpi, 0, y, session.Document.Width, y, grid);
        }
        if (session.ShowGuides)
        {
            var guide = new Pen(new SolidColorBrush(Color.FromRgb(120, 190, 255)), 1);
            foreach (var item in session.Document!.Guides)
            {
                if (item.Horizontal) Line(context, session, dpi, 0, item.Position, session.Document.Width, item.Position, guide);
                else Line(context, session, dpi, item.Position, 0, item.Position, session.Document.Height, guide);
            }
        }
        if (session.Document?.ActiveLayer is { IsGroup: false } layer && session.Tool == EditorTool.Move)
        {
            var corners = layer.Transform.Corners();
            for (int i = 0; i < 4; i++)
                Line(context, session, dpi, corners[i].X, corners[i].Y, corners[(i + 1) % 4].X, corners[(i + 1) % 4].Y, pen);
            foreach (var handle in session.ActiveHandles())
            {
                var p = ToDip(session, dpi, handle.X, handle.Y);
                context.DrawRectangle(Brushes.White, null, new Rect(p.X - 4, p.Y - 4, 8, 8));
            }
        }
        if (session.CropRect is { } crop)
            RectDip(context, session, dpi, crop.X, crop.Y, crop.Width, crop.Height, new Pen(Brushes.White, 1) { DashStyle = DashStyles.Dash });
        if (session.MarqueeRect is { } marquee)
            RectDip(context, session, dpi, marquee.X, marquee.Y, marquee.Width, marquee.Height, pen);
        if (session.ShapeRect is { } shape)
            RectDip(context, session, dpi, shape.X, shape.Y, shape.Width, shape.Height, pen);
        if (session.LassoPoints is { Count: > 1 } points)
        {
            for (int i = 1; i < points.Count; i++)
                Line(context, session, dpi, points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y, pen);
        }
        if (session.GradientLine is { } gradient)
            Line(context, session, dpi, gradient.X0, gradient.Y0, gradient.X1, gradient.Y1, pen);
        if (session.Hovering && session.Tool is EditorTool.Brush or EditorTool.SpotHealing or EditorTool.CloneStamp or EditorTool.Blur)
        {
            var c = ToDip(session, dpi, session.HoverX, session.HoverY);
            double radius = session.BrushSize * session.Zoom / dpi;
            context.DrawEllipse(null, pen, c, radius, radius);
        }
    }

    void DrawRulers(DrawingContext context, EditorSession session, double ruler, double dpi)
    {
        var bg = new SolidColorBrush(Color.FromRgb(28, 28, 28));
        context.DrawRectangle(bg, null, new Rect(0, 0, ActualWidth, ruler / dpi));
        context.DrawRectangle(bg, null, new Rect(0, 0, ruler / dpi, ActualHeight));
        var tick = new Pen(new SolidColorBrush(Color.FromRgb(160, 160, 160)), 1);
        double step = session.Zoom >= 4 ? 10 : session.Zoom >= 0.5 ? 50 : 100;
        for (double x = 0; x < session.Document!.Width; x += step)
        {
            var p = ToDip(session, dpi, x, 0);
            context.DrawLine(tick, new Point(p.X, 4), new Point(p.X, ruler / dpi));
            if (x % (step * 2) == 0)
                context.DrawText(Label(x.ToString(CultureInfo.InvariantCulture)), new Point(p.X + 2, 0));
        }
        for (double y = 0; y < session.Document.Height; y += step)
        {
            var p = ToDip(session, dpi, 0, y);
            context.DrawLine(tick, new Point(4, p.Y), new Point(ruler / dpi, p.Y));
        }
    }

    static FormattedText Label(string text) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        new Typeface("Segoe UI"), 9, Brushes.Silver, 1);

    void Line(DrawingContext context, EditorSession session, double dpi, double x0, double y0, double x1, double y1, Pen pen)
    {
        context.DrawLine(pen, ToDip(session, dpi, x0, y0), ToDip(session, dpi, x1, y1));
    }

    void RectDip(DrawingContext context, EditorSession session, double dpi, double x, double y, double w, double h, Pen pen)
    {
        var a = ToDip(session, dpi, x, y);
        var b = ToDip(session, dpi, x + w, y + h);
        context.DrawRectangle(null, pen, new Rect(a, b));
    }

    static Point ToDip(EditorSession session, double dpi, double docX, double docY) =>
        new((session.PanX + docX * session.Zoom) / dpi, (session.PanY + docY * session.Zoom) / dpi);

    (double X, double Y, double DeviceX, double DeviceY) Map(Point dip)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double deviceX = dip.X * dpi.DpiScaleX;
        double deviceY = dip.Y * dpi.DpiScaleY;
        if (Session == null) return (0, 0, deviceX, deviceY);
        return ((deviceX - Session.PanX) / Session.Zoom, (deviceY - Session.PanY) / Session.Zoom, deviceX, deviceY);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (Session?.Document == null) return;
        CaptureMouse();
        var (x, y, dx, dy) = Map(e.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);
        if (Session.ShowRulers && (e.GetPosition(this).Y * dpi.DpiScaleY < 18 || e.GetPosition(this).X * dpi.DpiScaleX < 18))
        {
            bool horizontal = e.GetPosition(this).Y * dpi.DpiScaleY < 18 && e.GetPosition(this).X * dpi.DpiScaleX >= 18;
            Session.StartGuide(horizontal, horizontal ? y : x);
            return;
        }
        Session.PointerDown(x, y, dx, dy, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), Keyboard.Modifiers.HasFlag(ModifierKeys.Alt), e.ClickCount > 1);
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Session?.Document == null) return;
        var (x, y, dx, dy) = Map(e.GetPosition(this));
        bool inside = x >= 0 && y >= 0 && x < Session.Document.Width && y < Session.Document.Height;
        if (e.LeftButton == MouseButtonState.Pressed)
            Session.PointerMove(x, y, dx, dy, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        Session.Hover(x, y, inside);
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        ReleaseMouseCapture();
        if (Session?.Document == null) return;
        var (x, y, _, _) = Map(e.GetPosition(this));
        Session.PointerUp(x, y);
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Session?.Document == null) return;
        var (_, _, dx, dy) = Map(e.GetPosition(this));
        Session.ZoomBy(e.Delta > 0 ? 1.1 : 1 / 1.1, dx, dy);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Session?.Hover(0, 0, false);
        InvalidateVisual();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Compositor;

/// <summary>Tool-rail and panel icons drawn to match the Mac app's SF Symbols.</summary>
public static class ChromeIcons
{
    static readonly Brush Ink = Brushes.White;

    public static UIElement For(EditorTool tool, bool erase = false, bool polygonal = false) => tool switch
    {
        EditorTool.Move => Stroke("M10.2,7.2 L4.2,4.2 M4.2,4.2 L4.2,8 M4.2,4.2 L8,4.2 M7.8,10.8 L13.8,13.8 M13.8,13.8 L13.8,10 M13.8,13.8 L10,13.8"),
        EditorTool.Marquee => Stroke("M3.5,4.5 H14.5 V13.5 H3.5 Z", dash: true),
        EditorTool.Lasso => polygonal
            ? Stroke("M1.2,7 L4,2.4 L11.8,1.8 L16.8,5.2 L15.6,10.4 L7,11.6 Z M8.9,10.9 L13.3,10.5 L11.6,14.5 Z M11.6,14.5 L12.9,17.3")
            : Stroke("M6,15 C3,15 2,12 3,9 C4,5 7,2 11,3 C15,4 16,8 14,11 C13,13 12,13 11,12 C10,14 8,16 6,15 M11,12 C12,14 13,16 14,16"),
        EditorTool.Wand => Stroke("M3.5,14.5 L11,7 M12.2,3.2 L13.1,5.2 L15.2,5.4 L13.6,6.7 L14,8.8 L12.2,7.6 L10.4,8.8 L10.8,6.7 L9.2,5.4 L11.3,5.2 Z"),
        EditorTool.Crop => Stroke("M3,6.5 V3.5 H6.5 M11.5,3.5 H14.5 V6.5 M14.5,11.5 V14.5 H11.5 M6.5,14.5 H3.5 V11.5"),
        EditorTool.Brush => erase
            ? Fill("M4.2,5.2 L13.2,8.4 L11.4,14.6 L2.4,11.4 Z")
            : Fill("M3.2,14.6 L6.4,16.2 L15.4,5.6 L12.4,3.4 L10.6,5.2 L4.6,12.4 Z"),
        EditorTool.SpotHealing => Stroke("M4,6.5 H14 V11.5 H4 Z M9,7.6 V10.4 M7.2,9 H10.8"),
        EditorTool.CloneStamp => CloneStamp(),
        EditorTool.Blur => Fill("M9,2.2 C9,2.2 4.2,8 4.2,11.6 A4.8,4.8 0 1 0 13.8,11.6 C13.8,8 9,2.2 9,2.2 Z"),
        EditorTool.Gradient => Gradient(),
        EditorTool.Shape => Stroke("M3.5,9 A5.5,5.5 0 1 0 14.5,9 A5.5,5.5 0 1 0 3.5,9 M8,6.5 H15 V13.5 H8 Z"),
        EditorTool.Type => Stroke("M3.5,4.5 H14.5 M9,4.5 V15"),
        EditorTool.Eyedropper => Stroke("M11.5,3.2 L14.8,6.5 L8.2,13.1 L5.2,13.1 L5.2,10.1 Z M3.6,14.6 L6.2,12"),
        EditorTool.Hand => Stroke("M8.2,15.2 V8.2 M8.2,9.2 C8.2,6.4 6,6.2 5.6,8.2 V11.4 M8.2,8 C8.2,5.4 10.4,5.2 11,7.2 V11.2 M11,8.2 C11,6.2 13.2,6.2 13.4,8.4 V11.2 M5.6,11.4 C4.6,13 5.6,15.2 8.2,15.2 H12.2 C14.2,15.2 14.4,12.4 13.4,11.2"),
        EditorTool.Zoom => Stroke("M10.5,10.5 L15,15 M4,8.5 A4.6,4.6 0 1 0 13.2,8.5 A4.6,4.6 0 1 0 4,8.5"),
        _ => Stroke("M4,4 H14 V14 H4 Z")
    };

    public static UIElement PlusSquare() => Stroke("M3.5,3.5 H14.5 V14.5 H3.5 Z M9,6 V12 M6,9 H12");
    public static UIElement FolderPlus() => Stroke("M2.5,5.5 H7 L8.4,4 H15.5 V14 H2.5 Z M9,8 V12 M7,10 H11");
    public static UIElement Mask() => Stroke("M3.5,4.5 H14.5 V13.5 H3.5 Z M6.5,9 A2.5,2.5 0 1 0 11.5,9 A2.5,2.5 0 1 0 6.5,9");
    public static UIElement Trash() => Stroke("M4,6.5 H14 M6.2,6.5 V4.5 H11.8 V6.5 M5.5,6.5 L6.2,14.5 H11.8 L12.5,6.5");
    public static UIElement Eye(bool open) => open
        ? Stroke("M2,9 C4.5,5.5 13.5,5.5 16,9 C13.5,12.5 4.5,12.5 2,9 M7.2,9 A1.8,1.8 0 1 0 10.8,9 A1.8,1.8 0 1 0 7.2,9")
        : Stroke("M2,9 C4.5,5.5 13.5,5.5 16,9 C13.5,12.5 4.5,12.5 2,9 M3.5,4.5 L14.5,13.5");
    public static UIElement Chevron(bool up) => Stroke(up ? "M4,11 L9,6 L14,11" : "M4,7 L9,12 L14,7");
    public static UIElement Magnify(bool zoomIn) => Stroke(zoomIn
        ? "M10.2,10.2 L14.5,14.5 M3.5,8 A4.4,4.4 0 1 0 12.3,8 A4.4,4.4 0 1 0 3.5,8 M8,5.8 V10.2 M5.8,8 H10.2"
        : "M10.2,10.2 L14.5,14.5 M3.5,8 A4.4,4.4 0 1 0 12.3,8 A4.4,4.4 0 1 0 3.5,8 M5.8,8 H10.2");
    public static UIElement Plus() => Stroke("M9,3.5 V14.5 M3.5,9 H14.5");
    public static UIElement Swap() => Stroke("M2,5 H12 M9,2 L12,5 L9,8 M14,13 H4 M7,10 L4,13 L7,16", 12);
    public static UIElement Reset() => Stroke("M10,3.5 A6.2,6.2 0 1 1 4.2,6.5 M4,3.2 V7 H7.6", 12);

    public static string ToolLabel(EditorTool tool, bool erase) => tool switch
    {
        EditorTool.Move => "Move",
        EditorTool.Marquee => "Marquee",
        EditorTool.Lasso => "Lasso",
        EditorTool.Wand => "Magic",
        EditorTool.Crop => "Crop",
        EditorTool.Brush => erase ? "Eraser" : "Brush",
        EditorTool.SpotHealing => "Spot Healing",
        EditorTool.CloneStamp => "Clone Stamp",
        EditorTool.Blur => "Smear",
        EditorTool.Gradient => "Gradient",
        EditorTool.Shape => "Shape",
        EditorTool.Type => "Type",
        EditorTool.Eyedropper => "Eyedropper",
        EditorTool.Hand => "Hand",
        EditorTool.Zoom => "Zoom",
        _ => tool.ToString()
    };

    static UIElement Stroke(string data, double size = 18, bool dash = false)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data),
            Stroke = Ink,
            StrokeThickness = size < 16 ? 1.2 : 1.45,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform
        };
        if (dash) path.StrokeDashArray = new DoubleCollection { 1.1, 0.9 };
        return Box(path, size);
    }

    static UIElement Fill(string data)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data),
            Fill = Ink,
            Stretch = Stretch.Uniform
        };
        return Box(path, 18);
    }

    static UIElement CloneStamp()
    {
        var group = new GeometryGroup();
        group.Children.Add(new EllipseGeometry(new Point(9, 3.1), 3.1, 2.7));
        group.Children.Add(new RectangleGeometry(new Rect(7.7, 5, 2.6, 5)));
        group.Children.Add(new RectangleGeometry(new Rect(2.2, 9.7, 13.6, 3.6), 1.4, 1.4));
        group.Children.Add(new RectangleGeometry(new Rect(1.1, 14.2, 15.8, 2.2)));
        return Box(new Path { Data = group, Fill = Ink, Stretch = Stretch.Uniform }, 18);
    }

    static UIElement Gradient()
    {
        var canvas = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M1.5,1.5 H16.5 V16.5 H1.5 Z"),
            Stroke = Ink,
            StrokeThickness = 1.3
        };
        var dots = new GeometryGroup();
        const int n = 12;
        var ramp = new double[n, n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                ramp[y, x] = x / (double)(n - 1);
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                bool on = ramp[y, x] >= 0.5;
                double error = ramp[y, x] - (on ? 1 : 0);
                if (on) dots.Children.Add(new RectangleGeometry(new Rect(2 + x * 1.15, 2 + y * 1.15, 1.15, 1.15)));
                if (x + 1 < n) ramp[y, x + 1] += error * 7 / 16;
                if (y + 1 >= n) continue;
                if (x > 0) ramp[y + 1, x - 1] += error * 3 / 16;
                ramp[y + 1, x] += error * 5 / 16;
                if (x + 1 < n) ramp[y + 1, x + 1] += error / 16;
            }
        }
        var host = new Grid { Width = 18, Height = 18 };
        host.Children.Add(new Path { Data = dots, Fill = Ink });
        host.Children.Add(canvas);
        return new Viewbox { Width = 18, Height = 18, Child = host };
    }

    static Viewbox Box(Shape shape, double size) => new() { Width = size, Height = size, Child = shape };
}

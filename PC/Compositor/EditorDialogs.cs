using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Compositor;

public static class EditorDialogs
{
    public static bool NewCanvas(Window owner, out int width, out int height)
    {
        width = 1920;
        height = 1080;
        return Pair(owner, "New canvas", "A blank space for your next composition.", "Width", "Height", ref width, ref height);
    }

    public static bool CanvasSize(Window owner, ref int width, ref int height, out int anchor)
    {
        anchor = 0;
        int w = width, h = height, chosen = 0;
        var dialog = Create(owner, "Canvas Size", out var root);
        root.Children.Add(Field("Width", w.ToString(CultureInfo.InvariantCulture), out var widthBox));
        root.Children.Add(Field("Height", h.ToString(CultureInfo.InvariantCulture), out var heightBox));
        var anchors = new ComboBox { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var label in new[] { "Top left", "Top", "Top right", "Left", "Center", "Right", "Bottom left", "Bottom", "Bottom right" })
            anchors.Items.Add(label);
        anchors.SelectedIndex = 4;
        root.Children.Add(anchors);
        if (!Show(dialog, root, () => int.TryParse(widthBox.Text, out w) && int.TryParse(heightBox.Text, out h) && CanvasDocument.ValidDimension(w) && CanvasDocument.ValidDimension(h)))
            return false;
        width = w;
        height = h;
        anchor = anchors.SelectedIndex;
        chosen = anchor;
        _ = chosen;
        return true;
    }

    public static bool ImageSize(Window owner, ref int width, ref int height) =>
        Pair(owner, "Image Size", "Resamples every layer.", "Width", "Height", ref width, ref height);

    public static bool Levels(Window owner, out float inBlack, out float inWhite, out float gamma, out float outBlack, out float outWhite)
    {
        inBlack = 0; inWhite = 1; gamma = 1; outBlack = 0; outWhite = 1;
        var dialog = Create(owner, "Levels", out var root);
        var blacks = Slider(root, "Input black", 0, 255, 0);
        var whites = Slider(root, "Input white", 0, 255, 255);
        var gammas = Slider(root, "Gamma", 10, 300, 100);
        var outB = Slider(root, "Output black", 0, 255, 0);
        var outW = Slider(root, "Output white", 0, 255, 255);
        if (!Show(dialog, root, () => true)) return false;
        inBlack = (float)(blacks.Value / 255);
        inWhite = (float)(whites.Value / 255);
        gamma = (float)(gammas.Value / 100);
        outBlack = (float)(outB.Value / 255);
        outWhite = (float)(outW.Value / 255);
        return true;
    }

    public static bool HueSaturation(Window owner, out float hue, out float saturation, out float lightness)
    {
        hue = saturation = lightness = 0;
        var dialog = Create(owner, "Hue/Saturation", out var root);
        var h = Slider(root, "Hue", -180, 180, 0);
        var s = Slider(root, "Saturation", -100, 100, 0);
        var l = Slider(root, "Lightness", -100, 100, 0);
        if (!Show(dialog, root, () => true)) return false;
        hue = (float)h.Value;
        saturation = (float)(s.Value / 100);
        lightness = (float)(l.Value / 100);
        return true;
    }

    public static bool Amount(Window owner, string title, string label, double minimum, double maximum, double start, out double value)
    {
        value = start;
        var dialog = Create(owner, title, out var root);
        var slider = Slider(root, label, minimum, maximum, start);
        if (!Show(dialog, root, () => true)) return false;
        value = slider.Value;
        return true;
    }

    public static bool Text(Window owner, out string text, out int size)
    {
        text = "Text";
        size = 64;
        var dialog = Create(owner, "Type", out var root);
        root.Children.Add(Field("Text", text, out var textBox));
        root.Children.Add(Field("Size", size.ToString(CultureInfo.InvariantCulture), out var sizeBox));
        int parsedSize = 64;
        if (!Show(dialog, root, () => int.TryParse(sizeBox.Text, out parsedSize) && parsedSize is >= 1 and <= 2000 && textBox.Text.Length > 0))
            return false;
        text = textBox.Text;
        size = parsedSize;
        return true;
    }

    public static bool Rename(Window owner, ref string name)
    {
        var dialog = Create(owner, "Rename layer", out var root);
        root.Children.Add(Field("Name", name, out var box));
        if (!Show(dialog, root, () => !string.IsNullOrWhiteSpace(box.Text))) return false;
        name = box.Text.Trim();
        return true;
    }

    public static bool PickColor(Window owner, ref Rgba color)
    {
        var dialog = Create(owner, "Color", out var root);
        var r = Slider(root, "Red", 0, 255, color.R);
        var g = Slider(root, "Green", 0, 255, color.G);
        var b = Slider(root, "Blue", 0, 255, color.B);
        if (!Show(dialog, root, () => true)) return false;
        color = new Rgba((byte)r.Value, (byte)g.Value, (byte)b.Value, 255);
        return true;
    }

    static bool Pair(Window owner, string title, string subtitle, string a, string b, ref int va, ref int vb)
    {
        int w = va, h = vb;
        var dialog = Create(owner, title, out var root);
        root.Children.Add(new TextBlock { Text = subtitle, Foreground = Brushes.Silver, Margin = new Thickness(0, 0, 0, 8) });
        root.Children.Add(Field(a, w.ToString(CultureInfo.InvariantCulture), out var boxA));
        root.Children.Add(Field(b, h.ToString(CultureInfo.InvariantCulture), out var boxB));
        if (!Show(dialog, root, () => int.TryParse(boxA.Text, out w) && int.TryParse(boxB.Text, out h) && CanvasDocument.ValidDimension(w) && CanvasDocument.ValidDimension(h)))
            return false;
        va = w;
        vb = h;
        return true;
    }

    static Window Create(Window owner, string title, out StackPanel root)
    {
        root = new StackPanel { Margin = new Thickness(20) };
        return new Window
        {
            Title = title,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(36, 36, 36)),
            Foreground = Brushes.White,
            Content = root,
            MinWidth = 320
        };
    }

    static UIElement Field(string label, string value, out TextBox box)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        box = new TextBox { Text = value };
        stack.Children.Add(box);
        return stack;
    }

    static Slider Slider(StackPanel root, string label, double minimum, double maximum, double value)
    {
        root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 2) });
        var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 280 };
        root.Children.Add(slider);
        return slider;
    }

    static bool Show(Window dialog, StackPanel root, Func<bool> valid)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(16, 4, 16, 4) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 4, 16, 4) };
        ok.Click += (_, _) => { if (valid()) dialog.DialogResult = true; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        return dialog.ShowDialog() == true;
    }
}

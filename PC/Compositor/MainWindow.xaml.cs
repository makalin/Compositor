using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Compositor;

public partial class MainWindow : Window
{
    readonly EditorSession _session = new();
    int _builtStructure = -1;
    EditorTool _builtTool = (EditorTool)(-1);
    bool _builtErase;
    bool _ready;
    bool _syncingAppearance;
    static readonly ControlTemplate ToolButtonTemplate = FlatTemplate(7);

    public MainWindow()
    {
        InitializeComponent();
        CanvasView.Session = _session;
        _session.Changed += Refresh;
        _session.Painted += () => Dispatcher.Invoke(() =>
        {
            CanvasView.InvalidateVisual();
            UpdateStatus();
        });
        _session.TextRequested += PlaceText;
        CanvasView.SizeChanged += (_, _) =>
        {
            if (!_session.FitRequested || !_session.HasDocument) return;
            var size = CanvasView.DeviceSize();
            if (size.Width > 20) _session.Fit(size.Width, size.Height);
        };
        BuildTools();
        WireChrome();
        _ready = true;
        Refresh();
    }

    void WireChrome()
    {
        NewCanvasButton.Content = ChromeIcons.Plus();
        ZoomInButton.Content = ChromeIcons.Magnify(true);
        ZoomOutButton.Content = ChromeIcons.Magnify(false);
        NewLayerButton.Content = ChromeIcons.PlusSquare();
        GroupLayerButton.Content = ChromeIcons.FolderPlus();
        MaskLayerButton.Content = ChromeIcons.Mask();
        LayerUpButton.Content = ChromeIcons.Chevron(true);
        LayerDownButton.Content = ChromeIcons.Chevron(false);
        DeleteLayerButton.Content = ChromeIcons.Trash();
        SwapColorsButton.Content = ChromeIcons.Swap();
        ResetColorsButton.Content = ChromeIcons.Reset();
        foreach (var mode in BlendModes.All) BlendBox.Items.Add(BlendModes.Name(mode));
        BlendBox.SelectedIndex = 0;
        BlendBox.SelectionChanged += (_, _) =>
        {
            if (!_ready || _syncingAppearance || BlendBox.SelectedItem is not string name) return;
            _session.SetBlend(BlendModes.Parse(name));
        };
    }

    void BuildTools()
    {
        (EditorTool Tool, string Tip)[] tools =
        [
            (EditorTool.Move, "Move / Transform (V)"),
            (EditorTool.Marquee, "Marquee (M)"),
            (EditorTool.Lasso, "Lasso (L)"),
            (EditorTool.Wand, "Magic (W)"),
            (EditorTool.Crop, "Crop (C)"),
            (EditorTool.Brush, "Brush (B) · Eraser (E)"),
            (EditorTool.SpotHealing, "Spot Healing Brush (J)"),
            (EditorTool.CloneStamp, "Clone Stamp (S) · Alt-click sets the source"),
            (EditorTool.Blur, "Smear (R)"),
            (EditorTool.Gradient, "Gradient (G)"),
            (EditorTool.Shape, "Shape (U)"),
            (EditorTool.Type, "Type (T)"),
            (EditorTool.Eyedropper, "Eyedropper (I)"),
            (EditorTool.Hand, "Hand (H)"),
            (EditorTool.Zoom, "Zoom (Z)")
        ];
        foreach (var tool in tools)
        {
            var button = new Button
            {
                Content = ChromeIcons.For(tool.Tool),
                Width = 36,
                Height = 36,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(0),
                Tag = tool.Tool,
                ToolTip = tool.Tip,
                Template = ToolButtonTemplate,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White
            };
            button.Click += (_, _) =>
            {
                if (tool.Tool == EditorTool.Brush) _session.Erase = false;
                _session.SelectTool(tool.Tool);
            };
            ToolRail.Children.Add(button);
        }
    }

    void Refresh()
    {
        Title = _session.Title;
        Welcome.Visibility = _session.HasDocument ? Visibility.Collapsed : Visibility.Visible;
        bool has = _session.HasDocument;
        foreach (var item in new MenuItem[] { SaveItem, SaveAsItem, ExportPngItem, ExportJpegItem, CloseItem, UndoItem, RedoItem, CutItem, CopyItem, CopyMergedItem, PasteItem, FillFgItem, FillBgItem, ClearItem, LevelsItem, HueItem, ExposureItem, GrayItem, InvertItem, CanvasSizeItem, ImageSizeItem, TrimItem, FlipCanvasHItem, FlipCanvasVItem, NewLayerItem, GroupItem, DuplicateItem, DeleteLayerItem, MergeItem, RenameItem, MaskItem, InvertMaskItem, FlipLayerHItem, FlipLayerVItem, AllItem, DeselectItem, InverseItem, LayerPixelsItem, ExpandItem, ContractItem, FeatherItem, BlurItem, MotionItem, NoiseItem, FitItem, ActualItem, ZoomInItem, ZoomOutItem })
            item.IsEnabled = has;
        UndoItem.IsEnabled = _session.CanUndo;
        RedoItem.IsEnabled = _session.CanRedo;
        ForegroundSwatch.Background = new SolidColorBrush(Media(_session.Foreground));
        BackgroundSwatch.Background = new SolidColorBrush(Media(_session.Background));
        SyncAppearance();
        RefreshToolIcons();
        if (_builtTool != _session.Tool || _builtErase != _session.Erase) RebuildHeader();
        if (_builtStructure != _session.Structure) RebuildLayers();
        UpdateStatus();
        CanvasView.InvalidateVisual();
        if (_session.FitRequested && _session.HasDocument)
        {
            var size = CanvasView.DeviceSize();
            if (size.Width > 20) _session.Fit(size.Width, size.Height);
        }
    }

    void RebuildHeader()
    {
        _builtTool = _session.Tool;
        _builtErase = _session.Erase;
        HeaderHost.Children.Clear();
        HeaderHost.Children.Add(Label(ChromeIcons.ToolLabel(_session.Tool, _session.Erase)));
        switch (_session.Tool)
        {
            case EditorTool.Brush:
            case EditorTool.SpotHealing:
            case EditorTool.CloneStamp:
            case EditorTool.Blur:
                HeaderHost.Children.Add(SliderLabel("Size", 1, 400, _session.BrushSize, value => _session.BrushSize = (int)value));
                HeaderHost.Children.Add(SliderLabel("Hardness", 0, 100, _session.BrushHardness, value => _session.BrushHardness = (int)value));
                HeaderHost.Children.Add(SliderLabel("Opacity", 1, 100, _session.BrushOpacity, value => _session.BrushOpacity = (int)value));
                if (_session.Tool == EditorTool.Brush)
                {
                    var erase = new CheckBox { Content = "Erase", IsChecked = _session.Erase, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
                    erase.Checked += (_, _) => { _session.Erase = true; _builtErase = true; SetHeaderTitle(); RefreshToolIcons(); };
                    erase.Unchecked += (_, _) => { _session.Erase = false; _builtErase = false; SetHeaderTitle(); RefreshToolIcons(); };
                    HeaderHost.Children.Add(erase);
                    var mask = new CheckBox { Content = "Mask", IsChecked = _session.PaintMask, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
                    mask.Checked += (_, _) => _session.PaintMask = true;
                    mask.Unchecked += (_, _) => _session.PaintMask = false;
                    HeaderHost.Children.Add(mask);
                }
                break;
            case EditorTool.Marquee:
                HeaderHost.Children.Add(Choice("Rectangle", "Ellipse", _session.EllipseMarquee, value => { _session.EllipseMarquee = value; _session.SelectTool(EditorTool.Marquee); }));
                break;
            case EditorTool.Lasso:
                HeaderHost.Children.Add(Choice("Freehand", "Polygonal", _session.PolygonalLasso, value => { _session.PolygonalLasso = value; RefreshToolIcons(); }));
                break;
            case EditorTool.Wand:
                HeaderHost.Children.Add(SliderLabel("Tolerance", 0, 255, _session.Tolerance, value => _session.Tolerance = (int)value));
                var contiguous = new CheckBox { Content = "Contiguous", IsChecked = _session.Contiguous, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
                contiguous.Checked += (_, _) => _session.Contiguous = true;
                contiguous.Unchecked += (_, _) => _session.Contiguous = false;
                HeaderHost.Children.Add(contiguous);
                break;
            case EditorTool.Shape:
                var shapes = new ComboBox { Width = 120, Margin = new Thickness(12, 0, 0, 0), SelectedIndex = (int)_session.Shape };
                shapes.Items.Add("Rectangle");
                shapes.Items.Add("Ellipse");
                shapes.Items.Add("Line");
                shapes.SelectionChanged += (_, _) => _session.Shape = (ShapeKind)Math.Max(0, shapes.SelectedIndex);
                HeaderHost.Children.Add(shapes);
                break;
        }
    }

    void SetHeaderTitle()
    {
        if (HeaderHost.Children.Count > 0 && HeaderHost.Children[0] is TextBlock title)
            title.Text = ChromeIcons.ToolLabel(_session.Tool, _session.Erase);
    }

    void RefreshToolIcons()
    {
        foreach (Button button in ToolRail.Children)
        {
            if (button.Tag is not EditorTool tool) continue;
            bool selected = tool == _session.Tool;
            button.Background = new SolidColorBrush(selected ? Color.FromArgb(31, 255, 255, 255) : Colors.Transparent);
            button.BorderBrush = new SolidColorBrush(selected ? Color.FromArgb(36, 255, 255, 255) : Colors.Transparent);
            button.BorderThickness = new Thickness(selected ? 1 : 0);
            if (tool is EditorTool.Brush or EditorTool.Lasso)
                button.Content = ChromeIcons.For(tool, _session.Erase, _session.PolygonalLasso);
        }
    }

    void RebuildLayers()
    {
        _builtStructure = _session.Structure;
        LayerHost.Children.Clear();
        int count = _session.Document?.Layers.Count ?? 0;
        LayerCount.Text = count.ToString();
        if (_session.Document == null) return;
        for (int i = _session.Document.Layers.Count - 1; i >= 0; i--)
        {
            var layer = _session.Document.Layers[i];
            int depth = _session.Document.Depth(layer);
            bool active = layer.Id == _session.Document.ActiveLayerId;
            var row = new Grid
            {
                Height = 52,
                Margin = new Thickness(depth * 14, 0, 0, 0),
                Background = new SolidColorBrush(active ? Color.FromArgb(70, 10, 132, 255) : Colors.Transparent),
                Tag = layer.Id
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var eye = new Button
            {
                Content = ChromeIcons.Eye(layer.Visible),
                Width = 28,
                Height = 32,
                Tag = layer.Id,
                Template = ToolButtonTemplate,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Opacity = layer.Visible ? 0.85 : 0.35,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            eye.Click += (_, _) => _session.ToggleVisible((Guid)eye.Tag);
            var thumb = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 8, 0),
                Background = CheckerBrush,
                ClipToBounds = true,
                Child = layer.IsGroup ? Centered(ChromeIcons.FolderPlus()) : layer.Pixels == null ? null : Thumbnail(layer.Pixels)
            };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = layer.Name,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            text.Children.Add(new TextBlock
            {
                Text = layer.IsGroup ? "Group" : layer.Pixels == null ? "Empty" : $"{layer.Pixels.Width} × {layer.Pixels.Height}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160))
            });
            Grid.SetColumn(thumb, 1);
            Grid.SetColumn(text, 2);
            row.Children.Add(eye);
            row.Children.Add(thumb);
            row.Children.Add(text);
            row.MouseLeftButtonUp += (_, _) => _session.SetActive(layer.Id);
            row.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) OnRename(this, new RoutedEventArgs()); };
            var host = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(58, 58, 58)), BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
            LayerHost.Children.Add(host);
        }
    }

    void SyncAppearance()
    {
        var layer = _session.Document?.ActiveLayer;
        AppearanceHost.IsEnabled = layer != null;
        if (layer == null) return;
        _syncingAppearance = true;
        string mode = BlendModes.Name(layer.IsGroup ? BlendMode.Normal : layer.BlendMode);
        if (BlendBox.SelectedItem as string != mode) BlendBox.SelectedItem = mode;
        if (!OpacitySlider.IsMouseCaptureWithin) OpacitySlider.Value = layer.Opacity * 100;
        if (!OpacityBox.IsKeyboardFocused) OpacityBox.Text = ((int)Math.Round(layer.Opacity * 100)).ToString();
        _syncingAppearance = false;
    }

    void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _syncingAppearance || _session.Document?.ActiveLayer == null) return;
        if (Math.Abs(_session.Document.ActiveLayer.Opacity * 100 - e.NewValue) < 0.4) return;
        _session.SetOpacity(e.NewValue / 100);
        if (!OpacityBox.IsKeyboardFocused) OpacityBox.Text = ((int)Math.Round(e.NewValue)).ToString();
    }

    void OnOpacityTyped(object sender, RoutedEventArgs e)
    {
        if (!_ready || _session.Document?.ActiveLayer == null) return;
        if (double.TryParse(OpacityBox.Text, out double percent)) _session.SetOpacity(percent / 100);
        SyncAppearance();
    }

    void OnSwapColors(object sender, RoutedEventArgs e)
    {
        (_session.Foreground, _session.Background) = (_session.Background, _session.Foreground);
        Refresh();
    }

    void OnResetColors(object sender, RoutedEventArgs e)
    {
        _session.Foreground = Rgba.Black;
        _session.Background = Rgba.White;
        Refresh();
    }

    static ControlTemplate FlatTemplate(double radius)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background)) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Control.BorderBrush)) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(Control.BorderThickness)) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    static readonly Brush CheckerBrush = CreateChecker();

    static Brush CreateChecker()
    {
        var dark = new SolidColorBrush(Color.FromRgb(46, 46, 46));
        var light = new SolidColorBrush(Color.FromRgb(68, 68, 68));
        dark.Freeze();
        light.Freeze();
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        group.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(0, 0, 4, 4))));
        group.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(4, 4, 4, 4))));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        brush.Freeze();
        return brush;
    }

    static Image Thumbnail(PixelBuffer pixels)
    {
        const int max = 36;
        int step = Math.Max(1, Math.Max(pixels.Width, pixels.Height) / max);
        int tw = Math.Max(1, (pixels.Width + step - 1) / step);
        int th = Math.Max(1, (pixels.Height + step - 1) / step);
        var bytes = new byte[tw * th * 4];
        var src = pixels.Rgba;
        for (int y = 0; y < th; y++)
        {
            int sy = Math.Min(pixels.Height - 1, y * step);
            for (int x = 0; x < tw; x++)
            {
                int sx = Math.Min(pixels.Width - 1, x * step);
                int si = (sy * pixels.Width + sx) * 4;
                int di = (y * tw + x) * 4;
                bytes[di] = src[si + 2];
                bytes[di + 1] = src[si + 1];
                bytes[di + 2] = src[si];
                bytes[di + 3] = src[si + 3];
            }
        }
        var bitmap = new WriteableBitmap(tw, th, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, tw, th), bytes, tw * 4, 0);
        bitmap.Freeze();
        return new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    void UpdateStatus()
    {
        if (_session.Document == null) { StatusText.Text = "Ready when you are"; return; }
        string coords = _session.Hovering ? $"   {_session.HoverX:0}, {_session.HoverY:0}" : "";
        StatusText.Text = $"{_session.Zoom * 100:0}%    {_session.Document.Width} × {_session.Document.Height} px    sRGB · Transparent    {_session.ToolHint()}{coords}";
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        if (e.Key == Key.Space) { _session.SpacePan = true; e.Handled = true; return; }
        if (ctrl && e.Key == Key.N && shift) { OnNewLayer(this, e); e.Handled = true; return; }
        if (ctrl && e.Key == Key.N) { OnNew(this, e); e.Handled = true; return; }
        if (ctrl && e.Key == Key.O) { OnOpen(this, e); e.Handled = true; return; }
        if (ctrl && e.Key == Key.S && shift) { OnSaveAs(this, e); e.Handled = true; return; }
        if (ctrl && e.Key == Key.S) { OnSave(this, e); e.Handled = true; return; }
        if (ctrl && e.Key == Key.E && shift) { OnExportPng(this, e); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Z && shift) { _session.Redo(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Z) { _session.Undo(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Y) { _session.Redo(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.A) { _session.SelectAll(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.D) { _session.Deselect(); e.Handled = true; return; }
        if (!ctrl && !alt && e.Key == Key.X) { OnSwapColors(this, e); e.Handled = true; return; }
        if (!ctrl && !alt && e.Key == Key.D) { OnResetColors(this, e); e.Handled = true; return; }
        if (ctrl && e.Key == Key.C && shift) { _session.Copy(merged: true); e.Handled = true; return; }
        if (ctrl && e.Key == Key.C) { _session.Copy(merged: false); e.Handled = true; return; }
        if (ctrl && e.Key == Key.X) { _session.Cut(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.V) { OnPaste(this, e); e.Handled = true; return; }
        if (ctrl && e.Key == Key.I && shift) { _session.InvertSelection(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.I) { _session.InvertPixels(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.L) { OnLevels(this, e); e.Handled = true; return; }
        if (ctrl && e.Key == Key.U) { OnHue(this, e); e.Handled = true; return; }
        if (ctrl && e.Key == Key.E && !shift) { _session.MergeDown(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.W) { OnClose(this, e); e.Handled = true; return; }
        if (ctrl && e.Key == Key.D0) { OnFit(this, e); e.Handled = true; return; }
        if (ctrl && e.Key == Key.D1) { OnActual(this, e); e.Handled = true; return; }
        if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add)) { OnZoomIn(this, e); e.Handled = true; return; }
        if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract)) { OnZoomOut(this, e); e.Handled = true; return; }
        if (e.Key == Key.Enter) { _session.ApplyCrop(); _session.CloseLasso(); e.Handled = true; return; }
        if (e.Key == Key.Escape) { _session.CancelTransient(); e.Handled = true; return; }
        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            if (alt) _session.Fill(true);
            else if (ctrl) _session.Fill(false);
            else _session.ClearSelectedPixels();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.OemOpenBrackets) { _session.BrushSize = Math.Max(1, _session.BrushSize - (shift ? 5 : 1)); e.Handled = true; return; }
        if (e.Key == Key.OemCloseBrackets) { _session.BrushSize = Math.Min(400, _session.BrushSize + (shift ? 5 : 1)); e.Handled = true; return; }
        if (ctrl || alt) return;
        switch (e.Key)
        {
            case Key.V: _session.SelectTool(EditorTool.Move); break;
            case Key.M: _session.SelectTool(EditorTool.Marquee); break;
            case Key.L: _session.SelectTool(EditorTool.Lasso); break;
            case Key.W: _session.SelectTool(EditorTool.Wand); break;
            case Key.C: _session.SelectTool(EditorTool.Crop); break;
            case Key.B: _session.Erase = false; _session.SelectTool(EditorTool.Brush); break;
            case Key.E: _session.UseEraser(); break;
            case Key.J: _session.SelectTool(EditorTool.SpotHealing); break;
            case Key.S: _session.SelectTool(EditorTool.CloneStamp); break;
            case Key.R: _session.SelectTool(EditorTool.Blur); break;
            case Key.G: _session.SelectTool(EditorTool.Gradient); break;
            case Key.U: _session.SelectTool(EditorTool.Shape); break;
            case Key.T: _session.SelectTool(EditorTool.Type); break;
            case Key.I: _session.SelectTool(EditorTool.Eyedropper); break;
            case Key.H: _session.SelectTool(EditorTool.Hand); break;
            case Key.Z: _session.SelectTool(EditorTool.Zoom); break;
            default: return;
        }
        e.Handled = true;
    }

    void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) _session.SpacePan = false;
    }

    void OnNew(object sender, RoutedEventArgs e)
    {
        if (!ConfirmClose()) return;
        if (!EditorDialogs.NewCanvas(this, out int w, out int h)) return;
        _session.NewCanvas(w, h);
    }

    void OnCreateWelcome(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(WelcomeWidth.Text, out int w) || !int.TryParse(WelcomeHeight.Text, out int h) || !CanvasDocument.ValidDimension(w) || !CanvasDocument.ValidDimension(h))
        {
            MessageBox.Show(this, "Enter whole numbers from 1 to 30,000 pixels.", "New canvas");
            return;
        }
        _session.NewCanvas(w, h);
    }

    void OnOpen(object sender, RoutedEventArgs e)
    {
        if (!ConfirmClose()) return;
        var dialog = new OpenFolderDialog { Title = "Open Compositor project" };
        if (dialog.ShowDialog(this) == true) OpenSafe(dialog.FolderName);
    }

    void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var name in dialog.FileNames) ImportSafe(name);
    }

    void OnSave(object sender, RoutedEventArgs e)
    {
        if (!_session.HasDocument) return;
        if (_session.ProjectPath != null) SaveSafe(_session.ProjectPath);
        else OnSaveAs(sender, e);
    }

    void OnSaveAs(object sender, RoutedEventArgs e)
    {
        if (!_session.HasDocument) return;
        var dialog = new OpenFolderDialog { Title = "Choose a folder for the project" };
        if (dialog.ShowDialog(this) != true) return;
        string name = string.IsNullOrEmpty(_session.ProjectPath) ? "Untitled" : Path.GetFileName(_session.ProjectPath);
        if (!EditorDialogs.Rename(this, ref name)) return;
        string path = Path.Combine(dialog.FolderName, name.EndsWith(".comp", StringComparison.OrdinalIgnoreCase) ? name : name + ".comp");
        SaveSafe(path);
    }

    void OnExportPng(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "PNG|*.png", FileName = "Compositor.png" };
        if (dialog.ShowDialog(this) == true) _session.ExportPng(dialog.FileName);
    }

    void OnExportJpeg(object sender, RoutedEventArgs e)
    {
        if (!EditorDialogs.Amount(this, "Export JPEG", "Quality", 1, 100, 90, out double quality)) return;
        var dialog = new SaveFileDialog { Filter = "JPEG|*.jpg", FileName = "Compositor.jpg" };
        if (dialog.ShowDialog(this) == true) _session.ExportJpeg(dialog.FileName, (int)quality);
    }

    void OnClose(object sender, RoutedEventArgs e)
    {
        if (ConfirmClose()) _session.CloseDocument();
    }

    void OnExit(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmClose()) e.Cancel = true;
        base.OnClosing(e);
    }

    void OnUndo(object sender, RoutedEventArgs e) => _session.Undo();
    void OnRedo(object sender, RoutedEventArgs e) => _session.Redo();
    void OnCut(object sender, RoutedEventArgs e) => _session.Cut();
    void OnCopy(object sender, RoutedEventArgs e) => _session.Copy(false);
    void OnCopyMerged(object sender, RoutedEventArgs e) => _session.Copy(true);
    void OnPaste(object sender, RoutedEventArgs e)
    {
        var center = CanvasView.ViewCenter();
        _session.Paste(center.X, center.Y);
    }
    void OnFillFg(object sender, RoutedEventArgs e) => _session.Fill(true);
    void OnFillBg(object sender, RoutedEventArgs e) => _session.Fill(false);
    void OnClearPixels(object sender, RoutedEventArgs e) => _session.ClearSelectedPixels();
    void OnLevels(object sender, RoutedEventArgs e)
    {
        if (!EditorDialogs.Levels(this, out var ib, out var iw, out var g, out var ob, out var ow)) return;
        _session.ApplyLevels(ib, iw, g, ob, ow);
    }
    void OnHue(object sender, RoutedEventArgs e)
    {
        if (!EditorDialogs.HueSaturation(this, out var h, out var s, out var l)) return;
        _session.ApplyHue(h, s, l);
    }
    void OnExposure(object sender, RoutedEventArgs e)
    {
        if (EditorDialogs.Amount(this, "Exposure", "EV", -4, 4, 0, out double ev)) _session.ApplyExposure((float)ev);
    }
    void OnGray(object sender, RoutedEventArgs e) => _session.ApplyBlackAndWhite();
    void OnInvert(object sender, RoutedEventArgs e) => _session.InvertPixels();
    void OnCanvasSize(object sender, RoutedEventArgs e)
    {
        if (_session.Document == null) return;
        int w = _session.Document.Width, h = _session.Document.Height;
        if (!EditorDialogs.CanvasSize(this, ref w, ref h, out int anchor)) return;
        _session.ResizeCanvas(w, h, anchor);
    }
    void OnImageSize(object sender, RoutedEventArgs e)
    {
        if (_session.Document == null) return;
        int w = _session.Document.Width, h = _session.Document.Height;
        if (!EditorDialogs.ImageSize(this, ref w, ref h)) return;
        _session.ResizeImage(w, h);
    }
    void OnTrim(object sender, RoutedEventArgs e) => _session.Trim();
    void OnFlipCanvasH(object sender, RoutedEventArgs e) => _session.FlipCanvas(true);
    void OnFlipCanvasV(object sender, RoutedEventArgs e) => _session.FlipCanvas(false);
    void OnNewLayer(object sender, RoutedEventArgs e) => _session.AddLayer();
    void OnGroup(object sender, RoutedEventArgs e) => _session.AddGroup();
    void OnDuplicate(object sender, RoutedEventArgs e) => _session.DuplicateActive();
    void OnDeleteLayer(object sender, RoutedEventArgs e) => _session.DeleteActive();
    void OnMerge(object sender, RoutedEventArgs e) => _session.MergeDown();
    void OnRename(object sender, RoutedEventArgs e)
    {
        if (_session.Document?.ActiveLayer == null) return;
        string name = _session.Document.ActiveLayer.Name;
        if (EditorDialogs.Rename(this, ref name)) _session.RenameActive(name);
    }
    void OnMask(object sender, RoutedEventArgs e) => _session.AddMask();
    void OnInvertMask(object sender, RoutedEventArgs e) => _session.InvertMask();
    void OnFlipLayerH(object sender, RoutedEventArgs e) => _session.FlipActive(true);
    void OnFlipLayerV(object sender, RoutedEventArgs e) => _session.FlipActive(false);
    void OnSelectAll(object sender, RoutedEventArgs e) => _session.SelectAll();
    void OnDeselect(object sender, RoutedEventArgs e) => _session.Deselect();
    void OnInverse(object sender, RoutedEventArgs e) => _session.InvertSelection();
    void OnLayerPixels(object sender, RoutedEventArgs e) => _session.SelectLayerPixels();
    void OnExpand(object sender, RoutedEventArgs e) => ModifySelection(false, false);
    void OnContract(object sender, RoutedEventArgs e) => ModifySelection(true, false);
    void OnFeather(object sender, RoutedEventArgs e) => ModifySelection(false, true);
    void OnBlur(object sender, RoutedEventArgs e)
    {
        if (EditorDialogs.Amount(this, "Gaussian Blur", "Radius", 0, 48, 4, out double radius)) _session.ApplyBlur((float)radius);
    }
    void OnMotion(object sender, RoutedEventArgs e)
    {
        if (EditorDialogs.Amount(this, "Motion Blur", "Distance", 1, 64, 12, out double distance)) _session.ApplyMotionBlur(0, (int)distance);
    }
    void OnNoise(object sender, RoutedEventArgs e)
    {
        if (EditorDialogs.Amount(this, "Add Noise", "Amount", 1, 80, 12, out double amount)) _session.ApplyNoise((int)amount);
    }
    void OnFit(object sender, RoutedEventArgs e)
    {
        var size = CanvasView.DeviceSize();
        _session.Fit(size.Width, size.Height);
        CanvasView.InvalidateVisual();
    }
    void OnActual(object sender, RoutedEventArgs e)
    {
        var size = CanvasView.DeviceSize();
        _session.ZoomBy(1 / Math.Max(0.0001, _session.Zoom), size.Width / 2, size.Height / 2);
        CanvasView.InvalidateVisual();
    }
    void OnZoomIn(object sender, RoutedEventArgs e)
    {
        var size = CanvasView.DeviceSize();
        _session.ZoomBy(1.25, size.Width / 2, size.Height / 2);
        CanvasView.InvalidateVisual();
    }
    void OnZoomOut(object sender, RoutedEventArgs e)
    {
        var size = CanvasView.DeviceSize();
        _session.ZoomBy(1 / 1.25, size.Width / 2, size.Height / 2);
        CanvasView.InvalidateVisual();
    }
    void OnRulers(object sender, RoutedEventArgs e) { _session.ShowRulers = RulersItem.IsChecked; CanvasView.InvalidateVisual(); }
    void OnGrid(object sender, RoutedEventArgs e) { _session.ShowGrid = GridItem.IsChecked; CanvasView.InvalidateVisual(); }
    void OnGuides(object sender, RoutedEventArgs e) { _session.ShowGuides = GuidesItem.IsChecked; CanvasView.InvalidateVisual(); }
    void OnSnap(object sender, RoutedEventArgs e) => _session.SnapEnabled = SnapItem.IsChecked;
    void OnClearGuides(object sender, RoutedEventArgs e)
    {
        _session.Document?.Guides.Clear();
        CanvasView.InvalidateVisual();
    }
    void OnLayerUp(object sender, RoutedEventArgs e) => _session.MoveActive(1);
    void OnLayerDown(object sender, RoutedEventArgs e) => _session.MoveActive(-1);
    void OnForeground(object sender, MouseButtonEventArgs e)
    {
        var color = _session.Foreground;
        if (EditorDialogs.PickColor(this, ref color)) { _session.Foreground = color; Refresh(); }
    }
    void OnBackground(object sender, MouseButtonEventArgs e)
    {
        var color = _session.Background;
        if (EditorDialogs.PickColor(this, ref color)) { _session.Background = color; Refresh(); }
    }

    void PlaceText(double x, double y)
    {
        if (!EditorDialogs.Text(this, out string text, out int size)) return;
        _session.AddText(text, size, x, y, RasterizeText(text, size, _session.Foreground));
    }

    static PixelBuffer RasterizeText(string text, int size, Rgba color)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size,
            new SolidColorBrush(Media(color)), 1);
        int width = Math.Max(1, (int)Math.Ceiling(formatted.WidthIncludingTrailingWhitespace) + 4);
        int height = Math.Max(1, (int)Math.Ceiling(formatted.Height) + 4);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawText(formatted, new Point(2, 2));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var bytes = new byte[width * height * 4];
        bitmap.CopyPixels(bytes, width * 4, 0);
        var buffer = new PixelBuffer(width, height);
        for (int i = 0; i < width * height; i++)
        {
            byte alpha = bytes[i * 4 + 3];
            if (alpha == 0) continue;
            buffer.Set(i % width, i / width, new Rgba(color.R, color.G, color.B, alpha));
        }
        return buffer;
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        foreach (var file in files)
        {
            if (Directory.Exists(file) && File.Exists(Path.Combine(file, "manifest.json")))
            {
                if (ConfirmClose()) OpenSafe(file);
            }
            else if (ImageFiles.IsImagePath(file)) ImportSafe(file);
        }
    }

    bool ConfirmClose()
    {
        if (!_session.Dirty) return true;
        var result = MessageBox.Show(this, "Save changes to this project?", "Compositor", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.Yes) OnSave(this, new RoutedEventArgs());
        return !_session.Dirty || result == MessageBoxResult.No;
    }

    void OpenSafe(string path)
    {
        try { _session.OpenProject(path); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Couldn’t open the project"); }
    }

    void SaveSafe(string path)
    {
        try { _session.SaveProject(path); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Couldn’t save the project"); }
    }

    void ImportSafe(string path)
    {
        try { _session.ImportImage(path); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Couldn’t import the image"); }
    }

    void ModifySelection(bool contract, bool feather)
    {
        if (!EditorDialogs.Amount(this, feather ? "Feather" : contract ? "Contract" : "Expand", "Pixels", 1, 64, 1, out double amount)) return;
        _session.ModifySelection(contract, (int)amount, feather);
    }

    static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(4, 0, 8, 0) };

    static UIElement Centered(UIElement element)
    {
        if (element is FrameworkElement view)
        {
            view.HorizontalAlignment = HorizontalAlignment.Center;
            view.VerticalAlignment = VerticalAlignment.Center;
        }
        return element;
    }

    static StackPanel SliderLabel(string name, double min, double max, double value, Action<double> changed)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        panel.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, Width = 64 });
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = 120, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, args) => changed(args.NewValue);
        panel.Children.Add(slider);
        return panel;
    }

    static StackPanel Choice(string off, string on, bool value, Action<bool> changed)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        var box = new CheckBox { Content = value ? on : off, IsChecked = value, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        box.Checked += (_, _) => { box.Content = on; changed(true); };
        box.Unchecked += (_, _) => { box.Content = off; changed(false); };
        panel.Children.Add(box);
        return panel;
    }

    static Color Media(Rgba color) => Color.FromRgb(color.R, color.G, color.B);

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
    }
}

namespace Compositor.Tests;

public class BlendTests
{
    [Fact]
    public void MultiplyRedOverWhiteStaysRed()
    {
        byte r = 255, g = 255, b = 255, a = 255;
        BlendModes.Composite(BlendMode.Multiply, 255, 0, 0, 255, 1f, ref r, ref g, ref b, ref a);
        Assert.Equal(255, r);
        Assert.Equal(0, g);
        Assert.Equal(0, b);
        Assert.Equal(255, a);
    }

    [Fact]
    public void NormalHalfRedOverWhiteMixes()
    {
        byte r = 255, g = 255, b = 255, a = 255;
        BlendModes.Composite(BlendMode.Normal, 255, 0, 0, 255, 0.5f, ref r, ref g, ref b, ref a);
        Assert.InRange(r, 250, 255);
        Assert.InRange(g, 120, 135);
        Assert.InRange(b, 120, 135);
    }

    [Fact]
    public void FlattenStacksLayersBottomToTop()
    {
        var document = new CanvasDocument { Width = 2, Height = 1 };
        var bottom = new PixelBuffer(2, 1);
        bottom.Set(0, 0, Rgba.White);
        bottom.Set(1, 0, Rgba.White);
        document.AddPixelLayer("Bottom", bottom);
        var top = new PixelBuffer(2, 1);
        top.Set(0, 0, new Rgba(0, 0, 255, 255));
        top.Set(1, 0, Rgba.Transparent);
        var upper = document.AddPixelLayer("Top", top);
        upper.Opacity = 1;
        var flat = CompositorEngine.Flatten(document);
        Assert.Equal(0, flat.Get(0, 0).R);
        Assert.Equal(0, flat.Get(0, 0).G);
        Assert.Equal(255, flat.Get(0, 0).B);
        Assert.Equal(Rgba.White, flat.Get(1, 0));
    }
}

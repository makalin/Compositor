namespace Compositor;

/// <summary>Straight (non-premultiplied) 8-bit RGBA pixels, row-major.</summary>
public sealed class PixelBuffer
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Rgba { get; }

    public PixelBuffer(int width, int height, byte[]? rgba = null)
    {
        if (width < 1 || height < 1 || width > 30_000 || height > 30_000)
            throw new ArgumentOutOfRangeException(nameof(width), "Canvas sides must be from 1 to 30,000 pixels.");
        if ((long)width * height > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(width), "An image cannot exceed 100 million pixels.");
        Width = width;
        Height = height;
        Rgba = rgba ?? new byte[width * height * 4];
        if (Rgba.Length != width * height * 4)
            throw new ArgumentException("Pixel storage does not match the buffer size.", nameof(rgba));
    }

    public PixelBuffer Clone()
    {
        var copy = new byte[Rgba.Length];
        System.Buffer.BlockCopy(Rgba, 0, copy, 0, Rgba.Length);
        return new PixelBuffer(Width, Height, copy);
    }

    public int Offset(int x, int y) => (y * Width + x) * 4;

    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public Rgba Get(int x, int y)
    {
        int i = Offset(x, y);
        return new Rgba(Rgba[i], Rgba[i + 1], Rgba[i + 2], Rgba[i + 3]);
    }

    public void Set(int x, int y, Rgba color)
    {
        int i = Offset(x, y);
        Rgba[i] = color.R;
        Rgba[i + 1] = color.G;
        Rgba[i + 2] = color.B;
        Rgba[i + 3] = color.A;
    }

    public void Clear(Rgba color)
    {
        for (int i = 0; i < Rgba.Length; i += 4)
        {
            Rgba[i] = color.R;
            Rgba[i + 1] = color.G;
            Rgba[i + 2] = color.B;
            Rgba[i + 3] = color.A;
        }
    }

    public bool HasVisiblePixels()
    {
        for (int i = 3; i < Rgba.Length; i += 4)
            if (Rgba[i] != 0) return true;
        return false;
    }

    /// <summary>Bilinear sample. Coordinates are pixel centers at integer + 0.5, origin at the top left.</summary>
    public bool Sample(double x, double y, out byte r, out byte g, out byte b, out byte a)
    {
        r = g = b = a = 0;
        if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        int x1 = Math.Min(x0 + 1, Width - 1);
        int y1 = Math.Min(y0 + 1, Height - 1);
        double tx = x - x0;
        double ty = y - y0;
        Read(x0, y0, out int r00, out int g00, out int b00, out int a00);
        Read(x1, y0, out int r10, out int g10, out int b10, out int a10);
        Read(x0, y1, out int r01, out int g01, out int b01, out int a01);
        Read(x1, y1, out int r11, out int g11, out int b11, out int a11);
        r = Mix(r00, r10, r01, r11, tx, ty);
        g = Mix(g00, g10, g01, g11, tx, ty);
        b = Mix(b00, b10, b01, b11, tx, ty);
        a = Mix(a00, a10, a01, a11, tx, ty);
        return true;
    }

    void Read(int x, int y, out int r, out int g, out int b, out int a)
    {
        int i = Offset(x, y);
        r = Rgba[i];
        g = Rgba[i + 1];
        b = Rgba[i + 2];
        a = Rgba[i + 3];
    }

    static byte Mix(int c00, int c10, int c01, int c11, double tx, double ty)
    {
        double top = c00 + (c10 - c00) * tx;
        double bottom = c01 + (c11 - c01) * tx;
        return (byte)Math.Clamp((int)Math.Round(top + (bottom - top) * ty), 0, 255);
    }
}

public readonly record struct Rgba(byte R, byte G, byte B, byte A)
{
    public static Rgba Black { get; } = new(0, 0, 0, 255);
    public static Rgba White { get; } = new(255, 255, 255, 255);
    public static Rgba Transparent { get; } = new(0, 0, 0, 0);

    public static Rgba FromBytes(byte r, byte g, byte b, byte a = 255) => new(r, g, b, a);
}

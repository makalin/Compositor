using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Compositor;

public static class ImageFiles
{
    public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

    public static bool IsImagePath(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static PixelBuffer Load(string path) => Decode(File.ReadAllBytes(path));

    public static PixelBuffer Decode(byte[] data)
    {
        using var image = Image.Load<Rgba32>(data);
        return FromImage(image);
    }

    public static void SavePng(PixelBuffer buffer, string path)
    {
        using var image = Image.LoadPixelData<Rgba32>(buffer.Rgba, buffer.Width, buffer.Height);
        image.SaveAsPng(path);
    }

    public static void SaveJpeg(PixelBuffer buffer, string path, int quality)
    {
        var flat = CompositorEngine.CompositeOver(buffer, Rgba.White);
        using var image = Image.LoadPixelData<Rgba32>(flat.Rgba, flat.Width, flat.Height);
        image.SaveAsJpeg(path, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = Math.Clamp(quality, 1, 100) });
    }

    public static byte[] EncodePng(PixelBuffer buffer)
    {
        using var image = Image.LoadPixelData<Rgba32>(buffer.Rgba, buffer.Width, buffer.Height);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    public static PixelBuffer Resize(PixelBuffer buffer, int width, int height)
    {
        using var image = Image.LoadPixelData<Rgba32>(buffer.Rgba, buffer.Width, buffer.Height);
        image.Mutate(context => context.Resize(width, height, KnownResamplers.Lanczos3));
        return FromImage(image);
    }

    static PixelBuffer FromImage(Image<Rgba32> image)
    {
        var buffer = new PixelBuffer(image.Width, image.Height);
        image.CopyPixelDataTo(MemoryMarshal.Cast<byte, Rgba32>(buffer.Rgba.AsSpan()));
        return buffer;
    }
}

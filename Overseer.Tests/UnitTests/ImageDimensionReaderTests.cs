using System.Text;
using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ImageDimensionReaderTests
{
    internal static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 }.CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes, 12);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    private static byte[] Jpeg(int width, int height, byte sof = 0xC0)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        // An APP0 segment and a DHT segment, whose C4 marker lies inside the SOF range and is not a frame.
        bytes.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x04, 0x11, 0x22 });
        bytes.AddRange(new byte[] { 0xFF, 0xC4, 0x00, 0x03, 0x33 });
        bytes.AddRange(new byte[] { 0xFF, sof, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03 });
        return bytes.ToArray();
    }

    private static byte[] WebP(string chunk, params byte[] payload)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("RIFF"));
        bytes.AddRange(new byte[4]);
        bytes.AddRange(Encoding.ASCII.GetBytes("WEBP"));
        bytes.AddRange(Encoding.ASCII.GetBytes(chunk));
        bytes.AddRange(new byte[4]);
        bytes.AddRange(payload);
        return bytes.ToArray();
    }

    public static TheoryData<byte[], int, int> Valid => new()
    {
        { Png(1920, 1080), 1920, 1080 },
        { Jpeg(800, 600), 800, 600 },
        { Jpeg(640, 480, 0xC2), 640, 480 },
        // VP8: frame tag, start code, 14-bit width and height.
        { WebP("VP8 ", 0, 0, 0, 0x9D, 0x01, 0x2A, 0x80, 0x02, 0xE0, 0x01), 640, 480 },
        // VP8L: signature, then 14 bits of width - 1 (639) and 14 bits of height - 1 (479).
        { WebP("VP8L", 0x2F, 0x7F, 0xC2, 0x77, 0x00), 640, 480 },
        // VP8X: flags, reserved, 24-bit width - 1 and height - 1.
        { WebP("VP8X", 0, 0, 0, 0, 0x7F, 0x02, 0x00, 0xDF, 0x01, 0x00), 640, 480 }
    };

    [Theory]
    [MemberData(nameof(Valid))]
    public void ReadsTheDimensions(byte[] data, int width, int height)
    {
        Assert.True(ImageDimensionReader.TryRead(data, out int w, out int h));
        Assert.Equal(width, w);
        Assert.Equal(height, h);
    }

    public static TheoryData<byte[]> Invalid => new()
    {
        Array.Empty<byte>(),
        new byte[] { 0x89, 0x50 },
        Png(1, 1)[..20],
        Png(0, 100),
        Png(-1, 100),
        new byte[] { 0xFF, 0xD8 },
        new byte[] { 0xFF, 0xD8, 0xFF },
        // A segment length of zero, one running off the end, and scan data before any frame.
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x00 },
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0xFF, 0xFF, 0x00 },
        new byte[] { 0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x04 },
        Jpeg(800, 600)[..16],
        Jpeg(0, 600),
        Encoding.ASCII.GetBytes("RIFF"),
        WebP("VP8 ", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        WebP("VP8L", 0x00, 0, 0, 0, 0),
        WebP("VP8X", 0, 0),
        WebP("JUNK", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        Encoding.ASCII.GetBytes("not an image at all, just some text")
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void MalformedInput_ReturnsFalse(byte[] data)
    {
        Assert.False(ImageDimensionReader.TryRead(data, out int w, out int h));
        Assert.Equal(0, w);
        Assert.Equal(0, h);
    }

    [Fact]
    public void RandomBytes_NeverThrow()
    {
        var random = new Random(12345);
        for (int i = 0; i < 2000; i++)
        {
            var data = new byte[random.Next(0, 64)];
            random.NextBytes(data);
            if (i % 3 == 0 && data.Length >= 2) { data[0] = 0xFF; data[1] = 0xD8; }
            if (i % 3 == 1 && data.Length >= 12) { Encoding.ASCII.GetBytes("RIFF").CopyTo(data, 0); Encoding.ASCII.GetBytes("WEBP").CopyTo(data, 8); }

            ImageDimensionReader.TryRead(data, out _, out _);
        }
    }
}

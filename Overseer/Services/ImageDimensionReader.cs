namespace Overseer.Services;

using System.Buffers.Binary;

/// <summary>
/// Reads an image's width and height from its PNG, JPEG or WebP header without decoding pixels. The bytes
/// come from users, so malformed input returns false and never throws.
/// </summary>
public static class ImageDimensionReader
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool TryRead(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        bool read = data.StartsWith(PngSignature) ? TryReadPng(data, out width, out height)
            : data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8 ? TryReadJpeg(data, out width, out height)
            : IsAscii(data, 0, "RIFF") && IsAscii(data, 8, "WEBP") && TryReadWebP(data, out width, out height);

        if (read && width > 0 && height > 0) return true;

        width = 0;
        height = 0;
        return false;
    }

    private static bool TryReadPng(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 24 || !IsAscii(data, 12, "IHDR")) return false;

        uint w = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4));
        uint h = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4));
        if (w > int.MaxValue || h > int.MaxValue) return false;

        width = (int)w;
        height = (int)h;
        return true;
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        int pos = 2;
        while (pos < data.Length)
        {
            if (data[pos] != 0xFF) return false;
            while (pos < data.Length && data[pos] == 0xFF) pos++;
            if (pos >= data.Length) return false;

            byte marker = data[pos++];

            // Standalone markers carry no length.
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD8) continue;

            // End of image, or scan data, before any frame header.
            if (marker == 0xD9 || marker == 0xDA) return false;

            if (pos + 2 > data.Length) return false;
            int length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos, 2));
            if (length < 2) return false;

            // Every SOFn except DHT (C4), JPG (C8) and DAC (CC).
            if (marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                if (length < 7 || pos + 7 > data.Length) return false;
                height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos + 5, 2));
                return true;
            }

            pos += length;
        }
        return false;
    }

    private static bool TryReadWebP(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (IsAscii(data, 12, "VP8 "))
        {
            // A 3-byte frame tag, the start code, then 14-bit dimensions with 2 scale bits.
            if (data.Length < 30 || data[23] != 0x9D || data[24] != 0x01 || data[25] != 0x2A) return false;
            width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(26, 2)) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(28, 2)) & 0x3FFF;
            return true;
        }

        if (IsAscii(data, 12, "VP8L"))
        {
            // The signature byte, then 14 bits of width - 1 and 14 bits of height - 1.
            if (data.Length < 25 || data[20] != 0x2F) return false;
            uint bits = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(21, 4));
            width = (int)(bits & 0x3FFF) + 1;
            height = (int)((bits >> 14) & 0x3FFF) + 1;
            return true;
        }

        if (IsAscii(data, 12, "VP8X"))
        {
            // Flags and reserved bytes, then 24-bit canvas width - 1 and height - 1.
            if (data.Length < 30) return false;
            width = (data[24] | data[25] << 8 | data[26] << 16) + 1;
            height = (data[27] | data[28] << 8 | data[29] << 16) + 1;
            return true;
        }

        return false;
    }

    private static bool IsAscii(ReadOnlySpan<byte> data, int offset, string text)
    {
        if (data.Length < offset + text.Length) return false;
        for (int i = 0; i < text.Length; i++)
        {
            if (data[offset + i] != text[i]) return false;
        }
        return true;
    }
}

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Benchmarking;

namespace Overseer.Tests.Helpers;

/// <summary>Report chart stores for tests: one with no storage configured, and one in a throwaway temp folder.</summary>
public static class TestChartStores
{
    /// <summary>A store with an empty configuration: it refuses writes and loads nothing.</summary>
    public static BenchmarkReportChartStore Unconfigured()
        => new(ConfigurationFor(null));

    /// <summary>
    /// A store rooted in a new folder under the system temp directory, deleted on dispose.
    /// <paramref name="create"/> builds a store subclass from the configuration when a test needs one.
    /// </summary>
    public static TempChartStore InTempFolder(Func<IConfiguration, BenchmarkReportChartStore>? create = null)
        => new(create);

    /// <summary>A configuration whose only key is the chart root.</summary>
    public static IConfiguration ConfigurationFor(string? root)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { BenchmarkReportChartStore.ConfigurationKey, root }
            })
            .Build();
}

/// <summary>A chart store in its own temp folder; <see cref="Dispose"/> deletes the folder.</summary>
public sealed class TempChartStore : IDisposable
{
    public TempChartStore(Func<IConfiguration, BenchmarkReportChartStore>? create = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "OverseerChartTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        var configuration = TestChartStores.ConfigurationFor(Root);
        Store = create != null ? create(configuration) : new BenchmarkReportChartStore(configuration);
    }

    /// <summary>The chart root, an absolute path.</summary>
    public string Root { get; }

    public BenchmarkReportChartStore Store { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch
        {
            // A leftover temp folder does not fail a test.
        }
    }
}

/// <summary>Minimal valid PNG images: 8-bit grayscale, one black frame, correct chunk CRCs.</summary>
public static class TestPngs
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>A PNG of the given size; <paramref name="shade"/> fills every pixel, so different shades give different bytes.</summary>
    public static byte[] Make(int width, int height, byte shade = 0)
    {
        using var output = new MemoryStream();
        output.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
        header[8] = 8;  // bit depth
        header[9] = 0;  // grayscale
        header[10] = 0; // deflate
        header[11] = 0; // adaptive filtering
        header[12] = 0; // no interlace
        WriteChunk(output, "IHDR", header);

        using (var raw = new MemoryStream())
        {
            using (var zlib = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
            {
                var row = new byte[width + 1];
                Array.Fill(row, shade, 1, width);
                for (int y = 0; y < height; y++)
                {
                    zlib.Write(row);
                }
            }

            WriteChunk(output, "IDAT", raw.ToArray());
        }

        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    public static string MakeBase64(int width, int height, byte shade = 0)
        => Convert.ToBase64String(Make(width, height, shade));

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);

        var typeAndData = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++)
        {
            typeAndData[i] = (byte)type[i];
        }
        Buffer.BlockCopy(data, 0, typeAndData, 4, data.Length);
        output.Write(typeAndData);

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData));
        output.Write(crc);
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }
}

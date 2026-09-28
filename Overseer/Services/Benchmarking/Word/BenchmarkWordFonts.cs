namespace Overseer.Services.Benchmarking.Word;

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Overseer.Services.Benchmarking.Pdf;

/// <summary>
/// The Word documents' font table, with Source Sans 3 and Source Code Pro embedded the way Word
/// embeds a font (ECMA-376 Part 1, § 17.8.1): each face an obfuscated font part whose first 32 bytes
/// are XORed with a key taken from its <c>w:fontKey</c> GUID, referenced from the font table. The
/// faces are embedded whole, so the text stays editable in the same fonts.
///
/// <para>Each key is derived from the SHA-256 of its font file, so a face always embeds the same way.
/// The PDF's DejaVu Sans fallback is not embedded.</para>
/// </summary>
internal static class BenchmarkWordFonts
{
    public const string SansFamily = "Source Sans 3";
    public const string MonoFamily = "Source Code Pro";

    private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private enum Slot
    {
        Regular,
        Italic,
        Bold,
        BoldItalic
    }

    private sealed record Face(string FileName, Slot Slot);

    private sealed record Family(string Name, string AltName, FontFamilyValues Kind, FontPitchValues Pitch, Face[] Faces);

    // The first face of each family is its regular face, whose OS/2 table gives the family's
    // PANOSE number and signature.
    private static readonly Family[] Families =
    {
        new(SansFamily, "Calibri", FontFamilyValues.Swiss, FontPitchValues.Variable, new[]
        {
            new Face("SourceSans3-Regular.ttf", Slot.Regular),
            new Face("SourceSans3-It.ttf", Slot.Italic),
            new Face("SourceSans3-Bold.ttf", Slot.Bold),
            new Face("SourceSans3-BoldIt.ttf", Slot.BoldItalic),
        }),
        new(MonoFamily, "Consolas", FontFamilyValues.Modern, FontPitchValues.Fixed, new[]
        {
            new Face("SourceCodePro-Regular.ttf", Slot.Regular),
            new Face("SourceCodePro-Bold.ttf", Slot.Bold),
        }),
    };

    /// <summary>The font table of <paramref name="part"/>, every face added to it as an obfuscated font part.</summary>
    public static Fonts FontTable(FontTablePart part)
    {
        ArgumentNullException.ThrowIfNull(part);

        var fonts = new Fonts();
        fonts.AddNamespaceDeclaration("r", RelationshipsNamespace);

        foreach (var family in Families)
        {
            byte[] regular = BenchmarkPdfResources.FontFile(family.Faces[0].FileName);
            var os2 = Os2.Read(regular);

            var font = new Font { Name = family.Name };
            font.AltName = new AltName { Val = family.AltName };
            if (os2 != null)
            {
                font.Panose1Number = new Panose1Number { Val = os2.Panose };
            }
            font.FontCharSet = new FontCharSet { Val = "00" };
            font.FontFamily = new FontFamily { Val = family.Kind };
            font.Pitch = new Pitch { Val = family.Pitch };
            if (os2 != null)
            {
                font.FontSignature = new FontSignature
                {
                    UnicodeSignature0 = os2.UnicodeRanges[0],
                    UnicodeSignature1 = os2.UnicodeRanges[1],
                    UnicodeSignature2 = os2.UnicodeRanges[2],
                    UnicodeSignature3 = os2.UnicodeRanges[3],
                    CodePageSignature0 = os2.CodePageRanges[0],
                    CodePageSignature1 = os2.CodePageRanges[1]
                };
            }

            foreach (var face in family.Faces)
            {
                byte[] data = face.Slot == Slot.Regular ? regular : BenchmarkPdfResources.FontFile(face.FileName);
                string key = FontKey(data);

                var fontPart = part.AddFontPart(FontPartType.FontOdttf);
                using (var stream = new MemoryStream(Obfuscate(data, key), writable: false))
                {
                    fontPart.FeedData(stream);
                }
                string id = part.GetIdOfPart(fontPart);

                switch (face.Slot)
                {
                    case Slot.Regular:
                        font.EmbedRegularFont = new EmbedRegularFont { Id = id, FontKey = key };
                        break;
                    case Slot.Italic:
                        font.EmbedItalicFont = new EmbedItalicFont { Id = id, FontKey = key };
                        break;
                    case Slot.Bold:
                        font.EmbedBoldFont = new EmbedBoldFont { Id = id, FontKey = key };
                        break;
                    default:
                        font.EmbedBoldItalicFont = new EmbedBoldItalicFont { Id = id, FontKey = key };
                        break;
                }
            }

            fonts.Append(font);
        }

        return fonts;
    }

    /// <summary>
    /// The font's <c>w:fontKey</c>: a GUID made of the first 16 bytes of the file's SHA-256, in
    /// braces and upper case, as Word writes it.
    /// </summary>
    public static string FontKey(byte[] font)
    {
        ArgumentNullException.ThrowIfNull(font);
        byte[] hash = SHA256.HashData(font);
        return new Guid(hash.AsSpan(0, 16)).ToString("B").ToUpperInvariant();
    }

    /// <summary>
    /// A copy of <paramref name="font"/> with its first 32 bytes XORed with the key of
    /// <paramref name="fontKey"/>: the GUID's 16 bytes as written in the string, in reverse order, applied
    /// to bytes 0–15 and again to bytes 16–31. XOR is its own inverse, so the same call de-obfuscates.
    /// </summary>
    public static byte[] Obfuscate(byte[] font, string fontKey)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(fontKey);

        string hex = new(fontKey.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != 32)
        {
            throw new ArgumentException("The font key must be a GUID.", nameof(fontKey));
        }

        var key = new byte[16];
        for (int i = 0; i < 16; i++)
        {
            key[i] = byte.Parse(hex.AsSpan(30 - 2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        byte[] result = (byte[])font.Clone();
        for (int i = 0; i < 32 && i < result.Length; i++)
        {
            result[i] ^= key[i % 16];
        }
        return result;
    }

    /// <summary>The PANOSE number, Unicode ranges and code-page ranges of a TrueType font's OS/2 table, as hex.</summary>
    private sealed record Os2(string Panose, string[] UnicodeRanges, string[] CodePageRanges)
    {
        public static Os2? Read(byte[] font)
        {
            if (font.Length < 12) return null;
            int tables = ReadUInt16(font, 4);
            for (int t = 0; t < tables; t++)
            {
                int record = 12 + t * 16;
                if (record + 16 > font.Length) return null;
                if (font[record] != 'O' || font[record + 1] != 'S' || font[record + 2] != '/' || font[record + 3] != '2') continue;

                int offset = (int)ReadUInt32(font, record + 8);
                int length = (int)ReadUInt32(font, record + 12);
                if (offset < 0 || length < 58 || offset + length > font.Length) return null;

                string panose = Convert.ToHexString(font, offset + 32, 10);
                var unicode = Enumerable.Range(0, 4).Select(i => Hex32(ReadUInt32(font, offset + 42 + i * 4))).ToArray();
                int version = ReadUInt16(font, offset);
                var codePages = version >= 1 && length >= 86
                    ? new[] { Hex32(ReadUInt32(font, offset + 78)), Hex32(ReadUInt32(font, offset + 82)) }
                    : new[] { Hex32(0), Hex32(0) };
                return new Os2(panose, unicode, codePages);
            }
            return null;
        }

        private static int ReadUInt16(byte[] data, int offset) => (data[offset] << 8) | data[offset + 1];

        private static uint ReadUInt32(byte[] data, int offset)
            => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

        private static string Hex32(uint value) => value.ToString("X8", CultureInfo.InvariantCulture);
    }
}

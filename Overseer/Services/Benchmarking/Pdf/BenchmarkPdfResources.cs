namespace Overseer.Services.Benchmarking.Pdf;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using QuestPDF.Drawing;

/// <summary>
/// The fonts and logos the benchmark PDFs are drawn with, read from this assembly's embedded
/// resources under <c>Resources/Pdf</c> and registered with QuestPDF exactly once per process.
///
/// <para>Three families are used: <see cref="SansFamily"/> (Source Sans 3) for text,
/// <see cref="MonoFamily"/> (Source Code Pro) for code, and <see cref="FallbackFamily"/> (DejaVu
/// Sans) for the glyphs neither carries, such as check marks and box drawing. QuestPDF registers a
/// font under the family name stored in the file, so each name is resolved after registration from
/// <see cref="FontManager.GetRegisteredFonts"/> by the face's PostScript name, rather than assumed.</para>
/// </summary>
public static class BenchmarkPdfResources
{
    private const string WideLogoFile = "gnollbench-wide-v3-h256.webp";
    private const string SquareEmblemFile = "gnollbench-logo-v3-256.webp";

    private static readonly string[] FontFiles =
    {
        "SourceSans3-Regular.ttf",
        "SourceSans3-It.ttf",
        "SourceSans3-Semibold.ttf",
        "SourceSans3-Bold.ttf",
        "SourceSans3-BoldIt.ttf",
        "SourceCodePro-Regular.ttf",
        "SourceCodePro-Bold.ttf",
        "DejaVuSans.ttf",
        "DejaVuSans-Bold.ttf",
    };

    private sealed record Loaded(
        string SansFamily,
        string SemiboldFamily,
        string MonoFamily,
        string FallbackFamily,
        byte[] WideLogo,
        byte[] SquareEmblem);

    private static readonly Lazy<Loaded> State = new(Load);

    /// <summary>Registers the fonts and reads the logos on the first call; later calls do nothing.</summary>
    public static void EnsureRegistered() => _ = State.Value;

    /// <summary>The text family (Source Sans 3) as QuestPDF knows it.</summary>
    public static string SansFamily => State.Value.SansFamily;

    /// <summary>
    /// The family that holds the Source Sans 3 Semibold face: <see cref="SansFamily"/> when the face is
    /// listed under the typographic family name, else its own legacy family name.
    /// </summary>
    public static string SemiboldFamily => State.Value.SemiboldFamily;

    /// <summary>The code family (Source Code Pro) as QuestPDF knows it.</summary>
    public static string MonoFamily => State.Value.MonoFamily;

    /// <summary>The fallback family (DejaVu Sans) for glyphs the other two lack.</summary>
    public static string FallbackFamily => State.Value.FallbackFamily;

    /// <summary>The wide GnollBench logo, 978 × 256, transparent WebP.</summary>
    public static ReadOnlyMemory<byte> WideLogo => State.Value.WideLogo;

    /// <summary>The square GnollBench emblem, 256 × 256, transparent WebP.</summary>
    public static ReadOnlyMemory<byte> SquareEmblem => State.Value.SquareEmblem;

    private static Loaded Load()
    {
        // Only the embedded fonts, never the host's; a glyph none of them carries (an emoji in a
        // model's answer) renders as a replacement instead of failing the download.
        QuestPDF.Settings.UseSystemFonts = false;
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;

        var assembly = typeof(BenchmarkPdfResources).Assembly;
        string[] names = assembly.GetManifestResourceNames();

        foreach (string file in FontFiles)
        {
            FontManager.RegisterFontFromBinaryData(ReadResource(assembly, names, file));
        }

        var faces = FontManager.GetRegisteredFonts().ToList();

        string Family(string postScriptName, string preferred)
        {
            var familyNames = faces
                .Where(f => string.Equals(f.PostScriptName, postScriptName, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.FamilyName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();
            return familyNames.FirstOrDefault(n => string.Equals(n, preferred, StringComparison.OrdinalIgnoreCase))
                ?? familyNames.FirstOrDefault()
                ?? preferred;
        }

        string sans = Family("SourceSans3-Regular", "Source Sans 3");
        return new Loaded(
            SansFamily: sans,
            SemiboldFamily: Family("SourceSans3-Semibold", sans),
            MonoFamily: Family("SourceCodePro-Regular", "Source Code Pro"),
            FallbackFamily: Family("DejaVuSans", "DejaVu Sans"),
            WideLogo: ReadResource(assembly, names, WideLogoFile),
            SquareEmblem: ReadResource(assembly, names, SquareEmblemFile));
    }

    /// <summary>
    /// One embedded resource by file name. Manifest names carry the root namespace and folder path
    /// (<c>Overseer.Resources.Pdf.Fonts.SourceSans3-Regular.ttf</c>), so the file is matched as a suffix.
    /// </summary>
    private static byte[] ReadResource(Assembly assembly, IReadOnlyList<string> names, string fileName)
    {
        string suffix = "." + fileName;
        string? name = names.FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (name == null)
        {
            throw new InvalidOperationException(
                $"The embedded resource {fileName} is missing from {assembly.GetName().Name}; Resources/Pdf must be an EmbeddedResource.");
        }

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded resource {name} could not be opened.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}

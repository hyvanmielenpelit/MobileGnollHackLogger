namespace Overseer.Models;

/// <summary>The body of a PUT that replaces a report document's whole chart set.</summary>
public class PutReportDocumentChartsRequest
{
    public List<ReportDocumentChartUpload> Charts { get; set; } = new();
}

/// <summary>One chart image as the Model Comparison wizard uploads it.</summary>
public class ReportDocumentChartUpload
{
    /// <summary>One of the figure keys <c>BenchmarkReportChartPlacement</c> knows.</summary>
    public string FigureKey { get; set; } = string.Empty;

    /// <summary><c>named</c> or <c>anonymized</c>: the disclosure the image was drawn for.</summary>
    public string Naming { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string AltText { get; set; } = string.Empty;

    /// <summary>64 hex characters identifying the chart settings; the same for every chart of one upload.</summary>
    public string SettingsHash { get; set; } = string.Empty;

    /// <summary>The PNG, base64; a <c>data:image/png;base64,</c> prefix is accepted.</summary>
    public string PngBase64 { get; set; } = string.Empty;
}

/// <summary>An upload that passed validation, decoded and measured, ready to be written.</summary>
public sealed record ValidatedChart
{
    public string FigureKey { get; init; } = string.Empty;
    public string Naming { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Caption { get; init; } = string.Empty;
    public string AltText { get; init; } = string.Empty;

    /// <summary>Lowercase hex.</summary>
    public string SettingsHash { get; init; } = string.Empty;

    public byte[] Png { get; init; } = Array.Empty<byte>();
    public int WidthPx { get; init; }
    public int HeightPx { get; init; }

    /// <summary>SHA-256 of <see cref="Png"/>, lowercase hex.</summary>
    public string Sha256 { get; init; } = string.Empty;
}

/// <summary>A stored chart loaded for one render, in one naming.</summary>
public sealed record BenchmarkReportRenderChart
{
    public string FigureKey { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Caption { get; init; } = string.Empty;
    public string AltText { get; init; } = string.Empty;
    public byte[] Png { get; init; } = Array.Empty<byte>();
    public int WidthPx { get; init; }
    public int HeightPx { get; init; }

    /// <summary>SHA-256 of <see cref="Png"/>, lowercase hex.</summary>
    public string Sha256 { get; init; } = string.Empty;
}

/// <summary>A document's stored chart set, read from its manifest without the images.</summary>
public class ReportDocumentChartsSummaryDto
{
    public long DocumentId { get; set; }

    /// <summary>Images stored, both namings counted.</summary>
    public int ChartCount { get; set; }

    /// <summary>Distinct figure keys, in manifest order.</summary>
    public List<string> FigureKeys { get; set; } = new();

    public string SettingsHash { get; set; } = string.Empty;
}

/// <summary><c>manifest.json</c> in a document's chart folder.</summary>
public class BenchmarkReportChartManifest
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public long DocumentId { get; set; }
    public string SettingsHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public List<BenchmarkReportChartManifestEntry> Charts { get; set; } = new();
}

public class BenchmarkReportChartManifestEntry
{
    public string FigureKey { get; set; } = string.Empty;
    public string Naming { get; set; } = string.Empty;

    /// <summary>The file name inside the document's folder.</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>SHA-256 of the file, lowercase hex.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public int WidthPx { get; set; }
    public int HeightPx { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string AltText { get; set; } = string.Empty;
}

/// <summary>What clearing the chart storage deleted, or in a dry run would delete.</summary>
public class ReportChartClearResult
{
    /// <summary>Document folders and staging folders.</summary>
    public int FolderCount { get; set; }

    public int FileCount { get; set; }
    public long Bytes { get; set; }

    /// <summary>Document folders whose document no longer exists.</summary>
    public int OrphanFolderCount { get; set; }

    /// <summary>Entries in the chart root that are neither document folders nor <c>.staging</c>; never deleted.</summary>
    public List<string> LeftAlone { get; set; } = new();
}

/// <summary>A chart upload or write refused by the chart store; the message is safe to show the user.</summary>
public sealed class ChartStoreException : Exception
{
    public ChartStoreException(string message)
        : base(message)
    {
    }

    public ChartStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

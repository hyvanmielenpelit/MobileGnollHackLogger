namespace GnollHackServer.Data.Legal;

/// <summary>Where a document's required version comes from.</summary>
public enum LegalVersionSource
{
    /// <summary>The document's own <c>**Last updated:**</c> line, with the Git commit date as the fallback.</summary>
    DocumentDate,

    /// <summary>The date of the last Git commit that touched the file, so every edit asks again.</summary>
    GitCommitDate
}

/// <summary>One Terms document a host serves.</summary>
public sealed class LegalDocumentDefinition
{
    /// <summary>The wiki page name; the file is <c>&lt;WikiPath&gt;/&lt;WikiPageName&gt;.md</c>.</summary>
    public string WikiPageName { get; set; } = "";

    /// <summary>The wiki page of the privacy policy the acceptance statement names.</summary>
    public string? PrivacyPolicyPageName { get; set; }
}

/// <summary>
/// The <c>LegalDocuments</c> section. Each host lists only the documents it serves; the wiki folder
/// comes from the host's <c>WikiPath</c> key.
/// </summary>
public sealed class LegalDocumentOptions
{
    public const string SectionName = "LegalDocuments";

    public string WikiBaseUrl { get; set; } = "https://wiki.gnollhack.com/";

    public LegalVersionSource VersionSource { get; set; } = LegalVersionSource.DocumentDate;

    public string GitExecutable { get; set; } = "git";

    /// <summary>Keyed by document key, such as <c>overseer-terms</c>.</summary>
    public Dictionary<string, LegalDocumentDefinition> Documents { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>The document keys and acceptance sources the record uses.</summary>
public static class LegalDocumentKeys
{
    public const string OverseerTerms = "overseer-terms";
    public const string AccountTerms = "account-terms";

    public const string SourceWeb = "Web";
    public const string SourceGnollHackApp = "GnollHackApp";
    public const string SourceAccountSite = "AccountSite";
}

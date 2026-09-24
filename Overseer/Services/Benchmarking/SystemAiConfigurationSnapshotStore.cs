namespace Overseer.Services.Benchmarking;

using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

/// <summary>
/// Captures, deduplicates and applies <see cref="SystemAiConfigurationSnapshot"/> rows: the
/// recorded settings every benchmark model call runs with.
/// </summary>
public static class SystemAiConfigurationSnapshotStore
{
    public const string CanonicalHeader = "SystemAiConfigurationSnapshot/1";

    // Which snapshot each bound configuration was built from, so a writer can record it without
    // every call path carrying the pair.
    private static readonly ConditionalWeakTable<SystemAiApiConfiguration, SystemAiConfigurationSnapshot> Bindings = new();

    /// <summary>The message every benchmark path reports when a live key cannot call the recorded model.</summary>
    public const string MismatchMessage =
        "The configuration's provider or endpoint changed since this run was launched; its key cannot call the recorded model.";

    /// <summary>A complete, unsaved snapshot of the live configuration's settings.</summary>
    public static SystemAiConfigurationSnapshot FromConfiguration(SystemAiApiConfiguration c)
    {
        var snapshot = new SystemAiConfigurationSnapshot
        {
            IsComplete = true,
            Provider = c.Provider,
            ModelId = c.ModelId,
            DisplayName = c.DisplayName,
            ThinkingLevel = c.ThinkingLevel,
            ReasoningMode = c.ReasoningMode,
            ReasoningSummary = c.ReasoningSummary,
            ServiceTier = c.ServiceTier,
            MaxOutputTokens = c.MaxOutputTokens,
            ParallelExecutionMode = c.ParallelExecutionMode,
            BaseUrl = NullIfBlank(c.BaseUrl),
            ApiVersion = NullIfBlank(c.ApiVersion),
            CustomHeadersJson = NullIfBlank(c.CustomHeadersJson),
            CreatedAtUtc = DateTime.UtcNow
        };
        snapshot.Sha256 = ComputeSha256(snapshot);
        return snapshot;
    }

    /// <summary>
    /// The canonical form: a header line, then one <c>Name=value</c> line per non-null field in
    /// ordinal name order, values exact, integers in invariant decimal, <c>IsComplete</c> as 0 or 1.
    /// Omitting null fields keeps every existing hash stable when a nullable field is added.
    /// </summary>
    public static string Canonicalize(SystemAiConfigurationSnapshot s)
    {
        var sb = new StringBuilder();
        sb.Append(CanonicalHeader).Append('\n');
        Line(sb, "ApiVersion", s.ApiVersion);
        Line(sb, "BaseUrl", s.BaseUrl);
        Line(sb, "CustomHeadersJson", s.CustomHeadersJson);
        Line(sb, "DisplayName", s.DisplayName);
        Line(sb, "IsComplete", s.IsComplete ? "1" : "0");
        Line(sb, "MaxOutputTokens", s.MaxOutputTokens?.ToString(CultureInfo.InvariantCulture));
        Line(sb, "ModelId", s.ModelId);
        Line(sb, "ParallelExecutionMode", s.ParallelExecutionMode.HasValue
            ? ((int)s.ParallelExecutionMode.Value).ToString(CultureInfo.InvariantCulture)
            : null);
        Line(sb, "Provider", s.Provider);
        Line(sb, "ReasoningMode", s.ReasoningMode);
        Line(sb, "ReasoningSummary", s.ReasoningSummary);
        Line(sb, "ServiceTier", s.ServiceTier);
        Line(sb, "ThinkingLevel", s.ThinkingLevel);
        return sb.ToString();
    }

    /// <summary>
    /// Lower-case hex SHA-256 of the UTF-16LE canonical form. Byte-identical to the migration's
    /// <c>HASHBYTES('SHA2_256', nvarchar)</c>, which is also UTF-16LE.
    /// </summary>
    public static string ComputeSha256(SystemAiConfigurationSnapshot s)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.Unicode.GetBytes(Canonicalize(s))));

    /// <summary>
    /// The stored row with the candidate's hash, or the candidate itself once added. A context
    /// with no other pending changes saves at once, so a concurrent capture of the same settings
    /// is resolved here by the unique index; otherwise the insert rides on the caller's save.
    /// </summary>
    public static async Task<SystemAiConfigurationSnapshot> GetOrCreateAsync(
        ApplicationDbContext db, SystemAiConfigurationSnapshot candidate, CancellationToken ct)
    {
        candidate.Sha256 = ComputeSha256(candidate);

        var local = db.SystemAiConfigurationSnapshots.Local.FirstOrDefault(s => s.Sha256 == candidate.Sha256);
        if (local != null)
            return local;

        var stored = await db.SystemAiConfigurationSnapshots.FirstOrDefaultAsync(s => s.Sha256 == candidate.Sha256, ct);
        if (stored != null)
            return stored;

        bool saveNow = !db.ChangeTracker.HasChanges();
        db.SystemAiConfigurationSnapshots.Add(candidate);
        if (!saveNow)
            return candidate;

        try
        {
            await db.SaveChangesAsync(ct);
            return candidate;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.Entry(candidate).State = EntityState.Detached;
            return await db.SystemAiConfigurationSnapshots.FirstAsync(s => s.Sha256 == candidate.Sha256, ct);
        }
    }

    /// <summary>Captures the live configuration's current settings.</summary>
    public static Task<SystemAiConfigurationSnapshot> CaptureAsync(
        ApplicationDbContext db, SystemAiApiConfiguration live, CancellationToken ct)
        => GetOrCreateAsync(db, FromConfiguration(live), ct);

    /// <summary>
    /// <see cref="CaptureAsync"/>, saved, so the returned row carries its stored id. Saves the
    /// context's other pending changes with it.
    /// </summary>
    public static async Task<SystemAiConfigurationSnapshot> CaptureAndSaveAsync(
        ApplicationDbContext db, SystemAiApiConfiguration live, CancellationToken ct)
    {
        var snapshot = await CaptureAsync(db, live, ct);
        if (db.Entry(snapshot).State == EntityState.Added)
            await db.SaveChangesAsync(ct);
        return snapshot;
    }

    /// <summary>
    /// The configuration a benchmark call runs with: settings and endpoint from the recorded
    /// snapshot, credentials and rate-limit identity from the live row. Returns a new, untracked
    /// object. A key is only valid for the provider and endpoint it was issued for, so a live row
    /// whose provider or endpoint moved away from the snapshot's throws
    /// <see cref="InvalidOperationException"/> with <see cref="MismatchMessage"/>.
    /// </summary>
    /// <remarks>
    /// On an incomplete (backfilled) snapshot a null field means "not recorded", so it takes the
    /// live value instead of sending an unset one.
    /// </remarks>
    public static SystemAiApiConfiguration Bind(SystemAiApiConfiguration live, SystemAiConfigurationSnapshot s)
    {
        if (!string.Equals(live.Provider, s.Provider, StringComparison.Ordinal)
            || !string.Equals(NullIfBlank(live.BaseUrl), s.BaseUrl, StringComparison.Ordinal)
            || !string.Equals(NullIfBlank(live.ApiVersion), s.ApiVersion, StringComparison.Ordinal)
            || !string.Equals(NullIfBlank(live.CustomHeadersJson), s.CustomHeadersJson, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(MismatchMessage);
        }

        string? Pick(string? recorded, string? current) => s.IsComplete ? recorded : recorded ?? current;

        var bound = new SystemAiApiConfiguration
        {
            Id = live.Id,
            EncryptedApiKey = live.EncryptedApiKey,
            ApiKeyNonce = live.ApiKeyNonce,
            ApiKeyTag = live.ApiKeyTag,
            IsEnabled = live.IsEnabled,
            ModelRole = live.ModelRole,
            IsSystemWide = live.IsSystemWide,
            MaxInputTokens = live.MaxInputTokens,
            PricingMode = live.PricingMode,
            InputPricePerMillion = live.InputPricePerMillion,
            OutputPricePerMillion = live.OutputPricePerMillion,
            CachedInputPricePerMillion = live.CachedInputPricePerMillion,
            ConfidentialityPosture = live.ConfidentialityPosture,
            DataRegion = live.DataRegion,

            Provider = s.Provider,
            ModelId = s.ModelId,
            DisplayName = s.DisplayName ?? live.DisplayName,
            DisplayNameMode = live.DisplayNameMode,
            ThinkingLevel = Pick(s.ThinkingLevel, live.ThinkingLevel),
            ReasoningMode = Pick(s.ReasoningMode, live.ReasoningMode),
            ReasoningSummary = Pick(s.ReasoningSummary, live.ReasoningSummary),
            ServiceTier = Pick(s.ServiceTier, live.ServiceTier),
            MaxOutputTokens = s.IsComplete ? s.MaxOutputTokens : s.MaxOutputTokens ?? live.MaxOutputTokens,
            ParallelExecutionMode = s.ParallelExecutionMode ?? live.ParallelExecutionMode,
            BaseUrl = s.BaseUrl,
            ApiVersion = s.ApiVersion,
            CustomHeadersJson = s.CustomHeadersJson,
        };
        Bindings.AddOrUpdate(bound, s);
        return bound;
    }

    /// <summary>The snapshot <paramref name="config"/> was bound from, or null for a live row.</summary>
    public static SystemAiConfigurationSnapshot? SnapshotOf(SystemAiApiConfiguration config)
        => Bindings.TryGetValue(config, out var s) ? s : null;

    /// <summary>
    /// The instance of <paramref name="snapshot"/> to assign to a navigation of an entity tracked by
    /// <paramref name="db"/>: the one <paramref name="db"/> already tracks with the same id, or the
    /// given one attached as unchanged. A snapshot loaded by another context is safe to record here.
    /// </summary>
    public static SystemAiConfigurationSnapshot? Track(ApplicationDbContext db, SystemAiConfigurationSnapshot? snapshot)
    {
        if (snapshot == null || snapshot.Id <= 0)
            return snapshot;

        var local = db.SystemAiConfigurationSnapshots.Local.FirstOrDefault(s => s.Id == snapshot.Id);
        if (local != null)
            return local;

        var entry = db.Entry(snapshot);
        if (entry.State == EntityState.Detached)
            entry.State = EntityState.Unchanged;
        return snapshot;
    }

    /// <summary>
    /// <see cref="Bind"/>, or the error text a benchmark path reports when the configuration is
    /// missing or no longer matches.
    /// </summary>
    public static bool TryBind(
        SystemAiApiConfiguration? live, SystemAiConfigurationSnapshot? snapshot,
        out SystemAiApiConfiguration? bound, out string? error)
    {
        bound = null;
        if (live == null)
        {
            error = "The model configuration was not found.";
            return false;
        }

        if (snapshot == null)
        {
            bound = live;
            error = null;
            return true;
        }

        try
        {
            bound = Bind(live, snapshot);
            error = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// <c>official</c>, or <c>custom-</c> and the first 12 hex characters of the SHA-256 of the
    /// UTF-16LE <c>BaseUrl\nApiVersion\nCustomHeadersJson</c>. Carries no hostname, and no
    /// <c>;</c> or <c>=</c>, so it is safe in a comparability key and in a shared report.
    /// </summary>
    public static string EndpointFingerprint(SystemAiConfigurationSnapshot? s)
    {
        if (s == null || (s.BaseUrl == null && s.CustomHeadersJson == null))
            return "official";

        string text = (s.BaseUrl ?? "") + "\n" + (s.ApiVersion ?? "") + "\n" + (s.CustomHeadersJson ?? "");
        return "custom-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.Unicode.GetBytes(text)))[..12];
    }

    /// <summary>
    /// "official", or "custom (Azure OpenAI, api-version X; fingerprint abc123def456)". Never the
    /// hostname: exported reports are shared.
    /// </summary>
    public static string DescribeEndpoint(SystemAiConfigurationSnapshot? s)
    {
        string fingerprint = EndpointFingerprint(s);
        if (fingerprint == "official")
            return "official";

        string kind = s!.ApiVersion != null ? $"Azure OpenAI, api-version {s.ApiVersion}" : "bearer token";
        return $"custom ({kind}; fingerprint {fingerprint["custom-".Length..]})";
    }

    /// <summary>The name a report or DTO shows for the model: its display name, else its model id.</summary>
    public static string? Label(this SystemAiConfigurationSnapshot? s) => s?.DisplayName ?? s?.ModelId;

    private static void Line(StringBuilder sb, string name, string? value)
    {
        if (value != null)
            sb.Append(name).Append('=').Append(value).Append('\n');
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

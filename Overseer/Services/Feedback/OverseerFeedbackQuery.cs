namespace Overseer.Services.Feedback;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>The wire values of <see cref="OverseerFeedbackReport.ModelSource"/> and of the Model source filter.</summary>
public static class FeedbackModelSources
{
    /// <summary>The turn ran on a system AI configuration.</summary>
    public const string System = "system";

    /// <summary>The turn ran on the user's own model.</summary>
    public const string User = "user";

    /// <summary>Filter value only: no source was recorded (a null column).</summary>
    public const string Unknown = "unknown";
}

/// <summary>
/// The limits of the triage text fields, shared by every path that writes them. An absent value keeps the stored
/// one; an empty or blank value clears it.
/// </summary>
public static class FeedbackTriageFields
{
    public const int MaxNoteLength = 1000;
    public const int MaxResolutionLength = 4000;
    public const int MaxFixedInVersionLength = 64;
    public const string FixedInVersionPattern = @"^[0-9A-Za-z.+\-]{1,64}$";

    private static readonly Regex FixedInVersionRegex = new(FixedInVersionPattern, RegexOptions.CultureInvariant);

    /// <summary>Whether a trimmed, non-empty fixed-in version is well formed.</summary>
    public static bool IsValidFixedInVersion(string value) => FixedInVersionRegex.IsMatch(value);

    /// <summary>
    /// Reads one optional text field. <paramref name="present"/> is false when <paramref name="input"/> is null
    /// (keep); otherwise <paramref name="value"/> is the trimmed text, or null when it was blank (clear). False with
    /// <paramref name="error"/> when the trimmed text is longer than <paramref name="maxLength"/>.
    /// </summary>
    public static bool TryRead(string? input, int maxLength, string label,
        out bool present, out string? value, [NotNullWhen(false)] out string? error)
    {
        error = null;
        value = null;
        present = input != null;
        if (!present) return true;

        string trimmed = input!.Trim();
        if (trimmed.Length == 0) return true;
        if (trimmed.Length > maxLength)
        {
            error = $"The {label} can be at most {maxLength} characters.";
            return false;
        }
        value = trimmed;
        return true;
    }

    /// <summary><see cref="TryRead"/> for the fixed-in version, which must also match <c>^[0-9A-Za-z.+\-]{1,64}$</c>.</summary>
    public static bool TryReadFixedInVersion(string? input, out bool present, out string? value, [NotNullWhen(false)] out string? error)
    {
        if (!TryRead(input, MaxFixedInVersionLength, "fixed-in version", out present, out value, out error)) return false;
        if (value != null && !IsValidFixedInVersion(value))
        {
            error = "The fixed-in version may contain only letters, digits, '.', '+' and '-'.";
            return false;
        }
        return true;
    }
}

/// <summary>The sort keys of the Overseer feedback issue list.</summary>
public static class OverseerFeedbackSorts
{
    public const string LastSeen = "lastSeen";
    public const string FirstSeen = "firstSeen";
    public const string Count = "count";
    public const string Users = "users";
    public const string Regressions = "regressions";
    public const string Triaged = "triaged";
    public const string Subject = "subject";

    public static IReadOnlyList<string> All { get; } = new[] { LastSeen, FirstSeen, Count, Users, Regressions, Triaged, Subject };

    /// <summary>Parses a sort key ignoring case; null or blank is <see cref="LastSeen"/>.</summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out string? key)
    {
        if (string.IsNullOrWhiteSpace(value)) { key = LastSeen; return true; }
        key = All.FirstOrDefault(s => string.Equals(s, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return key != null;
    }
}

/// <summary>
/// The parsed filters of the Overseer feedback issue list. Empty lists and nulls filter nothing. The
/// occurrence-level filters (<see cref="ModelIds"/> through <see cref="Tools"/>) match an issue when one retained
/// occurrence matches all of them together.
/// </summary>
public sealed class OverseerFeedbackFilter
{
    public const int MaxSearchLength = 200;
    public const int MaxIssueIds = 500;
    public const int MaxListValues = 100;
    public const int MaxValueLength = 256;
    public const string NoGroup = "none";
    public const string WithResolution = "with";
    public const string WithoutResolution = "without";

    /// <summary>No filter at all.</summary>
    public static OverseerFeedbackFilter None { get; } = new();

    /// <summary>An issue is in the range when its active span overlaps it.</summary>
    public DateTime? StartDate { get; init; }

    /// <summary>Inclusive: the whole day.</summary>
    public DateTime? EndDate { get; init; }
    public IReadOnlyList<OverseerFeedbackStatus> Statuses { get; init; } = Array.Empty<OverseerFeedbackStatus>();
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ModelIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Providers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UserIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<long> GroupIds { get; init; } = Array.Empty<long>();

    /// <summary>Also match occurrences whose user is in no group, or that have no user.</summary>
    public bool IncludeNoGroup { get; init; }

    /// <summary><see cref="FeedbackModelSources"/> values; <c>unknown</c> matches a null source.</summary>
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();
    public IReadOnlyList<long> ConfigurationIds { get; init; } = Array.Empty<long>();
    public IReadOnlyList<string> Versions { get; init; } = Array.Empty<string>();

    /// <summary>Related tool names, lowercase.</summary>
    public IReadOnlyList<string> Tools { get; init; } = Array.Empty<string>();

    /// <summary>True: a non-empty resolution; false: none; null: either.</summary>
    public bool? HasResolution { get; init; }

    /// <summary><see cref="FeedbackResolvedVia"/> ids; an issue matches when it has any of them.</summary>
    public IReadOnlyList<string> ResolvedVia { get; init; } = Array.Empty<string>();

    /// <summary>Also match issues with no stored resolved-via ids.</summary>
    public bool IncludeNoResolvedVia { get; init; }
    public IReadOnlyList<long> IssueIds { get; init; } = Array.Empty<long>();

    /// <summary>Trimmed; at most <see cref="MaxSearchLength"/> characters.</summary>
    public string? Search { get; init; }

    /// <summary>Whether any occurrence-level filter is set.</summary>
    public bool HasOccurrenceFilters =>
        ModelIds.Count > 0 || Providers.Count > 0 || UserIds.Count > 0 || GroupIds.Count > 0 || IncludeNoGroup
        || Sources.Count > 0 || ConfigurationIds.Count > 0 || Versions.Count > 0 || Tools.Count > 0;

    /// <summary>Parses the query values; false with a user-facing <paramref name="error"/> for an invalid one.</summary>
    public static bool TryParse(AdminFeedbackFilterQuery? query, out OverseerFeedbackFilter filter, [NotNullWhen(false)] out string? error)
    {
        filter = None;
        error = null;
        if (query == null) return true;

        var statuses = new List<OverseerFeedbackStatus>();
        foreach (var part in Split(query.Status))
        {
            if (!AdminFeedbackStatuses.TryParse(part, out var parsed)) { error = $"Unknown status '{part}'."; return false; }
            if (!statuses.Contains(parsed)) statuses.Add(parsed);
        }

        var categories = Split(query.Category);
        foreach (var category in categories)
        {
            if (!FeedbackCategories.IsKnown(category)) { error = $"Unknown category '{category}'."; return false; }
        }

        if (!TryStrings(query.ModelId, "model", out var models, out error)) return false;
        if (!TryStrings(query.Provider, "provider", out var providers, out error)) return false;
        if (!TryStrings(query.User, "user", out var users, out error)) return false;
        if (!TryStrings(query.Version, "version", out var versions, out error)) return false;
        if (!TryStrings(query.Tool, "tool", out var toolNames, out error)) return false;
        var tools = toolNames.Select(t => t.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();

        var groupIds = new List<long>();
        bool noGroup = false;
        foreach (var part in Split(query.Group))
        {
            if (string.Equals(part, NoGroup, StringComparison.OrdinalIgnoreCase)) { noGroup = true; continue; }
            if (!long.TryParse(part, out long groupId) || groupId <= 0) { error = $"Unknown group '{part}'."; return false; }
            if (!groupIds.Contains(groupId)) groupIds.Add(groupId);
        }
        if (groupIds.Count > MaxListValues) { error = $"At most {MaxListValues} groups can be chosen."; return false; }

        var sources = new List<string>();
        foreach (var part in Split(query.Source))
        {
            string lowered = part.ToLowerInvariant();
            if (lowered is not (FeedbackModelSources.System or FeedbackModelSources.User or FeedbackModelSources.Unknown))
            {
                error = $"Unknown model source '{part}'.";
                return false;
            }
            if (!sources.Contains(lowered)) sources.Add(lowered);
        }

        if (!TryIds(query.Config, "configuration", MaxListValues, out var configIds, out error)) return false;
        if (!TryIds(query.Ids, "issue id", MaxIssueIds, out var issueIds, out error)) return false;

        bool? hasResolution = null;
        if (!string.IsNullOrWhiteSpace(query.Resolution))
        {
            string value = query.Resolution.Trim();
            if (string.Equals(value, WithResolution, StringComparison.OrdinalIgnoreCase)) hasResolution = true;
            else if (string.Equals(value, WithoutResolution, StringComparison.OrdinalIgnoreCase)) hasResolution = false;
            else { error = $"Unknown resolution filter '{value}'."; return false; }
        }

        if (!FeedbackResolvedVia.TryParseFilter(query.ResolvedVia, out var resolvedVia, out bool noResolvedVia, out error)) return false;

        string? search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if (search != null && search.Length > MaxSearchLength)
        {
            error = $"The search can be at most {MaxSearchLength} characters.";
            return false;
        }

        filter = new OverseerFeedbackFilter
        {
            StartDate = query.StartDate,
            EndDate = query.EndDate,
            Statuses = statuses,
            Categories = categories,
            ModelIds = models,
            Providers = providers,
            UserIds = users,
            GroupIds = groupIds,
            IncludeNoGroup = noGroup,
            Sources = sources,
            ConfigurationIds = configIds,
            Versions = versions,
            Tools = tools,
            HasResolution = hasResolution,
            ResolvedVia = resolvedVia,
            IncludeNoResolvedVia = noResolvedVia,
            IssueIds = issueIds,
            Search = search
        };
        return true;
    }

    private static List<string> Split(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new List<string>();
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static bool TryStrings(string? value, string label, out List<string> values, [NotNullWhen(false)] out string? error)
    {
        values = Split(value);
        error = null;
        if (values.Count > MaxListValues) { error = $"At most {MaxListValues} values can be chosen for {label}."; return false; }
        if (values.Any(v => v.Length > MaxValueLength)) { error = $"A {label} value can be at most {MaxValueLength} characters."; return false; }
        return true;
    }

    private static bool TryIds(string? value, string label, int max, out List<long> ids, [NotNullWhen(false)] out string? error)
    {
        ids = new List<long>();
        error = null;
        foreach (var part in Split(value))
        {
            if (!long.TryParse(part, out long id) || id <= 0) { error = $"Invalid {label} '{part}'."; return false; }
            if (!ids.Contains(id)) ids.Add(id);
        }
        if (ids.Count > max) { error = $"At most {max} values can be given for {label}."; return false; }
        return true;
    }
}

/// <summary>
/// The Overseer feedback issue queries shared by the admin list, its id list and the Markdown export: the filters,
/// the sorts, the facets, the occurrences and the row DTOs. Scoped, over the request's <see cref="ApplicationDbContext"/>.
/// Every query is untracked.
/// </summary>
public sealed class OverseerFeedbackQuery
{
    public const int MaxModelIdsShown = 5;
    public const string NoGroupName = "No group";

    private readonly ApplicationDbContext _db;
    private readonly ModelMetadataService? _metadata;

    public OverseerFeedbackQuery(ApplicationDbContext db, ModelMetadataService? metadata = null)
    {
        _db = db;
        _metadata = metadata;
    }

    /// <summary>The display name of a configuration that no longer exists.</summary>
    public static string DeletedConfigurationName(long id) => $"Configuration #{id} (deleted)";

    /// <summary>The catalog's display name of a model; the id without a service, a provider or a catalog name.</summary>
    public string ModelLabel(string? provider, string modelId)
    {
        if (_metadata == null || string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(modelId)) return modelId;
        string name = _metadata.GetMetadata(provider, modelId).DisplayName;
        return string.IsNullOrWhiteSpace(name) ? modelId : name;
    }

    // --- Issues ---

    /// <summary>The issues matching every filter, unsorted.</summary>
    public IQueryable<OverseerFeedbackIssue> Issues(OverseerFeedbackFilter filter)
    {
        var query = InRange(filter.StartDate, filter.EndDate);

        var statuses = filter.Statuses;
        if (statuses.Count > 0) query = query.Where(i => statuses.Contains(i.Status));
        var categories = filter.Categories;
        if (categories.Count > 0) query = query.Where(i => categories.Contains(i.Category));
        var issueIds = filter.IssueIds;
        if (issueIds.Count > 0) query = query.Where(i => issueIds.Contains(i.Id));

        if (filter.HasResolution == true) query = query.Where(i => i.Resolution != null && i.Resolution != "");
        else if (filter.HasResolution == false) query = query.Where(i => i.Resolution == null || i.Resolution == "");

        if (filter.ResolvedVia.Count > 0 || filter.IncludeNoResolvedVia)
            query = query.Where(HasAnyResolvedVia(filter.ResolvedVia, filter.IncludeNoResolvedVia));

        if (filter.HasOccurrenceFilters)
        {
            // One subquery carries every occurrence filter, so a single occurrence has to match all of them.
            var matching = MatchingOccurrences(filter);
            query = query.Where(i => matching.Any(r => r.IssueId == i.Id));
        }

        if (filter.Search != null)
        {
            // Lowered on both sides so the match ignores case on every provider, whatever the collation.
            var lowered = filter.Search.ToLowerInvariant();
            var reports = _db.OverseerFeedbackReports;
            query = query.Where(i => i.Subject.ToLower().Contains(lowered) || i.Summary.ToLower().Contains(lowered)
                || reports.Any(r => r.IssueId == i.Id
                    && ((r.Details != null && r.Details.ToLower().Contains(lowered))
                        || (r.SearchedFor != null && r.SearchedFor.ToLower().Contains(lowered))
                        || (r.ExpectedLocation != null && r.ExpectedLocation.ToLower().Contains(lowered)))));
        }

        return query;
    }

    /// <summary>The issues matching <paramref name="filter"/> in the order of <paramref name="sortKey"/>, an <see cref="OverseerFeedbackSorts"/> key.</summary>
    public IOrderedQueryable<OverseerFeedbackIssue> Sorted(OverseerFeedbackFilter filter, string sortKey)
    {
        var query = Issues(filter);
        var reports = _db.OverseerFeedbackReports;
        return sortKey switch
        {
            OverseerFeedbackSorts.FirstSeen => query.OrderByDescending(i => i.FirstSeenUtc).ThenByDescending(i => i.Id),
            OverseerFeedbackSorts.Count => query.OrderByDescending(i => i.OccurrenceCount).ThenByDescending(i => i.LastSeenUtc).ThenByDescending(i => i.Id),
            OverseerFeedbackSorts.Users => query
                .OrderByDescending(i => reports.Where(r => r.IssueId == i.Id && r.AspNetUserId != null).Select(r => r.AspNetUserId).Distinct().Count())
                .ThenByDescending(i => i.LastSeenUtc).ThenByDescending(i => i.Id),
            OverseerFeedbackSorts.Regressions => query.OrderByDescending(i => i.RegressionCount)
                .ThenByDescending(i => i.LastRegressionUtc).ThenByDescending(i => i.LastSeenUtc).ThenByDescending(i => i.Id),
            // Untriaged issues last, on every provider.
            OverseerFeedbackSorts.Triaged => query.OrderBy(i => i.TriagedUtc == null).ThenByDescending(i => i.TriagedUtc).ThenByDescending(i => i.Id),
            OverseerFeedbackSorts.Subject => query.OrderBy(i => i.Subject).ThenByDescending(i => i.LastSeenUtc).ThenByDescending(i => i.Id),
            _ => query.OrderByDescending(i => i.LastSeenUtc).ThenByDescending(i => i.Id)
        };
    }

    /// <summary>One page of the sorted issues with the total that match.</summary>
    public async Task<(int TotalCount, List<OverseerFeedbackIssue> Issues)> PageAsync(
        OverseerFeedbackFilter filter, string sortKey, int skip, int take, CancellationToken ct)
    {
        int total = await Issues(filter).CountAsync(ct);
        var issues = await Sorted(filter, sortKey).Skip(Math.Max(0, skip)).Take(Math.Max(1, take)).ToListAsync(ct);
        return (total, issues);
    }

    /// <summary>The ids of the sorted issues, at most <paramref name="cap"/>, with the total that match.</summary>
    public async Task<AdminFeedbackIssueIdsDto> IdsAsync(OverseerFeedbackFilter filter, string sortKey, int cap, CancellationToken ct)
    {
        int total = await Issues(filter).CountAsync(ct);
        var ids = await Sorted(filter, sortKey).Select(i => i.Id).Take(Math.Max(0, cap)).ToListAsync(ct);
        return new AdminFeedbackIssueIdsDto { Ids = ids, TotalCount = total, Truncated = total > ids.Count };
    }

    // --- Occurrences ---

    /// <summary>Every occurrence that matches all of the filter's occurrence-level filters; its other filters are ignored.</summary>
    public IQueryable<OverseerFeedbackReport> MatchingOccurrences(OverseerFeedbackFilter filter)
    {
        var query = _db.OverseerFeedbackReports.AsNoTracking();

        var models = filter.ModelIds;
        if (models.Count > 0) query = query.Where(r => models.Contains(r.ModelId!));
        var providers = filter.Providers;
        if (providers.Count > 0) query = query.Where(r => providers.Contains(r.Provider!));
        var users = filter.UserIds;
        if (users.Count > 0) query = query.Where(r => users.Contains(r.AspNetUserId!));
        var versions = filter.Versions;
        if (versions.Count > 0) query = query.Where(r => versions.Contains(r.AppVersion!));
        var configs = filter.ConfigurationIds;
        if (configs.Count > 0) query = query.Where(r => r.SystemAiConfigurationId != null && configs.Contains(r.SystemAiConfigurationId.Value));

        if (filter.GroupIds.Count > 0 || filter.IncludeNoGroup)
        {
            var groupIds = filter.GroupIds;
            var userGroups = _db.UserGroups;
            if (groupIds.Count > 0 && filter.IncludeNoGroup)
                query = query.Where(r => userGroups.Any(ug => ug.AspNetUserId == r.AspNetUserId && groupIds.Contains(ug.GroupId))
                    || !userGroups.Any(ug => ug.AspNetUserId == r.AspNetUserId));
            else if (groupIds.Count > 0)
                query = query.Where(r => userGroups.Any(ug => ug.AspNetUserId == r.AspNetUserId && groupIds.Contains(ug.GroupId)));
            else
                query = query.Where(r => !userGroups.Any(ug => ug.AspNetUserId == r.AspNetUserId));
        }

        if (filter.Sources.Count > 0)
        {
            var known = filter.Sources.Where(s => s != FeedbackModelSources.Unknown).ToList();
            bool unknown = filter.Sources.Contains(FeedbackModelSources.Unknown);
            if (known.Count > 0 && unknown) query = query.Where(r => r.ModelSource == null || known.Contains(r.ModelSource!));
            else if (known.Count > 0) query = query.Where(r => known.Contains(r.ModelSource!));
            else query = query.Where(r => r.ModelSource == null);
        }

        if (filter.Tools.Count > 0) query = query.Where(RelatedToAnyTool(filter.Tools));

        return query;
    }

    /// <summary>
    /// One issue's occurrences, newest first. With a <paramref name="filter"/>, only those matching all of its
    /// occurrence-level filters.
    /// </summary>
    public IOrderedQueryable<OverseerFeedbackReport> Occurrences(long issueId, OverseerFeedbackFilter? filter = null)
        => MatchingOccurrences(filter ?? OverseerFeedbackFilter.None)
            .Where(r => r.IssueId == issueId)
            .OrderByDescending(r => r.CreatedUtc).ThenByDescending(r => r.Id);

    // --- Facets ---

    /// <summary>
    /// The options present among the issues in the range, each with the number of those issues that have it: the
    /// issue's own category, and for the other facets any of its retained occurrences.
    /// </summary>
    public async Task<AdminFeedbackFacetsDto> FacetsAsync(DateTime? startDate, DateTime? endDate, CancellationToken ct)
    {
        var issues = InRange(startDate, endDate);
        var reports = _db.OverseerFeedbackReports.AsNoTracking().Where(r => issues.Any(i => i.Id == r.IssueId));

        var categoryCounts = await issues.GroupBy(i => i.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Category, x => x.Count, ct);

        var modelPairs = await reports.Where(r => r.ModelId != null && r.ModelId != "")
            .Select(r => new { r.IssueId, Value = r.ModelId! }).Distinct().ToListAsync(ct);
        var modelProviders = await ModelProvidersAsync(reports, ct);
        var providerPairs = await reports.Where(r => r.Provider != null && r.Provider != "")
            .Select(r => new { r.IssueId, Value = r.Provider! }).Distinct().ToListAsync(ct);
        var versionPairs = await reports.Where(r => r.AppVersion != null && r.AppVersion != "")
            .Select(r => new { r.IssueId, Value = r.AppVersion! }).Distinct().ToListAsync(ct);
        var sourcePairs = await reports.Select(r => new { r.IssueId, r.ModelSource }).Distinct().ToListAsync(ct);
        var configPairs = await reports.Where(r => r.SystemAiConfigurationId != null)
            .Select(r => new { r.IssueId, Id = r.SystemAiConfigurationId!.Value }).Distinct().ToListAsync(ct);
        var toolPairs = await reports.Where(r => r.RelatedToolsJson != null)
            .Select(r => new { r.IssueId, Json = r.RelatedToolsJson! }).Distinct().ToListAsync(ct);
        var userPairs = await reports.Select(r => new { r.IssueId, r.AspNetUserId }).Distinct().ToListAsync(ct);

        var memberships = await _db.UserGroups.AsNoTracking()
            .Where(ug => reports.Any(r => r.AspNetUserId == ug.AspNetUserId))
            .Select(ug => new { ug.AspNetUserId, ug.GroupId, ug.Group.DisplayName })
            .ToListAsync(ct);
        var groupsByUser = memberships.GroupBy(m => m.AspNetUserId).ToDictionary(g => g.Key, g => g.Select(m => m.GroupId).ToList());
        var groupNames = memberships.GroupBy(m => m.GroupId).ToDictionary(g => g.Key, g => g.First().DisplayName);

        var userNames = await UserNamesAsync(userPairs.Select(p => p.AspNetUserId), ct);
        var configNames = await ConfigurationNamesAsync(configPairs.Select(p => p.Id), ct);

        var dto = new AdminFeedbackFacetsDto
        {
            Categories = FeedbackCategories.All
                .Where(categoryCounts.ContainsKey)
                .Select(c => new AdminFeedbackFacetValueDto { Value = c, Count = categoryCounts[c] })
                .Concat(categoryCounts.Keys.Where(c => !FeedbackCategories.IsKnown(c)).OrderBy(c => c, StringComparer.Ordinal)
                    .Select(c => new AdminFeedbackFacetValueDto { Value = c, Count = categoryCounts[c] }))
                .ToList(),
            Models = ValueFacet(modelPairs.Select(p => (p.IssueId, p.Value)), StringComparer.Ordinal.Compare),
            Providers = ValueFacet(providerPairs.Select(p => (p.IssueId, p.Value)), StringComparer.Ordinal.Compare),
            Versions = ValueFacet(versionPairs.Select(p => (p.IssueId, p.Value)), CompareVersionsDescending),
            Tools = ValueFacet(toolPairs.SelectMany(p => ParseRelatedTools(p.Json).Select(t => (p.IssueId, t.ToLowerInvariant()))),
                StringComparer.Ordinal.Compare),
        };
        foreach (var model in dto.Models)
            model.Label = ModelLabel(modelProviders.GetValueOrDefault(model.Value), model.Value);

        var sourceIssues = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        foreach (var pair in sourcePairs)
        {
            string source = pair.ModelSource is FeedbackModelSources.System or FeedbackModelSources.User ? pair.ModelSource! : FeedbackModelSources.Unknown;
            if (!sourceIssues.TryGetValue(source, out var set)) sourceIssues[source] = set = new HashSet<long>();
            set.Add(pair.IssueId);
        }
        dto.Sources = new[] { FeedbackModelSources.System, FeedbackModelSources.User, FeedbackModelSources.Unknown }
            .Where(sourceIssues.ContainsKey)
            .Select(s => new AdminFeedbackFacetValueDto { Value = s, Count = sourceIssues[s].Count })
            .ToList();

        dto.Users = userPairs.Where(p => p.AspNetUserId != null)
            .GroupBy(p => p.AspNetUserId!)
            .Select(g => new AdminFeedbackFacetEntityDto
            {
                Id = g.Key,
                Name = userNames.TryGetValue(g.Key, out var name) && !string.IsNullOrEmpty(name) ? name : g.Key,
                Count = g.Select(p => p.IssueId).Distinct().Count()
            })
            .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ThenBy(u => u.Id, StringComparer.Ordinal)
            .ToList();

        var groupIssues = new Dictionary<long, HashSet<long>>();
        var noGroupIssues = new HashSet<long>();
        foreach (var pair in userPairs)
        {
            if (pair.AspNetUserId != null && groupsByUser.TryGetValue(pair.AspNetUserId, out var userGroupIds))
            {
                foreach (var groupId in userGroupIds)
                {
                    if (!groupIssues.TryGetValue(groupId, out var set)) groupIssues[groupId] = set = new HashSet<long>();
                    set.Add(pair.IssueId);
                }
            }
            else
            {
                noGroupIssues.Add(pair.IssueId);
            }
        }
        dto.Groups = groupIssues
            .Select(g => new AdminFeedbackFacetEntityDto { Id = g.Key.ToString(), Name = groupNames[g.Key], Count = g.Value.Count })
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Id, StringComparer.Ordinal)
            .ToList();
        if (noGroupIssues.Count > 0)
            dto.Groups.Add(new AdminFeedbackFacetEntityDto { Id = OverseerFeedbackFilter.NoGroup, Name = NoGroupName, Count = noGroupIssues.Count });

        dto.Configs = configPairs.GroupBy(p => p.Id)
            .Select(g => new AdminFeedbackFacetEntityDto
            {
                Id = g.Key.ToString(),
                Name = configNames.TryGetValue(g.Key, out var name) ? name : DeletedConfigurationName(g.Key),
                Count = g.Select(p => p.IssueId).Distinct().Count()
            })
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

        var resolvedVia = await issues.Select(i => i.ResolvedVia).ToListAsync(ct);
        dto.ResolvedVia = FeedbackResolvedVia.FacetCounts(resolvedVia)
            .Select(o => new AdminFeedbackFacetValueDto { Value = o.Value, Count = o.Count })
            .ToList();

        return dto;
    }

    // --- Row DTOs ---

    /// <summary>
    /// Builds the rows of <paramref name="issues"/>, in their order, with the aggregates over their retained
    /// occurrences and the GnollBench link, read by a few set-based queries over the issue ids.
    /// </summary>
    public async Task<List<AdminFeedbackIssueDto>> BuildIssueDtosAsync(IReadOnlyList<OverseerFeedbackIssue> issues, CancellationToken ct)
    {
        if (issues.Count == 0) return new List<AdminFeedbackIssueDto>();
        var ids = issues.Select(i => i.Id).ToList();
        var reports = _db.OverseerFeedbackReports.AsNoTracking().Where(r => ids.Contains(r.IssueId));

        var retained = await reports
            .GroupBy(r => r.IssueId)
            .Select(g => new { IssueId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.IssueId, x => x.Count, ct);

        var userPairs = await reports
            .Where(r => r.AspNetUserId != null)
            .Select(r => new { r.IssueId, r.AspNetUserId })
            .Distinct()
            .ToListAsync(ct);
        var userCounts = userPairs.GroupBy(p => p.IssueId).ToDictionary(g => g.Key, g => g.Count());

        var modelPairs = await reports
            .Where(r => r.ModelId != null && r.ModelId != "")
            .Select(r => new { r.IssueId, r.ModelId })
            .Distinct()
            .ToListAsync(ct);
        var modelsByIssue = modelPairs.GroupBy(p => p.IssueId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ModelId!).OrderBy(m => m, StringComparer.Ordinal).ToList());
        var modelProviders = await ModelProvidersAsync(reports, ct);

        var providerPairs = await reports
            .Where(r => r.Provider != null && r.Provider != "")
            .Select(r => new { r.IssueId, r.Provider })
            .Distinct()
            .ToListAsync(ct);
        var providersByIssue = providerPairs.GroupBy(p => p.IssueId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Provider!).OrderBy(m => m, StringComparer.Ordinal).ToList());

        var sourceCounts = await reports
            .GroupBy(r => new { r.IssueId, r.ModelSource })
            .Select(g => new { g.Key.IssueId, g.Key.ModelSource, Count = g.Count() })
            .ToListAsync(ct);
        var sourcesByIssue = new Dictionary<long, AdminFeedbackModelSourceCountsDto>();
        foreach (var s in sourceCounts)
        {
            if (!sourcesByIssue.TryGetValue(s.IssueId, out var counts)) sourcesByIssue[s.IssueId] = counts = new AdminFeedbackModelSourceCountsDto();
            switch (s.ModelSource)
            {
                case FeedbackModelSources.System: counts.System += s.Count; break;
                case FeedbackModelSources.User: counts.User += s.Count; break;
                default: counts.Unknown += s.Count; break;
            }
        }

        // The newest occurrence is the one with the highest id: rows are only ever appended.
        var latestIds = await reports
            .GroupBy(r => r.IssueId)
            .Select(g => g.Max(r => r.Id))
            .ToListAsync(ct);
        var latest = await _db.OverseerFeedbackReports.AsNoTracking()
            .Where(r => latestIds.Contains(r.Id))
            .Select(r => new { r.IssueId, r.ExpectedLocation, r.RelatedToolsJson, r.SuspectedCause, r.AppVersion })
            .ToDictionaryAsync(r => r.IssueId, ct);

        var gnollBench = await GnollBenchLinksAsync(issues.Select(i => i.Fingerprint), ct);
        var triagers = await UserNamesAsync(issues.Select(i => i.TriagedByUserId), ct);

        var rows = new List<AdminFeedbackIssueDto>(issues.Count);
        foreach (var issue in issues)
        {
            latest.TryGetValue(issue.Id, out var newest);
            var shownModels = ShownModelIds(modelsByIssue.GetValueOrDefault(issue.Id));
            rows.Add(new AdminFeedbackIssueDto
            {
                Id = issue.Id,
                Category = issue.Category,
                Subject = issue.Subject,
                Summary = issue.Summary,
                FirstSeenUtc = issue.FirstSeenUtc,
                LastSeenUtc = issue.LastSeenUtc,
                OccurrenceCount = issue.OccurrenceCount,
                RetainedCount = retained.GetValueOrDefault(issue.Id),
                DistinctUserCount = userCounts.GetValueOrDefault(issue.Id),
                ModelIds = shownModels,
                ModelLabels = shownModels.Select(m => m.StartsWith('+') ? m : ModelLabel(modelProviders.GetValueOrDefault(m), m)).ToList(),
                Providers = providersByIssue.GetValueOrDefault(issue.Id) ?? new List<string>(),
                ModelSources = sourcesByIssue.GetValueOrDefault(issue.Id) ?? new AdminFeedbackModelSourceCountsDto(),
                RegressionCount = issue.RegressionCount,
                LastRegressionUtc = issue.LastRegressionUtc,
                LatestExpectedLocation = newest?.ExpectedLocation,
                LatestRelatedTools = ParseRelatedTools(newest?.RelatedToolsJson),
                LatestSuspectedCause = newest?.SuspectedCause,
                LatestAppVersion = newest?.AppVersion,
                Status = AdminFeedbackStatuses.ToWire(issue.Status),
                TriageNote = issue.TriageNote,
                TriagedByUserName = issue.TriagedByUserId != null && triagers.TryGetValue(issue.TriagedByUserId, out var name) ? name : null,
                TriagedUtc = issue.TriagedUtc,
                Resolution = issue.Resolution,
                FixedInVersion = issue.FixedInVersion,
                ResolvedVia = FeedbackResolvedVia.FromStored(issue.ResolvedVia),
                AlsoInGnollBench = gnollBench.GetValueOrDefault(issue.Fingerprint)
            });
        }
        return rows;
    }

    /// <summary>
    /// Builds occurrence rows with the reporting user's name and current groups, and the system configuration's
    /// name (<see cref="DeletedConfigurationName"/> once it is gone).
    /// </summary>
    public async Task<List<AdminFeedbackOccurrenceDto>> BuildOccurrenceDtosAsync(IReadOnlyList<OverseerFeedbackReport> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return new List<AdminFeedbackOccurrenceDto>();

        var names = await UserNamesAsync(rows.Select(r => r.AspNetUserId), ct);
        var configNames = await ConfigurationNamesAsync(rows.Where(r => r.SystemAiConfigurationId != null).Select(r => r.SystemAiConfigurationId!.Value), ct);

        var userIds = rows.Where(r => r.AspNetUserId != null).Select(r => r.AspNetUserId!).Distinct().ToList();
        var groupsByUser = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (userIds.Count > 0)
        {
            var memberships = await _db.UserGroups.AsNoTracking()
                .Where(ug => userIds.Contains(ug.AspNetUserId))
                .Select(ug => new { ug.AspNetUserId, ug.Group.DisplayName })
                .ToListAsync(ct);
            groupsByUser = memberships.GroupBy(m => m.AspNetUserId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(m => m.DisplayName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
                    StringComparer.Ordinal);
        }

        return rows.Select(r => new AdminFeedbackOccurrenceDto
        {
            Id = r.Id,
            CreatedUtc = r.CreatedUtc,
            Subject = r.Subject,
            Summary = r.Summary,
            Details = r.Details,
            SearchedFor = r.SearchedFor,
            ExpectedLocation = r.ExpectedLocation,
            RelatedTools = ParseRelatedTools(r.RelatedToolsJson),
            NoticedVia = r.NoticedVia,
            SuspectedCause = r.SuspectedCause,
            SuspectedCauseNote = r.SuspectedCauseNote,
            Truncated = r.Truncated,
            Redacted = r.Redacted,
            ChatSessionId = r.ChatSessionId,
            TurnUserMessageId = r.TurnUserMessageId,
            Provider = r.Provider,
            ModelId = r.ModelId,
            ModelLabel = string.IsNullOrWhiteSpace(r.ModelId) ? r.ModelId : ModelLabel(r.Provider, r.ModelId),
            AppVersion = r.AppVersion,
            UserName = r.AspNetUserId != null && names.TryGetValue(r.AspNetUserId, out var name) ? name : null,
            ModelSource = r.ModelSource,
            SystemConfigurationId = r.SystemAiConfigurationId,
            SystemConfigurationName = r.SystemAiConfigurationId is long configId
                ? configNames.TryGetValue(configId, out var configName) ? configName : DeletedConfigurationName(configId)
                : null,
            GroupNames = r.AspNetUserId != null && groupsByUser.TryGetValue(r.AspNetUserId, out var groups) ? groups : new List<string>()
        }).ToList();
    }

    // --- Lookups ---

    /// <summary>
    /// The GnollBench link of each fingerprint that has an accepted self-report: the count, and the status from
    /// <see cref="BenchmarkFeedbackTriage"/> (no row is New; Resolved with a self-report later than its
    /// <see cref="BenchmarkFeedbackTriage.ResolvedUtc"/> is Regressed). A self-report's time is its answer's start,
    /// or its run's when the answer has none.
    /// </summary>
    public async Task<Dictionary<string, AdminFeedbackGnollBenchLinkDto>> GnollBenchLinksAsync(IEnumerable<string> fingerprints, CancellationToken ct)
    {
        var fps = fingerprints.Where(f => !string.IsNullOrEmpty(f)).Distinct(StringComparer.Ordinal).ToList();
        var links = new Dictionary<string, AdminFeedbackGnollBenchLinkDto>(StringComparer.Ordinal);
        if (fps.Count == 0) return links;

        var selfReports = await _db.BenchmarkRunAnswerSelfReports.AsNoTracking()
            .Where(s => s.Accepted && s.Fingerprint != null && fps.Contains(s.Fingerprint!))
            .Select(s => new
            {
                Fingerprint = s.Fingerprint!,
                Time = s.BenchmarkRunAnswer!.StartedAtUtc ?? s.BenchmarkRunAnswer!.BenchmarkRun.StartedAtUtc
            })
            .ToListAsync(ct);
        if (selfReports.Count == 0) return links;

        var found = selfReports.Select(s => s.Fingerprint).Distinct(StringComparer.Ordinal).ToList();
        var triages = await _db.BenchmarkFeedbackTriages.AsNoTracking()
            .Where(t => found.Contains(t.Fingerprint))
            .ToDictionaryAsync(t => t.Fingerprint, StringComparer.Ordinal, ct);

        foreach (var group in selfReports.GroupBy(s => s.Fingerprint, StringComparer.Ordinal))
        {
            DateTime latest = group.Max(s => s.Time);
            triages.TryGetValue(group.Key, out var triage);
            links[group.Key] = new AdminFeedbackGnollBenchLinkDto
            {
                SelfReportCount = group.Count(),
                Status = GnollBenchStatus(triage, latest)
            };
        }
        return links;
    }

    /// <summary>Configuration display names by id, for the ids that still exist.</summary>
    public async Task<Dictionary<long, string>> ConfigurationNamesAsync(IEnumerable<long> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<long, string>();
        return await _db.SystemAiApiConfigurations.AsNoTracking()
            .Where(c => list.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);
    }

    /// <summary>User names by id for the ids that still have an account.</summary>
    public async Task<Dictionary<string, string?>> UserNamesAsync(IEnumerable<string?> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string?>();
        return await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UserName, ct);
    }

    /// <summary>The stored JSON array of tool names; empty when absent or malformed.</summary>
    public static List<string> ParseRelatedTools(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            var tools = JsonSerializer.Deserialize<List<string?>>(json);
            return tools?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).ToList() ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    // --- Helpers ---

    /// <summary>A provider for each model id among <paramref name="reports"/>: the ordinal first of those that name it.</summary>
    private static async Task<Dictionary<string, string>> ModelProvidersAsync(IQueryable<OverseerFeedbackReport> reports, CancellationToken ct)
    {
        var pairs = await reports
            .Where(r => r.ModelId != null && r.ModelId != "" && r.Provider != null && r.Provider != "")
            .Select(r => new { ModelId = r.ModelId!, Provider = r.Provider! })
            .Distinct()
            .ToListAsync(ct);
        return pairs.GroupBy(p => p.ModelId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Provider).OrderBy(p => p, StringComparer.Ordinal).First(), StringComparer.Ordinal);
    }

    private IQueryable<OverseerFeedbackIssue> InRange(DateTime? startDate, DateTime? endDate)
    {
        var query = _db.OverseerFeedbackIssues.AsNoTracking();
        if (startDate.HasValue) { var from = startDate.Value; query = query.Where(i => i.LastSeenUtc >= from); }
        if (endDate.HasValue) { var to = endDate.Value.AddDays(1); query = query.Where(i => i.FirstSeenUtc < to); }
        return query;
    }

    private static string GnollBenchStatus(BenchmarkFeedbackTriage? triage, DateTime latestSelfReport)
    {
        if (triage == null) return AdminFeedbackStatuses.New;
        return triage.Status switch
        {
            BenchmarkFeedbackTriageStatus.Acknowledged => AdminFeedbackStatuses.Acknowledged,
            BenchmarkFeedbackTriageStatus.Resolved => triage.ResolvedUtc is DateTime resolved && latestSelfReport > resolved
                ? AdminFeedbackStatuses.Regressed
                : AdminFeedbackStatuses.Resolved,
            BenchmarkFeedbackTriageStatus.Dismissed => AdminFeedbackStatuses.Dismissed,
            _ => AdminFeedbackStatuses.New
        };
    }

    /// <summary>
    /// <c>r.RelatedToolsJson</c> contains any of the quoted names, ignoring case: one <c>OR</c> of <c>LIKE</c>s.
    /// </summary>
    private static Expression<Func<OverseerFeedbackReport, bool>> RelatedToAnyTool(IReadOnlyList<string> tools)
    {
        var r = Expression.Parameter(typeof(OverseerFeedbackReport), "r");
        var json = Expression.Property(r, nameof(OverseerFeedbackReport.RelatedToolsJson));
        var lowered = Expression.Call(json, typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);
        var contains = typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;

        Expression? any = null;
        foreach (var tool in tools)
        {
            Expression match = Expression.Call(lowered, contains, Expression.Constant("\"" + tool.ToLowerInvariant() + "\""));
            any = any == null ? match : Expression.OrElse(any, match);
        }
        var body = Expression.AndAlso(Expression.NotEqual(json, Expression.Constant(null, typeof(string))), any!);
        return Expression.Lambda<Func<OverseerFeedbackReport, bool>>(body, r);
    }

    /// <summary>
    /// The issue's stored resolved-via list holds any of <paramref name="ids"/>, or is null or empty when
    /// <paramref name="includeNone"/>: one <c>OR</c> of comma-bracketed <c>LIKE</c>s over the canonical stored form.
    /// </summary>
    private static Expression<Func<OverseerFeedbackIssue, bool>> HasAnyResolvedVia(IReadOnlyList<string> ids, bool includeNone)
    {
        var i = Expression.Parameter(typeof(OverseerFeedbackIssue), "i");
        var stored = Expression.Property(i, nameof(OverseerFeedbackIssue.ResolvedVia));
        var concat = typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(string), typeof(string), typeof(string) })!;
        var contains = typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;
        var bracketed = Expression.Call(concat, Expression.Constant(","), stored, Expression.Constant(","));

        Expression? any = null;
        foreach (var id in ids)
        {
            Expression match = Expression.Call(bracketed, contains, Expression.Constant("," + id + ","));
            any = any == null ? match : Expression.OrElse(any, match);
        }
        if (includeNone)
        {
            Expression none = Expression.OrElse(
                Expression.Equal(stored, Expression.Constant(null, typeof(string))),
                Expression.Equal(stored, Expression.Constant("")));
            any = any == null ? none : Expression.OrElse(any, none);
        }
        return Expression.Lambda<Func<OverseerFeedbackIssue, bool>>(any ?? Expression.Constant(true), i);
    }

    /// <summary>At most <see cref="MaxModelIdsShown"/> model ids, then <c>+N</c> for the ones left out.</summary>
    private static List<string> ShownModelIds(List<string>? models)
    {
        if (models == null || models.Count == 0) return new List<string>();
        if (models.Count <= MaxModelIdsShown) return models;
        var shown = models.Take(MaxModelIdsShown).ToList();
        shown.Add($"+{models.Count - MaxModelIdsShown}");
        return shown;
    }

    private static List<AdminFeedbackFacetValueDto> ValueFacet(IEnumerable<(long IssueId, string Value)> pairs, Comparison<string> order)
    {
        var options = pairs
            .GroupBy(p => p.Value, StringComparer.Ordinal)
            .Select(g => new AdminFeedbackFacetValueDto { Value = g.Key, Count = g.Select(p => p.IssueId).Distinct().Count() })
            .ToList();
        options.Sort((a, b) => order(a.Value, b.Value));
        return options;
    }

    /// <summary>Newest first: by the numeric part before any <c>+</c> or <c>-</c> suffix, then ordinal.</summary>
    private static int CompareVersionsDescending(string a, string b)
    {
        bool parsedA = Version.TryParse(NumericPart(a), out var va);
        bool parsedB = Version.TryParse(NumericPart(b), out var vb);
        if (parsedA && parsedB)
        {
            int byVersion = vb!.CompareTo(va);
            if (byVersion != 0) return byVersion;
        }
        else if (parsedA != parsedB)
        {
            return parsedA ? -1 : 1;
        }
        return string.CompareOrdinal(b, a);
    }

    private static string NumericPart(string version)
    {
        int cut = version.IndexOfAny(new[] { '+', '-', ' ' });
        return cut < 0 ? version : version[..cut];
    }
}

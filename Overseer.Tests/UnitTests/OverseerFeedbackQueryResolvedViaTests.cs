using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Feedback;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The Resolved via filter of the Overseer feedback issue list on SQLite (<see cref="SqliteSchemaTemplate"/>), so the
/// comma-bracketed match is translated to SQL as it would be on SQL Server instead of being evaluated in memory.
/// </summary>
public sealed class OverseerFeedbackQueryResolvedViaTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime T0 = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = SqliteSchemaTemplate.OpenCopy();
    private readonly ApplicationDbContext _db;
    private readonly OverseerFeedbackQuery _query;
    private int _fingerprintSeed;

    public OverseerFeedbackQueryResolvedViaTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        _db = new ApplicationDbContext(options);
        _query = new OverseerFeedbackQuery(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task AddIssueAsync(string subject, string? resolvedVia)
    {
        _db.OverseerFeedbackIssues.Add(new OverseerFeedbackIssue
        {
            Fingerprint = (++_fingerprintSeed).ToString("x64"),
            Category = "missing_information",
            Subject = subject,
            Summary = "Something was missing.",
            FirstSeenUtc = T0,
            LastSeenUtc = T0,
            OccurrenceCount = 1,
            ResolvedVia = resolvedVia
        });
        await _db.SaveChangesAsync(Ct);
    }

    private async Task<string[]> SubjectsAsync(string resolvedVia)
    {
        Assert.True(OverseerFeedbackFilter.TryParse(new AdminFeedbackFilterQuery { ResolvedVia = resolvedVia }, out var filter, out var error), error);
        var subjects = await _query.Issues(filter).Select(i => i.Subject).ToListAsync(Ct);
        return subjects.OrderBy(s => s, StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public async Task TheFilterMatchesWholeIdsOnly_InAnyPositionOfTheStoredList()
    {
        await AddIssueAsync("Wiki", "wiki");
        await AddIssueAsync("Wiki and KB", "wiki,knowledge_base");
        await AddIssueAsync("KB and other", "knowledge_base,other");
        await AddIssueAsync("Partial id", "wiki_fix,knowledge_base_x");
        await AddIssueAsync("Nothing", null);

        Assert.Equal(new[] { "Wiki", "Wiki and KB" }, await SubjectsAsync("wiki"));
        Assert.Equal(new[] { "KB and other", "Wiki and KB" }, await SubjectsAsync("knowledge_base"));
        Assert.Equal(new[] { "KB and other" }, await SubjectsAsync("other"));
        Assert.Empty(await SubjectsAsync("overseer_release"));
    }

    [Fact]
    public async Task NoneMatchesANullOrEmptyValue_AndCombinesWithIdsAsOneOf()
    {
        await AddIssueAsync("Wiki", "wiki");
        await AddIssueAsync("Release", "gnollhack_release");
        await AddIssueAsync("Null", null);
        await AddIssueAsync("Empty", "");

        Assert.Equal(new[] { "Empty", "Null" }, await SubjectsAsync("none"));
        Assert.Equal(new[] { "Empty", "Null", "Wiki" }, await SubjectsAsync("none,wiki"));
        Assert.Equal(new[] { "Release", "Wiki" }, await SubjectsAsync("gnollhack_release,wiki"));
    }

    [Fact]
    public async Task TheFacetCountsEachIdOnce_AndListsNoneLast()
    {
        await AddIssueAsync("Wiki", "wiki");
        await AddIssueAsync("Wiki and KB", "wiki,knowledge_base");
        await AddIssueAsync("Nothing", null);

        var facets = await _query.FacetsAsync(null, null, Ct);

        Assert.Equal(new[] { "wiki:2", "knowledge_base:1", "none:1" }, facets.ResolvedVia.Select(o => $"{o.Value}:{o.Count}"));
    }
}

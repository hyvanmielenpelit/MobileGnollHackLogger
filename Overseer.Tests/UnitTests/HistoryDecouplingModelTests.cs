using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MobileGnollHackLogger.Data;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Invariants of the EF model that keep benchmark history and AI logs independent of the mutable
/// system configuration. The in-memory provider enforces no constraints, so a delete that
/// "succeeds" in a test proves nothing; these model facts are what carry the proof.
/// </summary>
public class HistoryDecouplingModelTests
{
    private static IModel Model()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        return db.Model;
    }

    [Fact]
    public void OnlyAssignmentsAndTrustDecisions_ReferenceASystemConfiguration()
    {
        var configuration = Model().FindEntityType(typeof(SystemAiApiConfiguration))!;

        var dependents = configuration.GetReferencingForeignKeys()
            .Select(fk => fk.DeclaringEntityType.ClrType)
            .Distinct()
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            new[] { typeof(GroupSystemAiApiConfiguration), typeof(UserSystemAiApiConfiguration), typeof(UserSystemModelConfidentialTrust) },
            dependents);
    }

    [Fact]
    public void EveryForeignKeyIntoTheSnapshotTable_IsRestrict()
    {
        var snapshot = Model().FindEntityType(typeof(SystemAiConfigurationSnapshot))!;
        var foreignKeys = snapshot.GetReferencingForeignKeys().ToList();

        Assert.Equal(12, foreignKeys.Count);
        Assert.All(foreignKeys, fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }

    [Fact]
    public void EverySnapshotNavigation_IsAutoIncluded()
    {
        var navigations = Model().GetEntityTypes()
            .SelectMany(e => e.GetNavigations())
            .Where(n => n.TargetEntityType.ClrType == typeof(SystemAiConfigurationSnapshot))
            .ToList();

        Assert.Equal(12, navigations.Count);
        Assert.All(navigations, n => Assert.True(n.IsEagerLoaded, $"{n.DeclaringEntityType.ClrType.Name}.{n.Name} is not auto-included."));
    }

    [Fact]
    public void TheSnapshotHash_IsUnique()
    {
        var snapshot = Model().FindEntityType(typeof(SystemAiConfigurationSnapshot))!;

        Assert.Contains(snapshot.GetIndexes(),
            i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(SystemAiConfigurationSnapshot.Sha256) }));
    }
}

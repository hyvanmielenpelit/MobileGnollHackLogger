namespace Overseer.Tests.UnitTests;

using System.Linq;
using System.Threading.Tasks;
using Overseer.Services;
using Overseer.Services.ChatConsistency;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The model axes of Chat Consistency carry the catalog release date of their model, so the subject
/// picker offers the same release-date sorts as the Run tab's pickers.
/// </summary>
public class ChatConsistencyModelAxisReleaseDateTests
{
    [Fact]
    public async Task ModelAxes_CarryTheCatalogReleaseDate_AndNullForAModelOutsideTheCatalog()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = ChatConsistencyAnalysisTestData.NewDb();
        var metadata = new ModelMetadataService();
        string catalogued = metadata.GetCatalogTargets("Google")
            .Select(t => t.ModelId)
            .First(id => metadata.GetReleaseDate("Google", id) != null);

        var assessor = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-judge");
        var cataloguedRun = ChatConsistencyAnalysisTestData.Run(
            1, ChatConsistencyAnalysisTestData.BaselineDay1, BenchmarkModelSnapshots.Model(provider: "Google", modelId: catalogued),
            assessor, ChatConsistencyAnalysisTestData.GuidesBefore, _ => 80, legacy: false, durationFactor: 1.0, servedModel: catalogued);
        var madeUpRun = ChatConsistencyAnalysisTestData.Run(
            2, ChatConsistencyAnalysisTestData.BaselineDay2, BenchmarkModelSnapshots.Model(provider: "Google", modelId: "made-up-model-x"),
            assessor, ChatConsistencyAnalysisTestData.GuidesBefore, _ => 80, legacy: false, durationFactor: 1.0, servedModel: "made-up-model-x");
        db.BenchmarkRuns.AddRange(cataloguedRun, madeUpRun);
        await db.SaveChangesAsync(ct);

        var axes = await new ChatConsistencyEvidenceBuilder(db, metadata: metadata).ListModelAxesAsync(ct);
        var cataloguedAxis = Assert.Single(axes, a => a.ModelId == catalogued);
        Assert.NotNull(cataloguedAxis.ReleaseDate);
        Assert.Equal(metadata.GetReleaseDate("Google", catalogued), cataloguedAxis.ReleaseDate);
        Assert.Null(Assert.Single(axes, a => a.ModelId == "made-up-model-x").ReleaseDate);

        var withoutCatalog = await new ChatConsistencyEvidenceBuilder(db).ListModelAxesAsync(ct);
        Assert.Equal(2, withoutCatalog.Count);
        Assert.All(withoutCatalog, a => Assert.Null(a.ReleaseDate));
    }
}

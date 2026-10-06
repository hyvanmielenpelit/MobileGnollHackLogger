namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Services.Privacy;
using Overseer.Tests.Helpers;
using UglyToad.PdfPig;
using Xunit;

/// <summary>
/// <c>POST report-packs/layout-preview</c> and <see cref="BenchmarkReportLayoutPreview"/>: a PDF of the
/// document a request would write, placeholder text in the writer's slots, the charts placed by the
/// layout, its refusals as the preview gives them, no writer call and nothing stored.
/// </summary>
public class BenchmarkReportLayoutPreviewTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // --- The endpoint ------------------------------------------------------------------------------

    [Fact]
    public async Task TheLayoutPreview_IsAPdfMarkedAsOne_WithPlaceholderTextAndTheChartsInARow_AndCallsNoWriterAndStoresNothing()
    {
        BenchmarkPdfTestSetup.Configure();
        var ct = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync();
        var (comparison, error) = await new BenchmarkComparisonIdentityService(h.Db, new BenchmarkModelComparisonService(h.Db))
            .EnsureAsync(h.Seeded.RunIds, null, null, "user-1", ct);
        Assert.True(comparison != null, error);
        int comparisons = await h.Db.BenchmarkComparisons.CountAsync(ct);

        var request = h.ComparisonRequest(BenchmarkReportAudience.ExecutiveSummary);
        request.Layout = RowLayout("p1a-quality", "p1b-speed");
        request.Charts = ChartEntries("p1a-quality", "p1b-speed");

        var result = await h.Controller().LayoutPreview(Json(request), Files("p1a-quality", "p1b-speed"), ct);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        using (var reader = PdfDocument.Open(file.FileContents))
        {
            string cover = Squash(reader.GetPage(1).Text);
            Assert.Contains(Squash(BenchmarkReportLayoutPreview.Stamp), cover);
            Assert.Contains(Squash("Comparison #" + comparison!.Id), cover);
            Assert.Contains(Squash("none (layout preview)"), cover);

            string text = Squash(string.Concat(reader.GetPages().Select(p => p.Text)));
            Assert.Contains(Squash("text for " + BenchmarkReportSlots.Overview + " appears here."), text);
            Assert.Contains(Squash("text for " + BenchmarkReportSlots.WhichModel + " appears here."), text);
            Assert.Contains(Squash("Figure 1. Chart p1a-quality — Caption of p1a-quality."), text);
            Assert.Contains(Squash("Figure 2. Chart p1b-speed — Caption of p1b-speed."), text);

            // The two half-width charts print side by side.
            var images = reader.GetPages()
                .SelectMany(p => p.GetImages().Where(i => i.BoundingBox.Width > 150).Select(i => (Page: p.Number, i.BoundingBox.Top)))
                .ToList();
            Assert.Equal(2, images.Count);
            Assert.Equal(images[0].Page, images[1].Page);
            Assert.Equal(images[0].Top, images[1].Top, 1.0);
        }

        Assert.Equal(0, h.WriterResolutions);
        Assert.Null(h.Jobs.Current);
        Assert.Empty(await h.Db.BenchmarkReportDocuments.IgnoreAutoIncludes().ToListAsync(ct));
        Assert.Equal(comparisons, await h.Db.BenchmarkComparisons.CountAsync(ct));
        Assert.Empty(Directory.EnumerateFileSystemEntries(h.ChartRoot));
    }

    [Fact]
    public async Task AModelScopeLayoutPreview_PreviewsTheFirstSubject_OnTheRequestedPaperAndNaming()
    {
        BenchmarkPdfTestSetup.Configure();
        var ct = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync();
        var request = h.ModelRequest(BenchmarkReportAudience.TechnicalReport, h.Seeded.RunIds[0], h.Seeded.RunIds[1]);
        request.SubjectKeys = new List<string> { $"run:{h.Seeded.RunIds[1]}", $"run:{h.Seeded.RunIds[0]}" };
        request.Paper = "letter";
        request.Naming = "anonymized";

        var file = Assert.IsType<FileContentResult>(await h.Controller().LayoutPreview(Json(request), null, ct));

        using var reader = PdfDocument.Open(file.FileContents);
        Assert.Equal(612, reader.GetPage(1).Width, 0.5);
        string text = Squash(string.Concat(reader.GetPages().Select(p => p.Text)));
        Assert.Contains(Squash(BenchmarkReportLayoutPreview.Stamp), text);
        Assert.Contains(Squash("text for " + BenchmarkReportSlots.WhyItScored + " appears here."), text);
        Assert.Contains(Squash("topic for this question appears here."), text);
        Assert.Equal(0, h.WriterResolutions);
        Assert.Empty(await h.Db.BenchmarkReportDocuments.IgnoreAutoIncludes().ToListAsync(ct));
    }

    [Fact]
    public async Task TheLayoutPreview_RefusesAsThePreviewDoes_AndChecksItsChartsAsAnUploadIsChecked()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync();
        var controller = h.Controller();

        AssertError(400, AdminBenchmarkReportPacksController.LayoutPreviewMissingRequest, await controller.LayoutPreview(null, null, ct));
        AssertError(400, AdminBenchmarkReportPacksController.LayoutPreviewInvalidRequest, await controller.LayoutPreview("{not json", null, ct));

        var mixed = h.ComparisonRequest(BenchmarkReportAudience.ExecutiveSummary);
        mixed.BatteryRunIds = new List<long> { 5 };
        AssertError(400, BenchmarkBatteryModelComparison.MixedSourcesError, await controller.LayoutPreview(Json(mixed), null, ct));

        var audience = h.ComparisonRequest((BenchmarkReportAudience)7);
        AssertError(400, AdminBenchmarkReportPacksController.LayoutPreviewAudienceError, await controller.LayoutPreview(Json(audience), null, ct));

        var paper = h.ComparisonRequest(BenchmarkReportAudience.ExecutiveSummary);
        paper.Paper = "a3";
        AssertError(400, Overseer.Services.Benchmarking.Pdf.BenchmarkPdfDocumentInfo.PaperError, await controller.LayoutPreview(Json(paper), null, ct));

        var naming = h.ComparisonRequest(BenchmarkReportAudience.ExecutiveSummary);
        naming.Naming = "pseudonymous";
        AssertError(400, AdminBenchmarkReportPacksController.LayoutPreviewNamingError, await controller.LayoutPreview(Json(naming), null, ct));

        var layout = h.ComparisonRequest(BenchmarkReportAudience.ExecutiveSummary);
        layout.Layout = new BenchmarkReportChartLayout { MaxHeightShare = 0.95 };
        AssertError(400, Assert.Throws<ChartStoreException>(() => BenchmarkReportChartStore.ValidateLayout(layout.Layout)).Message,
            await controller.LayoutPreview(Json(layout), null, ct));

        // A chart without its file, a file without its chart, an unknown figure key, a file that is not a PNG, too many charts.
        var noFile = h.ComparisonRequest(BenchmarkReportAudience.ExecutiveSummary);
        noFile.Charts = ChartEntries("p1a-quality");
        AssertError(400, "Chart 1 (p1a-quality) has no image data.", await controller.LayoutPreview(Json(noFile), null, ct));

        var noChart = h.ComparisonRequest(BenchmarkReportAudience.ExecutiveSummary);
        noChart.Charts = ChartEntries("p1a-quality");
        var orphan = await controller.LayoutPreview(Json(noChart), Files("p1a-quality", "p1b-speed"), ct);
        Assert.Contains("p1b-speed.png", ErrorOf(orphan, 400), StringComparison.Ordinal);

        var unknown = h.ComparisonRequest(BenchmarkReportAudience.ExecutiveSummary);
        unknown.Charts = ChartEntries("x-unknown");
        Assert.Contains("unknown figure key", ErrorOf(await controller.LayoutPreview(Json(unknown), Files("x-unknown"), ct), 400), StringComparison.Ordinal);

        var notPng = h.ComparisonRequest(BenchmarkReportAudience.ExecutiveSummary);
        notPng.Charts = ChartEntries("p1a-quality");
        byte[] text = System.Text.Encoding.UTF8.GetBytes("not a png at all, just text");
        AssertError(400, "Chart 1 (p1a-quality) is not a PNG image.",
            await controller.LayoutPreview(Json(notPng), new List<IFormFile> { FormFileOf("p1a-quality.png", text) }, ct));

        var many = h.ComparisonRequest(BenchmarkReportAudience.ExecutiveSummary);
        many.Charts = Enumerable.Range(0, BenchmarkReportLayoutPreview.MaxCharts + 1)
            .Select(_ => new BenchmarkReportLayoutPreviewChart { FigureKey = "p1a-quality", AltText = "Alt." })
            .ToList();
        Assert.Contains("at most 12", ErrorOf(await controller.LayoutPreview(Json(many), null, ct), 400), StringComparison.Ordinal);

        // Preparation refusals: a subject with no peer, and a comparison-wide document of one model.
        var single = h.ModelRequest(BenchmarkReportAudience.ExecutiveSummary, h.Seeded.RunIds[0]);
        AssertError(400, BenchmarkReportPackPreparation.PeerlessReportRefusal, await controller.LayoutPreview(Json(single), null, ct));

        var one = h.ComparisonRequest(BenchmarkReportAudience.ExecutiveSummary, new[] { $"run:{h.Seeded.RunIds[0]}" });
        AssertError(409, BenchmarkComparisonReportFacts.TooFewRefusal, await controller.LayoutPreview(Json(one), null, ct));

        Assert.Equal(0, h.WriterResolutions);
        Assert.Empty(await h.Db.BenchmarkReportDocuments.IgnoreAutoIncludes().ToListAsync(ct));
    }

    [Fact]
    public void TheLayoutPreview_IsAMultipartPostUnderReportPacks_TakingFortyMegabytes()
    {
        var method = typeof(AdminBenchmarkReportPacksController).GetMethod(nameof(AdminBenchmarkReportPacksController.LayoutPreview))!;
        Assert.Equal("report-packs/layout-preview", method.GetCustomAttribute<HttpPostAttribute>()!.Template);
        var limit = Assert.Single(method.CustomAttributes, a => a.AttributeType == typeof(RequestSizeLimitAttribute));
        Assert.Equal(40_000_000L, Assert.IsType<long>(limit.ConstructorArguments.Single().Value));

        var parameters = method.GetParameters();
        Assert.Equal("request", parameters.Single(p => p.ParameterType == typeof(string)).GetCustomAttribute<FromFormAttribute>()!.Name);
        Assert.Equal("files", parameters.Single(p => p.ParameterType == typeof(List<IFormFile>)).GetCustomAttribute<FromFormAttribute>()!.Name);
    }

    // --- The placeholder document --------------------------------------------------------------------

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary, false)]
    [InlineData(BenchmarkReportAudience.TechnicalReport, false)]
    [InlineData(BenchmarkReportAudience.InternalBrief, false)]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary, true)]
    [InlineData(BenchmarkReportAudience.TechnicalReport, true)]
    [InlineData(BenchmarkReportAudience.InternalBrief, true)]
    public void ThePlaceholderOutput_FillsEverySlotAtATypicalLength_AndEveryListTheAudienceUses(BenchmarkReportAudience audience, bool comparison)
    {
        var document = comparison ? BenchmarkReportPackFixture.ComparisonDocument(audience) : BenchmarkReportPackFixture.Document(audience);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        var spec = BenchmarkReportSlots.For(audience, sheet);

        var output = BenchmarkReportLayoutPreview.PlaceholderOutput(audience, sheet);

        var slots = spec.SlotsFor(hasPeers: true);
        Assert.Equal(slots.OrderBy(s => s, StringComparer.Ordinal), output.Sections.Keys.OrderBy(s => s, StringComparer.Ordinal));
        foreach (string slot in slots)
        {
            string text = output.Sections[slot];
            Assert.StartsWith(BenchmarkReportLayoutPreview.Sentence(slot), text, StringComparison.Ordinal);
            int limit = BenchmarkReportPackValidator.SlotMaxWords(audience, slot, comparison) ?? 100;
            int words = text.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.InRange(words, (int)Math.Round(limit * BenchmarkReportLayoutPreview.TypicalShareOfLimit), limit);
        }
        Assert.False(string.IsNullOrWhiteSpace(output.Headline));

        Assert.Equal(spec.UsesStrengthsAndWeaknesses, output.Strengths.Count > 0);
        Assert.Equal(spec.UsesStrengthsAndWeaknesses, output.Weaknesses.Count > 0);
        Assert.Equal(spec.UsesRecommendations ? spec.RecommendationTargets.OrderBy(t => t) : Enumerable.Empty<string>(),
            output.Recommendations.Select(r => r.For).Distinct().OrderBy(t => t));
        Assert.True(output.Recommendations.Count <= BenchmarkReportPackValidator.MaxRecommendations(audience) || !spec.UsesRecommendations);
        Assert.Equal(spec.UsesLeads, output.Leads.Count > 0);
        Assert.All(output.Leads, l => Assert.Contains(l.Triage, spec.LeadTriages));

        if (spec.MaxModelPoints > 0)
        {
            Assert.Equal(sheet.Peers.Select(p => p.Letter).OrderBy(l => l), output.Models!.Select(m => m.Model).OrderBy(l => l));
            Assert.All(output.Models!, m => Assert.Equal(spec.MaxModelPoints, m.Points.Count));
        }
        else
        {
            Assert.Null(output.Models);
        }

        Assert.Equal(spec.RequiresQuestionTopics ? sheet.Questions.Select(q => q.Number).Distinct().OrderBy(n => n) : Enumerable.Empty<int>(),
            output.QuestionTopics.Select(t => t.Question));

        // The document renders with it, through the same path as a writer's text.
        document.WriterOutputJson = BenchmarkReportJson.Serialize(output);
        string markdown = BenchmarkReportPackRenderer.Render(document,
            BenchmarkReportLayoutPreview.Options(audience, BenchmarkReportPeerNaming.Named, Array.Empty<BenchmarkReportRenderChart>()));
        Assert.Contains("text for " + slots[0] + " appears here.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlaceholderText_RepeatsItsSentenceToTheWords_InParagraphsOfEightSentences()
    {
        string text = BenchmarkReportLayoutPreview.PlaceholderText("abstract", 120);

        Assert.Equal(15 * 8, text.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length);
        var paragraphs = text.Split("\n\n");
        Assert.Equal(2, paragraphs.Length);
        Assert.Equal(string.Join(" ", Enumerable.Repeat(BenchmarkReportLayoutPreview.Sentence("abstract"), 8)), paragraphs[0]);
        Assert.Equal(BenchmarkReportLayoutPreview.Sentence("abstract"), BenchmarkReportLayoutPreview.PlaceholderText("abstract", 1));
    }

    // --- Helpers -----------------------------------------------------------------------------------

    private static string Json(BenchmarkReportLayoutPreviewRequest request) => JsonSerializer.Serialize(request, Web);

    private static BenchmarkReportChartLayout RowLayout(params string[] keys) => new()
    {
        Figures = keys.Select(k => new BenchmarkReportChartLayoutFigure { Key = k, WidthShare = 0.5, RowGroup = 1 }).ToList(),
        MaxHeightShare = 0.5
    };

    private static List<BenchmarkReportLayoutPreviewChart> ChartEntries(params string[] keys)
        => keys.Select(k => new BenchmarkReportLayoutPreviewChart
        {
            FigureKey = k,
            Title = "Chart " + k,
            Caption = "Caption of " + k + ".",
            AltText = "Alt text of " + k + "."
        }).ToList();

    private static List<IFormFile> Files(params string[] keys)
        => keys.Select((k, i) => FormFileOf(k + ".png", TestPngs.Make(640, 360, (byte)(40 * (i + 1))))).ToList();

    private static IFormFile FormFileOf(string name, byte[] bytes)
        => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", name);

    private static string Squash(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static string ErrorOf(IActionResult result, int status)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, objectResult.StatusCode);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(objectResult.Value));
        return json.RootElement.GetProperty("error").GetString()!;
    }

    private static void AssertError(int status, string expected, IActionResult result) => Assert.Equal(expected, ErrorOf(result, status));

    /// <summary>
    /// The seeded suite's three runs, an in-memory database, a chart root that must stay empty, and a
    /// scope factory whose report writer counts each time it is reached and throws.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        private ServiceProvider _provider = default!;

        public ApplicationDbContext Db { get; private init; } = default!;
        public BenchmarkRunExamTests.SeededSuite Seeded { get; private init; } = default!;
        public IConfiguration Configuration { get; private init; } = default!;
        public string ChartRoot { get; private init; } = string.Empty;
        public BenchmarkReportPackJobManager Jobs { get; } = new();
        public int WriterResolutions { get; private set; }

        public static async Task<Harness> CreateAsync()
        {
            var options = BenchmarkRunExamTests.InMemoryOptions();
            var seeded = await BenchmarkRunExamTests.SeedSuiteWithRunsAsync(options);
            string root = Path.Combine(Path.GetTempPath(), "OverseerLayoutPreviewTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Benchmark:Compliance:MaxRunsPerHour"] = "100",
                ["Benchmark:Compliance:MaxRunsPerDay"] = "100",
                [BenchmarkReportChartStore.ConfigurationKey] = root
            }).Build();

            var harness = new Harness
            {
                Db = new ApplicationDbContext(options),
                Seeded = seeded,
                Configuration = configuration,
                ChartRoot = root
            };

            var services = new ServiceCollection();
            services.AddScoped<BenchmarkReportPackService>(_ =>
            {
                harness.WriterResolutions++;
                throw new InvalidOperationException("The layout preview reached the report writer.");
            });
            services.AddScoped<IBenchmarkRunReportWriter>(_ =>
            {
                harness.WriterResolutions++;
                throw new InvalidOperationException("The layout preview reached the report writer.");
            });
            harness._provider = services.BuildServiceProvider();
            return harness;
        }

        public AdminBenchmarkReportPacksController Controller() => new(
            Db,
            Jobs,
            new BenchmarkComplianceGuard(Configuration, Db),
            new BenchmarkModelComparisonService(Db),
            new ModelPricingService(new ModelMetadataService(), Db),
            new EndpointPolicy(Configuration),
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Configuration);

        /// <summary>A comparison-scope request over the three seeded runs, covering <paramref name="covered"/> or every run.</summary>
        public BenchmarkReportLayoutPreviewRequest ComparisonRequest(BenchmarkReportAudience audience, IEnumerable<string>? covered = null) => new()
        {
            RunIds = Seeded.RunIds.ToList(),
            PricingBasis = BenchmarkModelComparisonPricingBasis.AsRun,
            Scope = BenchmarkReportScope.Comparison,
            CoveredEntryKeys = covered?.ToList(),
            Audiences = new List<BenchmarkReportAudience> { audience },
            Audience = audience,
            Paper = "a4",
            Naming = "named"
        };

        /// <summary>A model-scope request over the given runs, about the first.</summary>
        public BenchmarkReportLayoutPreviewRequest ModelRequest(BenchmarkReportAudience audience, params long[] runIds) => new()
        {
            RunIds = runIds.ToList(),
            PricingBasis = BenchmarkModelComparisonPricingBasis.AsRun,
            SubjectKey = $"run:{runIds[0]}",
            Audiences = new List<BenchmarkReportAudience> { audience },
            Audience = audience,
            Paper = "a4",
            Naming = "named"
        };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _provider.DisposeAsync();
            try
            {
                Directory.Delete(ChartRoot, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp folder does not fail a test.
            }
        }
    }
}

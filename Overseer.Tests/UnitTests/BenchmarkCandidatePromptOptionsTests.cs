using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Xunit;
using ParallelExecutionMode = MobileGnollHackLogger.Data.ParallelExecutionMode;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Pins the production candidate prompt and its comparability record. A benchmark-only addition
/// (an information condition, experimental changes) must leave every production run's prompt
/// bytes, canonical options JSON and signature exactly as they were, or every later run becomes
/// NotComparable with the history. A deliberate prompt edit updates these literals.
/// </summary>
public class BenchmarkCandidatePromptOptionsTests
{
    private const string ProductionPromptSha256 = "135f0a86150d993d2aa5a2e2e8c25c45e1b64286ef37754faf71fb78db5e158c";
    private const string ProductionCanonicalJson =
        "{\"verboseMode\":false,\"spoilerFreeMode\":false,\"overseerMode\":0,\"enableToolUse\":true,\"enableWebSearch\":false,\"allowSourceCodeReferences\":true,\"enableSubAgents\":false,\"isGameOn\":false,\"developerMode\":false,\"hasMessageHistory\":false,\"hasWikiContext\":false,\"hasGameSnapshot\":false}";

    [Fact]
    public void TheProductionPrompt_IsByteIdenticalToThePinnedBuild()
    {
        var chatService = CreateChatService();
        var options = new BenchmarkCandidatePromptOptions();

        string flat = options.BuildSystemPrompt(chatService, ParallelExecutionMode.Enabled);

        Assert.Equal(ProductionPromptSha256, Sha256(flat));
    }

    [Fact]
    public void TheProductionRecord_SerializesAndSignsAsPinned()
    {
        var options = new BenchmarkCandidatePromptOptions();

        Assert.Equal(ProductionCanonicalJson, options.ToCanonicalJson());
        Assert.Equal(ProductionCanonicalJson + "|parallelMode=2",
            options.ComparabilitySignature(ParallelExecutionMode.Enabled));
    }

    [Fact]
    public void TheProductionPrompt_WithANullOrBlankAppendix_EqualsThePromptWithoutTheParameter()
    {
        var chatService = CreateChatService();
        var options = new BenchmarkCandidatePromptOptions();

        string flat = options.BuildSystemPrompt(chatService, ParallelExecutionMode.Enabled);
        Assert.Equal(flat, options.BuildSystemPrompt(chatService, ParallelExecutionMode.Enabled, null));
        Assert.Equal(flat, options.BuildSystemPrompt(chatService, ParallelExecutionMode.Enabled, "  \n"));

        var segments = options.BuildSegmentedSystemPrompt(chatService, ParallelExecutionMode.Enabled);
        Assert.Equal(segments, options.BuildSegmentedSystemPrompt(chatService, ParallelExecutionMode.Enabled, null));
        Assert.Equal(flat, segments.frozenPrefix + segments.sessionPrefix + segments.volatileSuffix);
    }

    [Fact]
    public void WikiContext_ReachesTheVolatileSuffix_AndLeavesTheCachedSegmentsAlone()
    {
        var chatService = CreateChatService();
        var options = new BenchmarkCandidatePromptOptions();
        var context = new[] { "--- Monsters/Gnoll.md ---\nA gnoll carries a flail." };

        var without = options.BuildSegmentedSystemPrompt(chatService, ParallelExecutionMode.Enabled);
        var with = options.BuildSegmentedSystemPrompt(chatService, ParallelExecutionMode.Enabled, null, context);

        Assert.Equal(without.frozenPrefix, with.frozenPrefix);
        Assert.Equal(without.sessionPrefix, with.sessionPrefix);
        Assert.Contains("A gnoll carries a flail.", with.volatileSuffix);
        Assert.Equal(
            with.frozenPrefix + with.sessionPrefix + with.volatileSuffix,
            options.BuildSystemPrompt(chatService, ParallelExecutionMode.Enabled, null, context));
    }

    [Fact]
    public void ARecordWithANullCondition_SerializesWithoutTheKey()
    {
        var options = new BenchmarkCandidatePromptOptions { InformationCondition = null };

        Assert.Equal(ProductionCanonicalJson, options.ToCanonicalJson());
        Assert.DoesNotContain("informationCondition", options.ToCanonicalJson());
        Assert.Equal(BenchmarkInformationCondition.Production, options.Condition);

        var sourceOnly = new BenchmarkCandidatePromptOptions { InformationCondition = BenchmarkInformationConditions.SourceOnlyWire };
        Assert.Contains("\"informationCondition\":\"sourceOnly\"", sourceOnly.ToCanonicalJson());
        Assert.Equal(BenchmarkInformationCondition.SourceOnly,
            BenchmarkCandidatePromptOptions.FromJson(sourceOnly.ToCanonicalJson()).Condition);
    }

    [Fact]
    public void SourceOnly_EndsTheFlatPromptAndTheVolatileSuffixWithTheNotice()
    {
        var chatService = CreateChatService();
        var production = new BenchmarkCandidatePromptOptions();
        var sourceOnly = BenchmarkService.CandidatePromptOptionsFor(
            verboseMode: false, allowSourceCodeReferences: true, hasGameSnapshot: false,
            BenchmarkInformationCondition.SourceOnly);

        string productionFlat = production.BuildSystemPrompt(chatService, ParallelExecutionMode.Enabled);
        string flat = sourceOnly.BuildSystemPrompt(chatService, ParallelExecutionMode.Enabled);

        Assert.True(sourceOnly.EnableToolUse);
        Assert.StartsWith(productionFlat, flat);
        Assert.EndsWith(BenchmarkInformationConditions.SourceOnlyNotice + "\n", flat);
        Assert.NotEqual(productionFlat, flat);

        var productionSegments = production.BuildSegmentedSystemPrompt(chatService, ParallelExecutionMode.Enabled);
        var segments = sourceOnly.BuildSegmentedSystemPrompt(chatService, ParallelExecutionMode.Enabled);
        Assert.Equal(productionSegments.frozenPrefix, segments.frozenPrefix);
        Assert.Equal(productionSegments.sessionPrefix, segments.sessionPrefix);
        Assert.EndsWith(BenchmarkInformationConditions.SourceOnlyNotice + "\n", segments.volatileSuffix);
        Assert.Equal(flat, segments.frozenPrefix + segments.sessionPrefix + segments.volatileSuffix);
    }

    [Fact]
    public void ClosedBook_DisablesTools_AndItsPromptLacksTheToolSections()
    {
        const string ToolSectionMarker = "## Source Code Access";
        var chatService = CreateChatService();
        var closedBook = BenchmarkService.CandidatePromptOptionsFor(
            verboseMode: false, allowSourceCodeReferences: true, hasGameSnapshot: false,
            BenchmarkInformationCondition.ClosedBook);

        Assert.False(closedBook.EnableToolUse);
        Assert.Equal(BenchmarkInformationConditions.ClosedBookWire, closedBook.InformationCondition);

        string productionFlat = new BenchmarkCandidatePromptOptions().BuildSystemPrompt(chatService, ParallelExecutionMode.Enabled);
        string flat = closedBook.BuildSystemPrompt(chatService, ParallelExecutionMode.Enabled);

        Assert.Contains(ToolSectionMarker, productionFlat);
        Assert.DoesNotContain(ToolSectionMarker, flat);
        Assert.DoesNotContain(BenchmarkInformationConditions.SourceOnlyNotice, flat);

        var segments = closedBook.BuildSegmentedSystemPrompt(chatService, ParallelExecutionMode.Enabled);
        Assert.Equal(flat, segments.frozenPrefix + segments.sessionPrefix + segments.volatileSuffix);
    }

    [Fact]
    public void TheSignatureWithoutCondition_OfAClosedBookRecord_EqualsAProductionRecordsSignature()
    {
        var production = BenchmarkService.CandidatePromptOptionsFor(false, true, false);
        var closedBook = BenchmarkService.CandidatePromptOptionsFor(false, true, false, BenchmarkInformationCondition.ClosedBook);
        var sourceOnly = BenchmarkService.CandidatePromptOptionsFor(false, true, false, BenchmarkInformationCondition.SourceOnly);

        string productionSignature = production.ComparabilitySignature(ParallelExecutionMode.Enabled);

        Assert.NotEqual(productionSignature, closedBook.ComparabilitySignature(ParallelExecutionMode.Enabled));
        Assert.NotEqual(productionSignature, sourceOnly.ComparabilitySignature(ParallelExecutionMode.Enabled));
        Assert.Equal(productionSignature, closedBook.ComparabilitySignatureWithoutCondition(ParallelExecutionMode.Enabled));
        Assert.Equal(productionSignature, sourceOnly.ComparabilitySignatureWithoutCondition(ParallelExecutionMode.Enabled));
        Assert.Equal(productionSignature, production.ComparabilitySignatureWithoutCondition(ParallelExecutionMode.Enabled));

        // Another option still separates the two.
        var verboseClosedBook = BenchmarkService.CandidatePromptOptionsFor(true, true, false, BenchmarkInformationCondition.ClosedBook);
        Assert.NotEqual(productionSignature, verboseClosedBook.ComparabilitySignatureWithoutCondition(ParallelExecutionMode.Enabled));
    }

    [Fact]
    public void AVariantAppendix_EndsThePrompt_AfterTheSourceOnlyNotice()
    {
        const string Appendix = "When the question is about the player's own character, read the character first.";
        var chatService = CreateChatService();
        var sourceOnly = BenchmarkService.CandidatePromptOptionsFor(false, true, false, BenchmarkInformationCondition.SourceOnly);

        string flat = sourceOnly.BuildSystemPrompt(chatService, ParallelExecutionMode.Enabled, "\n" + Appendix + "\n\n");

        Assert.EndsWith(BenchmarkInformationConditions.SourceOnlyNotice + "\n\n" + Appendix + "\n", flat);

        var segments = sourceOnly.BuildSegmentedSystemPrompt(chatService, ParallelExecutionMode.Enabled, Appendix);
        Assert.EndsWith(BenchmarkInformationConditions.SourceOnlyNotice + "\n\n" + Appendix + "\n", segments.volatileSuffix);
        Assert.Equal(flat, segments.frozenPrefix + segments.sessionPrefix + segments.volatileSuffix);

        // Under production the appendix alone follows the production prompt.
        var production = new BenchmarkCandidatePromptOptions();
        string productionFlat = production.BuildSystemPrompt(chatService, ParallelExecutionMode.Enabled);
        string withAppendix = production.BuildSystemPrompt(chatService, ParallelExecutionMode.Enabled, Appendix);
        Assert.StartsWith(productionFlat, withAppendix);
        Assert.EndsWith(Appendix + "\n", withAppendix);
        Assert.DoesNotContain(BenchmarkInformationConditions.SourceOnlyNotice, withAppendix);
    }

    [Fact]
    public void Describe_NamesANonProductionCondition_Only()
    {
        Assert.DoesNotContain("information condition", new BenchmarkCandidatePromptOptions().Describe());

        string closedBook = BenchmarkService.CandidatePromptOptionsFor(false, true, false, BenchmarkInformationCondition.ClosedBook).Describe();
        Assert.Contains("tools: disabled", closedBook);
        Assert.Contains("information condition: " + BenchmarkInformationCondition.ClosedBook.DisplayName(), closedBook);
    }

    [Fact]
    public void FeedbackToolWithheld_False_IsOmittedFromTheCanonicalJson()
    {
        var options = new BenchmarkCandidatePromptOptions();

        Assert.False(options.FeedbackToolWithheld);
        Assert.DoesNotContain("feedbackToolWithheld", options.ToCanonicalJson());
        Assert.Equal(ProductionCanonicalJson, options.ToCanonicalJson());
        Assert.Equal(
            ProductionCanonicalJson.Replace("\"hasWikiContext\":false", "\"hasWikiContext\":true"),
            BenchmarkService.CandidatePromptOptionsFor(false, true, false, feedbackToolOffered: true).ToCanonicalJson());
    }

    [Fact]
    public void FeedbackToolWithheld_True_SerializesAndRoundTrips()
    {
        var options = new BenchmarkCandidatePromptOptions { FeedbackToolWithheld = true };

        string json = options.ToCanonicalJson();

        Assert.Contains("\"feedbackToolWithheld\":true", json);
        var read = BenchmarkCandidatePromptOptions.FromJson(json);
        Assert.True(read.FeedbackToolWithheld);
        Assert.Equal(options, read);
        Assert.False(BenchmarkCandidatePromptOptions.FromJson(ProductionCanonicalJson).FeedbackToolWithheld);
    }

    [Fact]
    public void Describe_NamesAWithheldFeedbackTool()
    {
        Assert.DoesNotContain("feedback tool", new BenchmarkCandidatePromptOptions().Describe());

        string production = BenchmarkService.CandidatePromptOptionsFor(false, true, false, feedbackToolOffered: false).Describe();
        Assert.EndsWith("source code references: allowed, feedback tool: withheld", production);

        string closedBook = BenchmarkService.CandidatePromptOptionsFor(
            false, true, false, BenchmarkInformationCondition.ClosedBook, feedbackToolOffered: false).Describe();
        Assert.EndsWith(
            "source code references: allowed, feedback tool: withheld, information condition: "
                + BenchmarkInformationCondition.ClosedBook.DisplayName(),
            closedBook);
    }

    private static string Sha256(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>
    /// A ChatService over an in-memory database, built the way the other prompt tests build one.
    /// Only the prompt builders are exercised through it, so nothing here reaches a provider.
    /// </summary>
    private static ChatService CreateChatService()
    {
        var services = new ServiceCollection();
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) },
            { "PromptCacheSettings:EnableSegmentedPrompt", "true" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

        services.AddSingleton<IConfiguration>(config);
        services.AddHttpClient();
        services.AddSignalR();
        services.AddMemoryCache();
        services.AddSingleton<Overseer.Services.Tools.IClientToolBridge, Overseer.Tests.DummyClientToolBridge>();
        services.AddScoped<Overseer.Services.Tools.ToolRegistry>();
        services.AddScoped<Overseer.Services.Tools.ToolExecutor>();
        services.AddScoped<CryptoService>();
        services.AddScoped<WikiService>();
        services.AddScoped<ModelMetadataService>();
        services.AddScoped<KnowledgeBaseService>();
        services.AddScoped<OngoingChatManager>();
        services.AddScoped<Overseer.Services.Providers.IAiProvider, Overseer.Services.Providers.OpenAiResponsesProvider>();
        services.AddScoped<Overseer.Services.Agents.AgentLoopRunner>();
        services.AddSingleton<ParallelExecutionResolver>();
        services.AddSingleton<Overseer.Services.Privacy.AttachmentValidator>();
        services.AddSingleton<Overseer.Services.Privacy.IAntiMalwareScanner,
            Overseer.Services.Privacy.NullAntiMalwareScanner>();
        services.AddSingleton<Overseer.Services.Privacy.EndpointPolicy>();
        services.AddSingleton<Overseer.Services.Privacy.ConfidentialityPostureService>();
        services.AddSingleton<Overseer.Services.Privacy.IContentKeyRing,
            Overseer.Services.Privacy.ConfigurationContentKeyRing>();
        services.AddSingleton<Overseer.Services.Privacy.ContentProtectionService>();
        services.AddSingleton<Overseer.Services.Privacy.Dlp.DlpScannerService>();
        services.AddSingleton<Overseer.Services.Documents.DocumentParserService>();
        services.AddSingleton<Overseer.Services.Rag.DocumentChunker>();
        services.AddSingleton<Overseer.Services.Rag.IEmbeddingService,
            Overseer.Services.Rag.LocalOnnxEmbeddingService>();
        services.AddSingleton<Overseer.Services.Rag.DocumentRagService>();
        services.AddScoped<Overseer.Services.Rag.RagSidecarStore>();
        services.AddSingleton(sp => new Overseer.Services.Privacy.EphemeralSessionStore(
            sp.GetRequiredService<IConfiguration>(), startSweeper: false));
        services.AddScoped<ChatService>();

        return services.BuildServiceProvider().GetRequiredService<ChatService>();
    }
}

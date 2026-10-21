namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services.Benchmarking;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

/// <summary>
/// Experimental tools and per-request tool guides: an experimental tool is declared and executed
/// only under the instruction variant that names it, a variant's guide replaces the declared
/// description for its own requests alone, and the shared handler is never changed by a request.
/// </summary>
public class ToolRegistryExperimentalToolTests
{
    // Names with no file in ToolGuides, so the registry keeps the descriptions given here.
    private const string ExperimentalTool = "experimental_probe_tool";
    private const string ProductionTool = "production_probe_tool";
    private const string ExperimentalDescription = "Experimental probe fallback description.";
    private const string ProductionDescription = "Production probe description.";
    private const string VariantGuide = "The variant's guide for the production probe.";
    private const string VariantExperimentalGuide = "The variant's guide for the experimental probe.";

    private sealed class ProbeToolHandler : IToolHandler
    {
        private int _executions;

        public ProbeToolHandler(string toolName, string description, bool isExperimental)
        {
            ToolName = toolName;
            Description = description;
            IsExperimental = isExperimental;
        }

        public string ToolName { get; }
        public string Description { get; set; }
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;
        public bool IsExperimental { get; }

        public int Executions => Volatile.Read(ref _executions);

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _executions);
            return Task.FromResult(new ToolResult { Success = true, Content = "probe result" });
        }
    }

    private static (ToolRegistry Registry, ProbeToolHandler Experimental, ProbeToolHandler Production) CreateRegistry()
    {
        var experimental = new ProbeToolHandler(ExperimentalTool, ExperimentalDescription, isExperimental: true);
        var production = new ProbeToolHandler(ProductionTool, ProductionDescription, isExperimental: false);
        var registry = new ToolRegistry(
            new List<IToolHandler> { experimental, production },
            new DummyClientToolBridge(),
            NullLogger<ToolRegistry>.Instance);
        return (registry, experimental, production);
    }

    private static readonly IAiProvider Provider = new OpenAiResponsesProvider(new ConfigurationBuilder().Build());

    private static BenchmarkVariantContext Variant(bool offerExperimental = true) => new()
    {
        Name = "probe-variant",
        Hash = new string('d', 64),
        ToolGuides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ProductionTool] = VariantGuide,
            [ExperimentalTool] = VariantExperimentalGuide
        },
        ExperimentalTools = offerExperimental ? new[] { ExperimentalTool } : Array.Empty<string>()
    };

    /// <summary>The declared tools of one request, by name, with their declared descriptions.</summary>
    private static Dictionary<string, string?> Declared(
        ToolRegistry registry, ToolExecutionContext context, bool enableToolUse = true, IEnumerable<string>? allowed = null)
        => registry.BuildToolsForRequest(Provider, context, false, enableToolUse, false, false, allowedToolNames: allowed)
            .FunctionDeclarations
            .ToDictionary(
                d => ProviderHelper.GetProperty(d, "name")!.ToString()!,
                d => ProviderHelper.GetProperty(d, "description")?.ToString());

    [Fact]
    public void AChatShapedRequest_DeclaresNoExperimentalTool_AndTheProductionDescriptions()
    {
        var (registry, _, _) = CreateRegistry();

        var declared = Declared(registry, new ToolExecutionContext());

        Assert.Equal(ProductionTool, Assert.Single(declared).Key);
        Assert.Equal(ProductionDescription, declared[ProductionTool]);
    }

    [Fact]
    public void AnAllowlistNamingTheExperimentalTool_DoesNotDeclareItWithoutTheVariant()
    {
        var (registry, _, _) = CreateRegistry();

        var declared = Declared(registry, new ToolExecutionContext(), allowed: new[] { ExperimentalTool, ProductionTool });

        Assert.Equal(ProductionTool, Assert.Single(declared).Key);
    }

    [Fact]
    public void ARequestWithTheVariant_DeclaresTheExperimentalTool_AndTheVariantsDescriptions()
    {
        var (registry, experimental, production) = CreateRegistry();

        var declared = Declared(registry, new ToolExecutionContext { Variant = Variant() });

        Assert.Equal(2, declared.Count);
        Assert.Equal(VariantGuide, declared[ProductionTool]);
        Assert.Equal(VariantExperimentalGuide, declared[ExperimentalTool]);

        // The handlers keep their own descriptions.
        Assert.Equal(ProductionDescription, production.Description);
        Assert.Equal(ExperimentalDescription, experimental.Description);
    }

    [Fact]
    public void AVariantThatDoesNotNameTheExperimentalTool_StillWithholdsIt_ButAppliesItsGuides()
    {
        var (registry, _, _) = CreateRegistry();

        var declared = Declared(registry, new ToolExecutionContext { Variant = Variant(offerExperimental: false) });

        Assert.Equal(ProductionTool, Assert.Single(declared).Key);
        Assert.Equal(VariantGuide, declared[ProductionTool]);
    }

    [Fact]
    public void TheAllowlist_StillNarrowsAVariantRequest()
    {
        var (registry, _, _) = CreateRegistry();

        var declared = Declared(registry, new ToolExecutionContext { Variant = Variant() }, allowed: new[] { ProductionTool });

        Assert.Equal(ProductionTool, Assert.Single(declared).Key);
    }

    [Fact]
    public void AnEmptyAllowlist_DeclaresNothing_AndToolUseOff_DeclaresNothing()
    {
        var (registry, _, _) = CreateRegistry();
        var context = new ToolExecutionContext { Variant = Variant() };

        Assert.Empty(Declared(registry, context, allowed: Array.Empty<string>()));
        Assert.Empty(Declared(registry, context, enableToolUse: false));
        Assert.False(registry.BuildToolsForRequest(Provider, context, false, false, false, false).HasTools);
    }

    [Fact]
    public async Task RequestsBuiltConcurrently_WithAndWithoutTheVariant_DoNotLeakIntoEachOther()
    {
        var (registry, experimental, production) = CreateRegistry();
        var variant = Variant();

        var results = await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() =>
        {
            bool withVariant = i % 2 == 0;
            var context = new ToolExecutionContext { Variant = withVariant ? variant : null };
            return (withVariant, Declared: Declared(registry, context));
        }, TestContext.Current.CancellationToken)));

        foreach (var (withVariant, declared) in results)
        {
            if (withVariant)
            {
                Assert.Equal(VariantGuide, declared[ProductionTool]);
                Assert.Equal(VariantExperimentalGuide, declared[ExperimentalTool]);
            }
            else
            {
                Assert.Equal(ProductionTool, Assert.Single(declared).Key);
                Assert.Equal(ProductionDescription, declared[ProductionTool]);
            }
        }

        Assert.Equal(ProductionDescription, production.Description);
        Assert.Equal(ExperimentalDescription, experimental.Description);
    }

    [Fact]
    public void IsEnabledByVariant_AndTheRegistryLookups()
    {
        var (registry, _, _) = CreateRegistry();

        Assert.False(ToolRegistry.IsEnabledByVariant(ExperimentalTool, new ToolExecutionContext()));
        Assert.True(ToolRegistry.IsEnabledByVariant(ExperimentalTool, new ToolExecutionContext { Variant = Variant() }));
        Assert.True(ToolRegistry.IsEnabledByVariant(ExperimentalTool.ToUpperInvariant(), new ToolExecutionContext { Variant = Variant() }));
        Assert.False(ToolRegistry.IsEnabledByVariant(ExperimentalTool, new ToolExecutionContext { Variant = Variant(offerExperimental: false) }));

        Assert.True(registry.IsExperimentalTool(ExperimentalTool));
        Assert.False(registry.IsExperimentalTool(ProductionTool));
        Assert.False(registry.IsExperimentalTool("no_such_tool"));
        Assert.True(registry.IsRegisteredTool(ProductionTool));
        Assert.True(registry.IsRegisteredTool(ExperimentalTool));
        Assert.False(registry.IsRegisteredTool("no_such_tool"));
    }

    [Fact]
    public void CloneFor_CarriesTheVariant()
    {
        var variant = Variant();
        var context = new ToolExecutionContext { Variant = variant, UserId = "user" };

        var clone = context.CloneFor("call-1");

        Assert.Same(variant, clone.Variant);
        Assert.Equal("call-1", clone.ToolCallId);
        Assert.Null(new ToolExecutionContext().CloneFor("call-2").Variant);
    }

    private static ToolExecutor CreateExecutor(MemoryCache cache, params IToolHandler[] handlers)
        => new(
            handlers,
            new DummyClientToolBridge(),
            NullLogger<ToolExecutor>.Instance,
            cache,
            new ConfigurationBuilder().Build());

    [Fact]
    public async Task TheExecutor_RefusesAnExperimentalCall_WithoutTheVariant()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        var experimental = new ProbeToolHandler(ExperimentalTool, ExperimentalDescription, isExperimental: true);
        var executor = CreateExecutor(cache, experimental);
        var parameters = JsonDocument.Parse("{}").RootElement;

        var refused = await executor.ExecuteAsync(ExperimentalTool, parameters, new ToolExecutionContext
        {
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(7)
        }, TestContext.Current.CancellationToken);

        Assert.False(refused.Success);
        Assert.Equal($"Tool '{ExperimentalTool}' is not available in this session.", refused.ErrorMessage);
        Assert.Equal(0, experimental.Executions);

        var otherVariant = await executor.ExecuteAsync(ExperimentalTool, parameters, new ToolExecutionContext
        {
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(7),
            Variant = Variant(offerExperimental: false)
        }, TestContext.Current.CancellationToken);

        Assert.False(otherVariant.Success);
        Assert.Equal(0, experimental.Executions);
    }

    [Fact]
    public async Task TheExecutor_RunsAnExperimentalCall_UnderTheVariant_AndAProductionCallWithout()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        var experimental = new ProbeToolHandler(ExperimentalTool, ExperimentalDescription, isExperimental: true);
        var production = new ProbeToolHandler(ProductionTool, ProductionDescription, isExperimental: false);
        var executor = CreateExecutor(cache, experimental, production);
        var parameters = JsonDocument.Parse("{}").RootElement;

        var allowed = await executor.ExecuteAsync(ExperimentalTool, parameters, new ToolExecutionContext
        {
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(8),
            Variant = Variant()
        }, TestContext.Current.CancellationToken);

        Assert.True(allowed.Success, allowed.ErrorMessage);
        Assert.Equal(1, experimental.Executions);

        var plain = await executor.ExecuteAsync(ProductionTool, parameters, new ToolExecutionContext
        {
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(8)
        }, TestContext.Current.CancellationToken);

        Assert.True(plain.Success, plain.ErrorMessage);
        Assert.Equal(1, production.Executions);
    }
}

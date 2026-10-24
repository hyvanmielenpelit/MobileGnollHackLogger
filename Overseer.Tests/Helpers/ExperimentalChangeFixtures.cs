using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Services.Tools;
using StoredChange = MobileGnollHackLogger.Data.BenchmarkExperimentalChange;

namespace Overseer.Tests.Helpers;

/// <summary>A tool handler with a fixed description and parameter schema.</summary>
internal sealed class FakeChangeToolHandler : IToolHandler
{
    private readonly string _schema;

    public FakeChangeToolHandler(string toolName, string description = "A fake tool.", bool isExperimental = false, string schema = "{}")
    {
        ToolName = toolName;
        Description = description;
        IsExperimental = isExperimental;
        _schema = schema;
    }

    public string ToolName { get; }
    public string Description { get; set; }
    public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
    public ToolCategory Category => ToolCategory.InformationRetrieval;
    public JsonElement ParameterSchema => JsonDocument.Parse(_schema).RootElement;
    public bool IsExperimental { get; }

    public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        => Task.FromResult(new ToolResult { Success = true, Content = "ok" });
}

/// <summary>A temporary <c>BenchmarkChanges</c> root that the fixture deletes.</summary>
internal sealed class ExperimentalChangeRoot : IDisposable
{
    public ExperimentalChangeRoot()
    {
        Root = Path.Combine(Path.GetTempPath(), "BenchmarkChanges_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>Writes a change folder: its <c>change.json</c> (when given) and the named files.</summary>
    public string Write(string folder, string? changeJson, params (string Name, string Content)[] files)
    {
        string path = Path.Combine(Root, folder);
        Directory.CreateDirectory(path);
        if (changeJson != null)
        {
            File.WriteAllText(Path.Combine(path, BenchmarkExperimentalChangeCatalog.ChangeFileName), changeJson);
        }
        foreach (var (name, content) in files)
        {
            File.WriteAllText(Path.Combine(path, name), content);
        }
        return path;
    }

    public void WriteRetired(params string[] ids)
        => File.WriteAllText(Path.Combine(Root, BenchmarkExperimentalChangeCatalog.RetiredFileName), JsonSerializer.Serialize(ids));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Change folders, handlers and catalogs for the experimental-change tests.</summary>
internal static class ExperimentalChangeFixtures
{
    public const string WikiDescription = "Production wiki_search description.";
    public const string ItemDescription = "Production get_item_stats description.";
    public const string CharacterSchema = "{\"type\":\"object\",\"properties\":{\"race\":{\"type\":\"string\"}}}";

    /// <summary>
    /// wiki_search, get_item_stats, get_monster_stats and get_artifact_stats; get_save_info, which the
    /// benchmark allowlist does not name; and the experimental get_character_info.
    /// </summary>
    public static IToolHandler[] Handlers() => new IToolHandler[]
    {
        new FakeChangeToolHandler("wiki_search", WikiDescription),
        new FakeChangeToolHandler("get_item_stats", ItemDescription),
        new FakeChangeToolHandler("get_monster_stats", "Production get_monster_stats description."),
        new FakeChangeToolHandler("get_artifact_stats", "Production get_artifact_stats description."),
        new FakeChangeToolHandler("get_save_info", "Production get_save_info description."),
        new FakeChangeToolHandler("get_character_info", "Declared get_character_info description.", isExperimental: true, schema: CharacterSchema)
    };

    /// <summary>A <c>change.json</c> naming the given contents.</summary>
    public static string ChangeJson(
        string id,
        string? title = null,
        string summary = "A probe summary.",
        string? expectedEffect = null,
        string? experimentalTool = null,
        string? experimentalToolDescription = null,
        string? experiment = null,
        Dictionary<string, string>? additions = null,
        Dictionary<string, string>? replacements = null,
        string? instructions = null)
        => JsonSerializer.Serialize(new BenchmarkExperimentalChangeFile
        {
            Id = id,
            Title = title ?? "Title of " + id,
            Summary = summary,
            ExpectedEffect = expectedEffect,
            ExperimentalTool = experimentalTool,
            ExperimentalToolDescription = experimentalToolDescription,
            Experiment = experiment,
            DescriptionAdditions = additions ?? new Dictionary<string, string>(),
            DescriptionReplacements = replacements ?? new Dictionary<string, string>(),
            Instructions = instructions
        });

    /// <summary>A catalog of the folders under <paramref name="root"/>, without database changes unless <paramref name="loadStored"/> reads them.</summary>
    public static BenchmarkExperimentalChangeCatalog Catalog(
        string root,
        IEnumerable<IToolHandler>? handlers = null,
        Func<CancellationToken, Task<IReadOnlyList<StoredChange>>>? loadStored = null)
        => new(root, handlers ?? Handlers(), NullLogger.Instance, loadStored);

    /// <summary>A catalog whose database changes are read from <paramref name="options"/>' database.</summary>
    public static BenchmarkExperimentalChangeCatalog Catalog(
        string root, DbContextOptions<ApplicationDbContext> options, IEnumerable<IToolHandler>? handlers = null)
        => Catalog(root, handlers, async ct =>
        {
            await using var db = new ApplicationDbContext(options);
            return await BenchmarkExperimentalChangeCatalog.LoadStoredAsync(db, ct);
        });

    /// <summary>The shipped <c>BenchmarkChanges</c> folder of the test output, or null when it was not copied.</summary>
    public static string? ShippedRoot()
    {
        string path = Path.Combine(AppContext.BaseDirectory, BenchmarkExperimentalChangeCatalog.FolderName);
        return Directory.Exists(path) ? path : null;
    }

    /// <summary>The production guide of <paramref name="tool"/> in the test output's <c>ToolGuides</c>, as the tool registry loads it.</summary>
    public static string? ProductionGuide(string tool)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "ToolGuides", tool + ".md");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>The handlers with the shipped production guides as their live descriptions, as at startup.</summary>
    public static IToolHandler[] ShippedHandlers()
    {
        var handlers = Handlers();
        foreach (var handler in handlers.Where(h => !h.IsExperimental))
        {
            if (ProductionGuide(handler.ToolName) is { } guide) handler.Description = guide;
        }
        return handlers;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class NetHackSourceCodeToolTests : IDisposable
{
    private readonly string _gnollHackDir;
    private readonly string _netHackDir;

    public NetHackSourceCodeToolTests()
    {
        _gnollHackDir = Path.Combine(Path.GetTempPath(), "GnollHackSourceTests_" + Guid.NewGuid().ToString("N"));
        _netHackDir = Path.Combine(Path.GetTempPath(), "NetHackSourceTests_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path.Combine(_gnollHackDir, "src"));
        Directory.CreateDirectory(Path.Combine(_gnollHackDir, "include"));
        Directory.CreateDirectory(Path.Combine(_gnollHackDir, "dat"));

        Directory.CreateDirectory(Path.Combine(_netHackDir, "src"));
        Directory.CreateDirectory(Path.Combine(_netHackDir, "include"));
        Directory.CreateDirectory(Path.Combine(_netHackDir, "dat"));

        // Setup GnollHack mock files (Allman / NetHack style with return type on line above)
        File.WriteAllText(Path.Combine(_gnollHackDir, "src", "potion.c"), @"/* GnollHack potion.c */
#include ""hack.h""

void
potionhit(struct monst *mtmp, struct obj *otmp)
{
    /* GnollHack potion hit logic */
}
");
        File.WriteAllText(Path.Combine(_gnollHackDir, "include", "hack.h"), @"/* GnollHack hack.h */
#define PM_GNOLL 100
#define POTION_TEST 1
enum gnoll_status {
    GNOLL_OK = 0,
    GNOLL_BAD = 1
};
");

        // Setup NetHack mock files
        File.WriteAllText(Path.Combine(_netHackDir, "src", "potion.c"), @"/* NetHack potion.c */
#include ""hack.h""

void
potionhit(struct monst *mtmp, struct obj *otmp)
{
    /* NetHack potion hit logic */
}

void
nethack_special_func()
{
    /* NetHack specific function */
}
");
        File.WriteAllText(Path.Combine(_netHackDir, "include", "hack.h"), @"/* NetHack hack.h */
#define PM_ARCHEOLOGIST 1
#define NETHACK_CONST 42
enum nethack_types {
    NETHACK_VAL_A = 10,
    NETHACK_VAL_B = 20
};
");
        File.WriteAllText(Path.Combine(_netHackDir, "include", "monsters.h"), @"/* NetHack monsters.h */
#define MON_NETHACK 500
");
    }

    public void Dispose()
    {
        if (Directory.Exists(_gnollHackDir))
        {
            try { Directory.Delete(_gnollHackDir, true); } catch { }
        }
        if (Directory.Exists(_netHackDir))
        {
            try { Directory.Delete(_netHackDir, true); } catch { }
        }
    }

    private (SourceCodeService gnollService, NetHackSourceCodeService netService, IConfiguration config) CreateServices()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("SourceCodePath", _gnollHackDir),
                new KeyValuePair<string, string?>("NetHackSourceCodePath", _netHackDir),
                new KeyValuePair<string, string?>("MaxSourceFileSizeKB", "800"),
                new KeyValuePair<string, string?>("Tools:source_code_search:MaxResults", "10"),
                new KeyValuePair<string, string?>("Tools:source_code_search:ContextLines", "5"),
                new KeyValuePair<string, string?>("Tools:source_code_view:LineCount", "50"),
            })
            .Build();

        var gnollService = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
        var netService = new NetHackSourceCodeService(config, NullLogger<NetHackSourceCodeService>.Instance);

        gnollService.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        netService.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        return (gnollService, netService, config);
    }

    [Fact]
    public void NetHackSourceCodeService_IndexesFiles()
    {
        var (gnollService, netService, _) = CreateServices();
        using (gnollService)
        using (netService)
        {
            Assert.True(netService.IsIndexingComplete);
            var listOutput = netService.ListFiles(null, false);
            Assert.Contains("src/potion.c", listOutput);
            Assert.Contains("include/hack.h", listOutput);
            Assert.Contains("include/monsters.h", listOutput);
        }
    }

    [Fact]
    public void NetHackSourceCodeService_UnconfiguredPath_DoesNotThrow()
    {
        var emptyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("NetHackSourceCodePath", "")
            })
            .Build();

        using var netService = new NetHackSourceCodeService(emptyConfig, NullLogger<NetHackSourceCodeService>.Instance);
        var ex = Record.Exception(() => netService.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Null(ex);
        Assert.True(netService.IsIndexingComplete);
    }

    [Fact]
    public void ConfigHealthService_WhenNetHackSourceCodePathMissing_ReturnsAlert()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("NetHackSourceCodePath", ""),
                new KeyValuePair<string, string?>("SentryDSN", "dummy"),
                new KeyValuePair<string, string?>("NetHackWikiPath", "dummy")
            })
            .Build();

        var healthService = new ConfigHealthService(config);
        var alerts = healthService.GetSystemAlerts().ToList();

        Assert.Contains(alerts, a => a.Id == "nethack-source-code-path-missing");
    }

    [Fact]
    public void ConfigHealthService_WhenNetHackSourceCodePathConfigured_NoAlert()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("NetHackSourceCodePath", @"C:\repos\NetHack"),
                new KeyValuePair<string, string?>("SentryDSN", "dummy"),
                new KeyValuePair<string, string?>("NetHackWikiPath", "dummy")
            })
            .Build();

        var healthService = new ConfigHealthService(config);
        var alerts = healthService.GetSystemAlerts().ToList();

        Assert.DoesNotContain(alerts, a => a.Id == "nethack-source-code-path-missing");
    }

    [Fact]
    public async Task SourceCodeSearchTool_Routing()
    {
        var (gnollService, netService, config) = CreateServices();
        using (gnollService)
        using (netService)
        {
            var tool = new SourceCodeSearchTool(gnollService, netService, config);
            var context = new ToolExecutionContext();

            // Query NetHack repo
            var netArgs = JsonDocument.Parse(@"{""query"": ""nethack_special_func"", ""repository"": ""nethack""}").RootElement;
            var netResult = await tool.ExecuteAsync(netArgs, context, CancellationToken.None);
            Assert.True(netResult.Success);
            Assert.Contains("src/potion.c", netResult.Content);
            Assert.Contains("nethack_special_func", netResult.Content);

            // Query default (GnollHack) repo
            var gnollArgs = JsonDocument.Parse(@"{""query"": ""potionhit""}").RootElement;
            var gnollResult = await tool.ExecuteAsync(gnollArgs, context, CancellationToken.None);
            Assert.True(gnollResult.Success);
            Assert.Contains("src/potion.c", gnollResult.Content);
            Assert.Contains("potionhit", gnollResult.Content);

            // Query GnollHack repo for NetHack-specific function should return no matches
            var notFoundArgs = JsonDocument.Parse(@"{""query"": ""nethack_special_func"", ""repository"": ""gnollhack""}").RootElement;
            var notFoundResult = await tool.ExecuteAsync(notFoundArgs, context, CancellationToken.None);
            Assert.True(notFoundResult.Success);
            Assert.Contains("No relevant source code found", notFoundResult.Content);
        }
    }

    [Fact]
    public async Task SourceCodeViewTool_RoutingAndPathValidation()
    {
        var (gnollService, netService, config) = CreateServices();
        using (gnollService)
        using (netService)
        {
            var tool = new SourceCodeViewTool(gnollService, netService, config);
            var context = new ToolExecutionContext();

            // View file from NetHack repo
            var netArgs = JsonDocument.Parse(@"{""file"": ""src/potion.c"", ""start_line"": 1, ""repository"": ""nethack""}").RootElement;
            var netResult = await tool.ExecuteAsync(netArgs, context, CancellationToken.None);
            Assert.True(netResult.Success);
            Assert.Contains("NetHack potion.c", netResult.Content);

            // View file from GnollHack repo
            var gnollArgs = JsonDocument.Parse(@"{""file"": ""src/potion.c"", ""start_line"": 1}").RootElement;
            var gnollResult = await tool.ExecuteAsync(gnollArgs, context, CancellationToken.None);
            Assert.True(gnollResult.Success);
            Assert.Contains("GnollHack potion.c", gnollResult.Content);

            // Path traversal should fail
            var traversalArgs = JsonDocument.Parse(@"{""file"": ""../outside.c"", ""start_line"": 1, ""repository"": ""nethack""}").RootElement;
            var traversalResult = await tool.ExecuteAsync(traversalArgs, context, CancellationToken.None);
            Assert.False(traversalResult.Success);
        }
    }

    [Fact]
    public async Task ListIndexedFilesTool_Routing()
    {
        var (gnollService, netService, _) = CreateServices();
        using (gnollService)
        using (netService)
        {
            var tool = new ListIndexedFilesTool(gnollService, netService);
            var context = new ToolExecutionContext();

            // List NetHack files
            var netArgs = JsonDocument.Parse(@"{""repository"": ""nethack""}").RootElement;
            var netResult = await tool.ExecuteAsync(netArgs, context, CancellationToken.None);
            Assert.True(netResult.Success);
            Assert.Contains("include/monsters.h", netResult.Content);

            // List GnollHack files
            var gnollArgs = JsonDocument.Parse(@"{}").RootElement;
            var gnollResult = await tool.ExecuteAsync(gnollArgs, context, CancellationToken.None);
            Assert.True(gnollResult.Success);
            Assert.DoesNotContain("include/monsters.h", gnollResult.Content);
        }
    }

    [Fact]
    public async Task GetConstantsTool_Routing()
    {
        var (gnollService, netService, _) = CreateServices();
        using (gnollService)
        using (netService)
        {
            var tool = new GetConstantsTool(gnollService, netService);
            var context = new ToolExecutionContext();

            // Look up NetHack constant
            var netArgs = JsonDocument.Parse(@"{""name"": ""NETHACK_CONST"", ""repository"": ""nethack""}").RootElement;
            var netResult = await tool.ExecuteAsync(netArgs, context, CancellationToken.None);
            Assert.True(netResult.Success);
            Assert.Contains("#define NETHACK_CONST 42", netResult.Content);

            // Look up GnollHack constant
            var gnollArgs = JsonDocument.Parse(@"{""name"": ""PM_GNOLL""}").RootElement;
            var gnollResult = await tool.ExecuteAsync(gnollArgs, context, CancellationToken.None);
            Assert.True(gnollResult.Success);
            Assert.Contains("#define PM_GNOLL 100", gnollResult.Content);
        }
    }

    [Fact]
    public async Task SearchDefinitionsTool_Routing()
    {
        var (gnollService, netService, _) = CreateServices();
        using (gnollService)
        using (netService)
        {
            var tool = new SearchDefinitionsTool(gnollService, netService);
            var context = new ToolExecutionContext();

            // Search NetHack definition
            var netArgs = JsonDocument.Parse(@"{""symbol"": ""nethack_special_func"", ""kind"": ""function"", ""repository"": ""nethack""}").RootElement;
            var netResult = await tool.ExecuteAsync(netArgs, context, CancellationToken.None);
            Assert.True(netResult.Success);
            Assert.Contains("nethack_special_func", netResult.Content);
        }
    }

    [Fact]
    public async Task GetFunctionDefinitionTool_Routing()
    {
        var (gnollService, netService, _) = CreateServices();
        using (gnollService)
        using (netService)
        {
            var tool = new GetFunctionDefinitionTool(gnollService, netService);
            var context = new ToolExecutionContext();

            // Get function body from NetHack
            var netArgs = JsonDocument.Parse(@"{""name"": ""nethack_special_func"", ""repository"": ""nethack""}").RootElement;
            var netResult = await tool.ExecuteAsync(netArgs, context, CancellationToken.None);
            Assert.True(netResult.Success);
            Assert.Contains("NetHack specific function", netResult.Content);
        }
    }

    [Fact]
    public async Task ToolGuard_IndexingInProgress()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("NetHackSourceCodePath", _netHackDir),
                new KeyValuePair<string, string?>("SourceCodePath", _gnollHackDir)
            })
            .Build();

        // Create services WITHOUT starting indexing
        using var gnollService = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
        using var netService = new NetHackSourceCodeService(config, NullLogger<NetHackSourceCodeService>.Instance);

        var tool = new ListIndexedFilesTool(gnollService, netService);
        var context = new ToolExecutionContext();

        var netArgs = JsonDocument.Parse(@"{""repository"": ""nethack""}").RootElement;
        var result = await tool.ExecuteAsync(netArgs, context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ToolGuardMessages.NetHackSourceCodeIndexingInProgress, result.ErrorMessage);
    }
}

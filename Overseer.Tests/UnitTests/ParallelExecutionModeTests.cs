using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Tools;
using ParallelExecutionMode = MobileGnollHackLogger.Data.ParallelExecutionMode;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ParallelExecutionModeTests
{
    private static IConfiguration CreateConfig(bool enforce = true, int defaultMode = 2, bool showBadge = true)
    {
        var dict = new Dictionary<string, string?>
        {
            { "ParallelExecutionSettings:EnforcePerKeyMode", enforce.ToString() },
            { "ParallelExecutionSettings:DefaultMode", defaultMode.ToString() },
            { "ParallelExecutionSettings:ShowBadge", showBadge.ToString() }
        };
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void Resolver_EnforceTrue_ReturnsUserKeyMode()
    {
        var config = CreateConfig(enforce: true, defaultMode: 2);
        var resolver = new ParallelExecutionResolver(config);

        var key = new UserAiApiKey { ParallelExecutionMode = ParallelExecutionMode.Disabled };
        var actual = resolver.Resolve(null, key);
        Assert.Equal(ParallelExecutionMode.Disabled, actual);
    }

    [Fact]
    public void Resolver_EnforceTrue_ReturnsSystemConfigMode()
    {
        var config = CreateConfig(enforce: true, defaultMode: 2);
        var resolver = new ParallelExecutionResolver(config);

        var sys = new SystemAiApiConfiguration { ParallelExecutionMode = ParallelExecutionMode.OnRequest };
        var actual = resolver.Resolve(sys, null);
        Assert.Equal(ParallelExecutionMode.OnRequest, actual);
    }

    [Fact]
    public void Resolver_EnforceFalse_AlwaysReturnsEnabled()
    {
        var config = CreateConfig(enforce: false, defaultMode: 0);
        var resolver = new ParallelExecutionResolver(config);

        var key = new UserAiApiKey { ParallelExecutionMode = ParallelExecutionMode.Disabled };
        var actual = resolver.Resolve(null, key);
        Assert.Equal(ParallelExecutionMode.Enabled, actual);
    }

    [Fact]
    public void Resolver_NullEntity_FallsBackToConfiguredDefault()
    {
        var config = CreateConfig(enforce: true, defaultMode: (int)ParallelExecutionMode.OnRequest);
        var resolver = new ParallelExecutionResolver(config);

        var actual = resolver.Resolve(null, null);
        Assert.Equal(ParallelExecutionMode.OnRequest, actual);
    }

    [Fact]
    public async Task Resolver_ResolveAsync_ReadsUserKeyFromDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var config = CreateConfig(enforce: true, defaultMode: 2);
        var resolver = new ParallelExecutionResolver(config);

        using (var db = new ApplicationDbContext(options))
        {
            db.UserAiApiKeys.Add(new UserAiApiKey
            {
                AspNetUserId = "user-123",
                Provider = "OpenAI",
                ParallelExecutionMode = ParallelExecutionMode.Disabled
            });
            await db.SaveChangesAsync();

            var resolved = await resolver.ResolveAsync("user-123", "OpenAI", null, db);
            Assert.Equal(ParallelExecutionMode.Disabled, resolved);
        }
    }

    [Fact]
    public async Task Resolver_ResolveAsync_ReadsSystemConfigFromDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var config = CreateConfig(enforce: true, defaultMode: 2);
        var resolver = new ParallelExecutionResolver(config);

        using (var db = new ApplicationDbContext(options))
        {
            var sysConfig = new SystemAiApiConfiguration
            {
                DisplayName = "Test System Model",
                Provider = "Anthropic",
                ModelId = "claude-3-5-sonnet",
                ParallelExecutionMode = ParallelExecutionMode.OnRequest
            };
            db.SystemAiApiConfigurations.Add(sysConfig);
            await db.SaveChangesAsync();

            var resolved = await resolver.ResolveAsync(null, null, sysConfig.Id, db);
            Assert.Equal(ParallelExecutionMode.OnRequest, resolved);
        }
    }

    [Fact]
    public void ToolExecutionContext_CloneFor_PreservesParallelExecutionMode()
    {
        var budget = new AgentRunBudget { MaxParallelSubAgents = 1 };
        var original = new ToolExecutionContext
        {
            SessionId = 42,
            UserId = "user-1",
            ParallelExecutionMode = ParallelExecutionMode.Disabled,
            Budget = budget,
            AgentDepth = 0,
            MaxAgentDepth = 2
        };

        var cloned = original.CloneFor("call_123");

        Assert.Equal(ParallelExecutionMode.Disabled, cloned.ParallelExecutionMode);
        Assert.Equal("call_123", cloned.ToolCallId);
    }

    [Fact]
    public void ToolRegistry_GetParallelOverrideText_ReturnsCorrectContent()
    {
        var config = CreateConfig();
        var registry = new ToolRegistry(
            Array.Empty<IToolHandler>(),
            new DummyClientToolBridge(),
            NullLogger<ToolRegistry>.Instance,
            null,
            null,
            config);

        var disabledText = registry.GetParallelOverrideText(ParallelExecutionMode.Disabled);
        Assert.Contains("Parallel Execution Policy: Sequential Only", disabledText);
        Assert.Contains("Issue tool calls one at a time", disabledText);

        var onRequestText = registry.GetParallelOverrideText(ParallelExecutionMode.OnRequest);
        Assert.Contains("Parallel Execution Policy: On Request Only", onRequestText);
        Assert.Contains("Issue tool calls one at a time unless the player has explicitly asked for parallel", onRequestText);

        var enabledText = registry.GetParallelOverrideText(ParallelExecutionMode.Enabled);
        Assert.Equal(string.Empty, enabledText);
    }

    [Fact]
    public void AgentRunBudget_EnforcesMaxParallelSubAgents()
    {
        var budget = new AgentRunBudget
        {
            MaxTotalModelCalls = 10,
            MaxSubAgentRuns = 5,
            MaxParallelSubAgents = 1
        };

        var first = budget.TryStartSubAgent(false, out var err1);
        Assert.True(first);
        Assert.Null(err1);

        var second = budget.TryStartSubAgent(false, out var err2);
        Assert.False(second);
        Assert.Contains("Maximum concurrent subagent runs (1) reached", err2);

        budget.EndSubAgent();

        var third = budget.TryStartSubAgent(false, out var err3);
        Assert.True(third);
        Assert.Null(err3);
    }
}

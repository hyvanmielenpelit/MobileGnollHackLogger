using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Covers <see cref="GetFunctionDefinitionTool"/>'s kind fallback: a requested kind with no match
/// retries as "any" and returns the other kind's definition behind a one-line note, while a kind
/// that does match is never touched by the retry. Also covers the reachability note the tool adds
/// after a function nothing calls.
/// </summary>
public class GetFunctionDefinitionToolFallbackTests : IDisposable
{
    private readonly string _sourceDir;

    public GetFunctionDefinitionToolFallbackTests()
    {
        _sourceDir = Path.Combine(Path.GetTempPath(), "GetFunctionDefinitionToolFallbackTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "src"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "include"));

        // 'foo' exists only as a macro, and only in a header — the function branch of the symbol
        // scan is .c-only, so "function" cannot match it. It sits on line 3 so the extraction's
        // two-line leading context is unrelated to the definition itself.
        File.WriteAllText(Path.Combine(_sourceDir, "include", "defs.h"),
            "/* defs.h */\r\n" +
            "#define OTHER_LIMIT 1\r\n" +
            "#define foo(x) ((x) * 2)\r\n" +
            "int baz;\r\n");

        // 'bar' exists as a function, so a "function" request matches outright.
        File.WriteAllText(Path.Combine(_sourceDir, "src", "util.c"),
            "/* util.c */\r\n" +
            "#include \"defs.h\"\r\n" +
            "int\r\n" +
            "bar(void)\r\n" +
            "{\r\n" +
            "    return 1;\r\n" +
            "}\r\n");

        // 'qux' has a live caller; 'bar' above has none.
        File.WriteAllText(Path.Combine(_sourceDir, "src", "live.c"),
            "int\r\n" +
            "qux(void)\r\n" +
            "{\r\n" +
            "    return 2;\r\n" +
            "}\r\n" +
            "\r\n" +
            "void\r\n" +
            "call_qux(void)\r\n" +
            "{\r\n" +
            "    qux();\r\n" +
            "}\r\n");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sourceDir)) Directory.Delete(_sourceDir, true);
        }
        catch (IOException)
        {
            /* A temp directory the OS still holds a handle on is not a test failure. */
        }
    }

    private IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("SourceCodePath", _sourceDir),
                new KeyValuePair<string, string?>("MaxSourceFileSizeKB", "800")
            })
            .Build();
    }

    private static ToolExecutionContext CreateContext(long sessionId)
        => new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(sessionId) };

    private async Task<string> ExecuteAsync(string name, string type, long sessionId, bool withLivenessIndex = false)
    {
        var config = CreateConfiguration();

        using var service = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
        await service.StartAsync(CancellationToken.None);

        // The NetHack repository is left unconfigured: nothing here selects it.
        using var netHackService = new NetHackSourceCodeService(config, NullLogger<NetHackSourceCodeService>.Instance);

        var livenessIndex = withLivenessIndex ? SourceLivenessIndex.ForSourceCodeService(service, 800) : null;
        var tool = new GetFunctionDefinitionTool(service, netHackService, livenessIndex);
        var arguments = JsonDocument.Parse($"{{\"name\": \"{name}\", \"type\": \"{type}\"}}").RootElement;

        var result = await tool.ExecuteAsync(arguments, CreateContext(sessionId), CancellationToken.None);

        Assert.True(result.Success);
        return result.Content;
    }

    /// <summary>
    /// 'foo' is a macro and nothing else, so a "function" request returns the macro behind the note
    /// — which names the kind actually found, read off the declaring line rather than the leading
    /// context line the extraction opens with.
    /// </summary>
    [Fact]
    public async Task FunctionRequest_NameIsOnlyAMacro_ReturnsTheMacroBehindTheNote()
    {
        string content = await ExecuteAsync("foo", "function", 3001);

        Assert.StartsWith("[No function named 'foo' in the indexed gnollhack source; showing the macro definition instead.]\n", content);
        Assert.Contains("--- include/defs.h:L1-L3 (foo, 3 lines) ---", content);
        Assert.Contains("#define foo(x) ((x) * 2)", content);
    }

    /// <summary>'bar' is a function, so the requested kind matches and no note is prepended.</summary>
    [Fact]
    public async Task FunctionRequest_NameIsAFunction_ReturnsTheBodyWithNoNote()
    {
        string content = await ExecuteAsync("bar", "function", 3002);

        Assert.DoesNotContain("showing the", content);
        Assert.StartsWith("--- src/util.c:", content);
        Assert.Contains("bar(void)", content);
        Assert.Contains("return 1;", content);
    }

    /// <summary>
    /// A name that exists under no kind at all falls through to the ordinary miss payload — the
    /// retry adds nothing when it misses too.
    /// </summary>
    [Fact]
    public async Task MacroRequest_NameDoesNotExist_ReturnsTheMissPayload()
    {
        string content = await ExecuteAsync("nosuch", "macro", 3003);

        Assert.StartsWith("No definition found for '", content);
        Assert.Contains("nosuch", content);
        Assert.DoesNotContain("showing the", content);
    }

    /// <summary>'bar' is a function nothing calls, so its body is followed by the reachability note.</summary>
    [Fact]
    public async Task FunctionRequest_AFunctionNothingCalls_GetsTheReachabilityNoteAfterTheBody()
    {
        string content = await ExecuteAsync("bar", "function", 3004, withLivenessIndex: true);

        const string note = "[Not reachable: bar() has no live call site in the indexed source, so the game never runs it.]";
        Assert.StartsWith("--- src/util.c:", content);
        Assert.Contains(note, content);
        Assert.True(content.IndexOf(note, StringComparison.Ordinal) > content.IndexOf("return 1;", StringComparison.Ordinal));
        Assert.Equal(2, content.Split("[Not reachable:").Length);
    }

    /// <summary>'qux' has a live caller, so no reachability note is added.</summary>
    [Fact]
    public async Task FunctionRequest_AFunctionWithALiveCaller_GetsNoReachabilityNote()
    {
        string content = await ExecuteAsync("qux", "function", 3005, withLivenessIndex: true);

        Assert.Contains("return 2;", content);
        Assert.DoesNotContain("[Not reachable:", content);
    }

    /// <summary>A macro returned by the kind fallback is no function definition, so it gets no reachability note.</summary>
    [Fact]
    public async Task FunctionRequest_NameIsOnlyAMacro_GetsNoReachabilityNote()
    {
        string content = await ExecuteAsync("foo", "function", 3006, withLivenessIndex: true);

        Assert.Contains("#define foo(x) ((x) * 2)", content);
        Assert.DoesNotContain("[Not reachable:", content);
    }
}

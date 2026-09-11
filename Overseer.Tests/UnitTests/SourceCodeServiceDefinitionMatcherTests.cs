namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services;
using Xunit;

/// <summary>
/// Covers the two definition-matcher additions in <see cref="SourceCodeService.FindDefinition"/>
/// and its body-locator counterpart: a one-line "&lt;type tokens&gt; name(" function definition, and
/// the closing line of a multi-line typedef block. Also pins the exclusions those additions must not
/// widen into — extern/FDECL/OVL prototypes and bare call statements.
/// </summary>
public class SourceCodeServiceDefinitionMatcherTests : IDisposable
{
    private readonly string _sourceDir;

    public SourceCodeServiceDefinitionMatcherTests()
    {
        _sourceDir = Path.Combine(Path.GetTempPath(), "SourceCodeServiceDefinitionMatcherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "src"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "include"));

        // One-line function definition (lib_print_glyph), the NetHack two-line K&R style
        // (old_style_fn), an extern prototype, an FDECL prototype, a STATIC_OVL prototype, and a
        // bare call statement plus an indented "return" call — none of the last four are definitions.
        File.WriteAllText(Path.Combine(_sourceDir, "src", "funcs.c"),
            "/* funcs.c */\r\n" +
            "#include \"hack.h\"\r\n" +
            "extern void extern_fn(int x);\r\n" +
            "STATIC_DCL void FDECL(fdecl_fn, (int));\r\n" +
            "STATIC_OVL void ovl_fn(int x);\r\n" +
            "void lib_print_glyph(winid wid, int x)\r\n" +
            "{\r\n" +
            "    print(wid, x);\r\n" +
            "}\r\n" +
            "int\r\n" +
            "old_style_fn(x)\r\n" +
            "{\r\n" +
            "    return x;\r\n" +
            "}\r\n" +
            "helper_fn(x);\r\n" +
            "    return helper_fn(x);\r\n");

        // An anonymous struct typedef block, closed by "} gbuf_entry;".
        File.WriteAllText(Path.Combine(_sourceDir, "include", "gbuf.h"),
            "/* gbuf.h */\r\n" +
            "typedef struct {\r\n" +
            "    int x;\r\n" +
            "    int y;\r\n" +
            "} gbuf_entry;\r\n");
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

    private SourceCodeService CreateService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("SourceCodePath", _sourceDir),
                new KeyValuePair<string, string?>("MaxSourceFileSizeKB", "800")
            })
            .Build();

        var service = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
        service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return service;
    }

    [Fact]
    public void FindDefinition_OneLineFunctionDefinition_Matches()
    {
        using var service = CreateService();

        string result = service.FindDefinition("lib_print_glyph", "function");

        Assert.StartsWith("--- src/funcs.c:L6 ---", result);
        Assert.Contains(">>> 6: void lib_print_glyph(winid wid, int x)", result);
    }

    [Fact]
    public void GetFunctionBody_OneLineFunctionDefinition_ReturnsTheBracedBody()
    {
        using var service = CreateService();

        string result = service.GetFunctionBody("lib_print_glyph", "function");

        Assert.Contains("void lib_print_glyph(winid wid, int x)", result);
        Assert.Contains("    print(wid, x);", result);
        Assert.Contains("}", result);
    }

    [Fact]
    public void FindDefinition_NetHackTwoLineStyle_StillMatches()
    {
        using var service = CreateService();

        string result = service.FindDefinition("old_style_fn", "function");

        Assert.StartsWith("--- src/funcs.c:L11 ---", result);
        Assert.Contains(">>> 11: old_style_fn(x)", result);
    }

    [Fact]
    public void GetFunctionBody_NetHackTwoLineStyle_StillMatches()
    {
        using var service = CreateService();

        string result = service.GetFunctionBody("old_style_fn", "function");

        Assert.Contains("old_style_fn(x)", result);
        Assert.Contains("    return x;", result);
    }

    [Fact]
    public void FindDefinition_ExternPrototype_DoesNotMatch()
    {
        using var service = CreateService();

        string result = service.FindDefinition("extern_fn", "any");

        Assert.Equal("No definition found for 'extern_fn' of kind 'any'.", result);
    }

    [Fact]
    public void GetFunctionBody_ExternPrototype_DoesNotMatch()
    {
        using var service = CreateService();

        string result = service.GetFunctionBody("extern_fn", "any");

        Assert.Equal("No definition found for 'extern_fn' of kind 'any'.", result);
    }

    [Fact]
    public void FindDefinition_FdeclPrototype_DoesNotMatch()
    {
        using var service = CreateService();

        string result = service.FindDefinition("fdecl_fn", "any");

        Assert.Equal("No definition found for 'fdecl_fn' of kind 'any'.", result);
    }

    [Fact]
    public void FindDefinition_StaticOvlPrototype_DoesNotMatch()
    {
        using var service = CreateService();

        string result = service.FindDefinition("ovl_fn", "any");

        Assert.Equal("No definition found for 'ovl_fn' of kind 'any'.", result);
    }

    [Fact]
    public void FindDefinition_CallStatementAtLineStartAndIndentedReturnCall_DoesNotMatch()
    {
        using var service = CreateService();

        // "helper_fn(x);" at column 0 and "    return helper_fn(x);" are both call sites, not
        // definitions - neither the unindented call nor the indented "return" form must match.
        string result = service.FindDefinition("helper_fn", "any");

        Assert.Equal("No definition found for 'helper_fn' of kind 'any'.", result);
    }

    [Fact]
    public void FindDefinition_TypedefBlockClose_MatchesUnderTypeKind()
    {
        using var service = CreateService();

        string result = service.FindDefinition("gbuf_entry", "type");

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- include/gbuf.h:L5 ---",
            "    4:     int y;",
            ">>> 5: } gbuf_entry;"
        });

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindDefinition_TypedefBlockClose_MatchesUnderAnyKindWithTheWiderLookback()
    {
        using var service = CreateService();

        string result = service.FindDefinition("gbuf_entry", "any");

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- include/gbuf.h:L5 ---",
            "    3:     int x;",
            "    4:     int y;",
            ">>> 5: } gbuf_entry;"
        });

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// search_definitions only widens the typedef-close alternative to kinds "type" and "any" - a
    /// plain "struct" search still relies on the named-struct opener alternative alone, so an
    /// anonymous typedef'd struct is not reachable under kind "struct" through this method.
    /// </summary>
    [Fact]
    public void FindDefinition_TypedefBlockClose_DoesNotMatchUnderStructKind()
    {
        using var service = CreateService();

        string result = service.FindDefinition("gbuf_entry", "struct");

        Assert.Equal("No definition found for 'gbuf_entry' of kind 'struct'.", result);
    }

    /// <summary>
    /// get_function_definition exposes no "type" kind (its schema enum is function/macro/struct/any),
    /// so the body locator surfaces the anonymous typedef'd struct under "struct" instead, walking
    /// back to the "typedef struct {" opener to return the whole block.
    /// </summary>
    [Fact]
    public void GetFunctionBody_TypedefBlockClose_StructKindReturnsTheWholeBlock()
    {
        using var service = CreateService();

        string result = service.GetFunctionBody("gbuf_entry", "struct");

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- include/gbuf.h:L2-L5 (gbuf_entry, 4 lines) ---",
            "typedef struct {",
            "    int x;",
            "    int y;",
            "} gbuf_entry;"
        });

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetFunctionBody_TypedefBlockClose_AnyKindMatchesTheStructKindResult()
    {
        using var service = CreateService();

        string structResult = service.GetFunctionBody("gbuf_entry", "struct");
        string anyResult = service.GetFunctionBody("gbuf_entry", "any");

        Assert.Equal(structResult, anyResult);
    }

    /// <summary>
    /// The service layer also recognizes "type" for the body locator, even though
    /// get_function_definition's own schema never offers that kind value to a caller.
    /// </summary>
    [Fact]
    public void GetFunctionBody_TypedefBlockClose_TypeKindMatchesTheStructKindResult()
    {
        using var service = CreateService();

        string structResult = service.GetFunctionBody("gbuf_entry", "struct");
        string typeResult = service.GetFunctionBody("gbuf_entry", "type");

        Assert.Equal(structResult, typeResult);
    }
}

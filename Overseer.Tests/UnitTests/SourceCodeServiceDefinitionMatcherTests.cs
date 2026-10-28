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
/// widen into — extern/FDECL/OVL prototypes and bare call statements. Also covers an <c>enum</c> or
/// <c>struct</c> head whose <c>{</c> is on the next line, and the look-alikes that must not match it.
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

        // An enum and a struct whose "{" is on the line after the name (the include/layer.h style,
        // one with a trailing comment), and a struct-typed member list whose next line is not a brace.
        File.WriteAllText(Path.Combine(_sourceDir, "include", "layer.h"),
            "/* layer.h */\r\n" +
            "#include \"config.h\"\r\n" +
            "/* Layer types */\r\n" +
            "enum layer_types\r\n" +
            "{\r\n" +
            "    LAYER_FLOOR = 0,\r\n" +
            "    LAYER_CARPET,\r\n" +
            "    MAX_LAYERS\r\n" +
            "};\r\n" +
            "/* Layer info */\r\n" +
            "struct layer_info /* per-position layers */\r\n" +
            "{\r\n" +
            "    int glyph;\r\n" +
            "    int layer_flags;\r\n" +
            "};\r\n" +
            "struct member_holder {\r\n" +
            "    struct member_struct\r\n" +
            "        first_field,\r\n" +
            "        second_field;\r\n" +
            "};\r\n");

        // A function taking an enum parameter, its body brace on the next line, and a struct
        // forward declaration followed by a block statement — neither is an enum or struct definition.
        File.WriteAllText(Path.Combine(_sourceDir, "src", "blocks.c"),
            "/* blocks.c */\r\n" +
            "static void takes_param_enum(enum param_enum e)\r\n" +
            "{\r\n" +
            "    (void) e;\r\n" +
            "}\r\n" +
            "void fwd_user(void)\r\n" +
            "{\r\n" +
            "    struct proto_only;\r\n" +
            "    {\r\n" +
            "        int scoped = 0;\r\n" +
            "    }\r\n" +
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
        service.InitializationTask.GetAwaiter().GetResult();
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

    [Theory]
    [InlineData(nameof(SourceCodeService.FindDefinition), "extern_fn")]
    [InlineData(nameof(SourceCodeService.GetFunctionBody), "extern_fn")]
    [InlineData(nameof(SourceCodeService.FindDefinition), "fdecl_fn")]
    [InlineData(nameof(SourceCodeService.FindDefinition), "ovl_fn")]
    // "helper_fn(x);" at column 0 and "    return helper_fn(x);" are both call sites, not
    // definitions - neither the unindented call nor the indented "return" form must match.
    [InlineData(nameof(SourceCodeService.FindDefinition), "helper_fn")]
    public void PrototypeOrCallStatement_IsNotADefinition(string method, string name)
    {
        using var service = CreateService();

        string result = method == nameof(SourceCodeService.GetFunctionBody)
            ? service.GetFunctionBody(name, "any")
            : service.FindDefinition(name, "any");

        Assert.Equal($"No definition found for '{name}' of kind 'any'.", result);
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

    [Fact]
    public void FindDefinition_EnumWithBraceOnTheNextLine_MatchesUnderEnumKind()
    {
        using var service = CreateService();

        string result = service.FindDefinition("layer_types", "enum");

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- include/layer.h:L4 ---",
            "    3: /* Layer types */",
            ">>> 4: enum layer_types",
            "    5: {",
            "    6:     LAYER_FLOOR = 0,",
            "    7:     LAYER_CARPET,",
            "    8:     MAX_LAYERS",
            "    9: };"
        });

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindDefinition_EnumWithBraceOnTheNextLine_MatchesUnderAnyKind()
    {
        using var service = CreateService();

        string result = service.FindDefinition("layer_types", "any");

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- include/layer.h:L4 ---",
            "    2: #include \"config.h\"",
            "    3: /* Layer types */",
            ">>> 4: enum layer_types",
            "    5: {",
            "    6:     LAYER_FLOOR = 0,",
            "    7:     LAYER_CARPET,",
            "    8:     MAX_LAYERS",
            "    9: };"
        });

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// The head line is the match and the output runs from it through the whole braced body,
    /// whereas a same-line "enum name {" starts its output at the brace.
    /// </summary>
    [Fact]
    public void GetFunctionBody_EnumWithBraceOnTheNextLine_ReturnsTheHeadAndTheWholeBody()
    {
        using var service = CreateService();

        string result = service.GetFunctionBody("layer_types", "enum");

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- include/layer.h:L3-L9 (layer_types, 7 lines) ---",
            "/* Layer types */",
            "enum layer_types",
            "{",
            "    LAYER_FLOOR = 0,",
            "    LAYER_CARPET,",
            "    MAX_LAYERS",
            "}"
        });

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetFunctionBody_EnumWithBraceOnTheNextLine_AnyKindReturnsTheWholeBody()
    {
        using var service = CreateService();

        string result = service.GetFunctionBody("layer_types", "any");

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- include/layer.h:L2-L9 (layer_types, 8 lines) ---",
            "#include \"config.h\"",
            "/* Layer types */",
            "enum layer_types",
            "{",
            "    LAYER_FLOOR = 0,",
            "    LAYER_CARPET,",
            "    MAX_LAYERS",
            "}"
        });

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindDefinition_StructWithBraceOnTheNextLine_MatchesUnderStructKind()
    {
        using var service = CreateService();

        string result = service.FindDefinition("layer_info", "struct");

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- include/layer.h:L11 ---",
            "    10: /* Layer info */",
            ">>> 11: struct layer_info /* per-position layers */",
            "    12: {",
            "    13:     int glyph;",
            "    14:     int layer_flags;",
            "    15: };",
            "    16: struct member_holder {"
        });

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetFunctionBody_StructWithBraceOnTheNextLine_ReturnsTheHeadAndTheWholeBody()
    {
        using var service = CreateService();

        string result = service.GetFunctionBody("layer_info", "struct");

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- include/layer.h:L10-L15 (layer_info, 6 lines) ---",
            "/* Layer info */",
            "struct layer_info /* per-position layers */",
            "{",
            "    int glyph;",
            "    int layer_flags;",
            "}"
        });

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// A line naming the symbol followed by a brace is not enough: a forward declaration
    /// ("struct proto_only;") followed by a block statement, an enum used as a parameter type with
    /// the function body's brace on the next line, and a struct-typed member list whose next line is
    /// a field name are not definitions.
    /// </summary>
    [Theory]
    [InlineData(nameof(SourceCodeService.FindDefinition), "proto_only", "struct")]
    [InlineData(nameof(SourceCodeService.FindDefinition), "proto_only", "any")]
    [InlineData(nameof(SourceCodeService.GetFunctionBody), "proto_only", "struct")]
    [InlineData(nameof(SourceCodeService.GetFunctionBody), "proto_only", "any")]
    [InlineData(nameof(SourceCodeService.FindDefinition), "param_enum", "enum")]
    [InlineData(nameof(SourceCodeService.FindDefinition), "param_enum", "any")]
    [InlineData(nameof(SourceCodeService.GetFunctionBody), "param_enum", "enum")]
    [InlineData(nameof(SourceCodeService.GetFunctionBody), "param_enum", "any")]
    [InlineData(nameof(SourceCodeService.FindDefinition), "member_struct", "struct")]
    [InlineData(nameof(SourceCodeService.GetFunctionBody), "member_struct", "any")]
    public void HeadLineWithoutANextLineBrace_IsNotADefinition(string method, string name, string kind)
    {
        using var service = CreateService();

        string result = method == nameof(SourceCodeService.GetFunctionBody)
            ? service.GetFunctionBody(name, kind)
            : service.FindDefinition(name, kind);

        Assert.Equal($"No definition found for '{name}' of kind '{kind}'.", result);
    }
}

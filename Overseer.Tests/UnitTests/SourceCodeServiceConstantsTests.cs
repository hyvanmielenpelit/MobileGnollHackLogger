using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Covers the enum constants <see cref="SourceCodeService"/> indexes for get_constants: an enum whose
/// opening brace is on the line after its name, the one-line <c>enum name {</c> form, and the lines
/// mentioning <c>enum</c> that must not open one — a line followed by something other than a brace,
/// a preprocessor line, and a function signature taking an enum parameter.
/// </summary>
public class SourceCodeServiceConstantsTests : IDisposable
{
    private readonly string _sourceDir;

    public SourceCodeServiceConstantsTests()
    {
        _sourceDir = Path.Combine(Path.GetTempPath(), "SourceCodeServiceConstantsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "include"));

        File.WriteAllText(Path.Combine(_sourceDir, "include", "brace_next.h"),
            "/* brace_next.h */\r\n" +
            "enum obj_class_types\r\n" +
            "{\r\n" +
            "    ILLOBJ_CLASS = 0,\r\n" +
            "    WEAPON_CLASS,\r\n" +
            "    ARMOR_CLASS,\r\n" +
            "    MAX_OBJECT_CLASSES\r\n" +
            "};\r\n" +
            "\r\n" +
            "enum spaced_types\r\n" +
            "\r\n" +
            "{ SPACED_FIRST = 7, SPACED_SECOND,\r\n" +
            "    SPACED_THIRD,\r\n" +
            "};\r\n");

        File.WriteAllText(Path.Combine(_sourceDir, "include", "one_line.h"),
            "/* one_line.h */\r\n" +
            "enum one_line_types {\r\n" +
            "    ONE_LINE_A = 1,\r\n" +
            "    ONE_LINE_B,\r\n" +
            "};\r\n" +
            "enum inline_types { INLINE_A = 3, INLINE_B, INLINE_C };\r\n");

        File.WriteAllText(Path.Combine(_sourceDir, "include", "not_an_enum.h"),
            "/* not_an_enum.h */\r\n" +
            "/* the enum values below come from objclass.h */\r\n" +
            "static int stray_counter\r\n" +
            "{\r\n" +
            "    COMMENT_STRAY_MEMBER,\r\n" +
            "}\r\n");

        File.WriteAllText(Path.Combine(_sourceDir, "include", "preproc.h"),
            "/* preproc.h */\r\n" +
            "#if 0 /* enum table */\r\n" +
            "{\r\n" +
            "    PREPROC_STRAY_MEMBER,\r\n" +
            "}\r\n" +
            "#endif\r\n");

        File.WriteAllText(Path.Combine(_sourceDir, "include", "signature.h"),
            "/* signature.h */\r\n" +
            "boolean is_class(enum obj_class_types oclass)\r\n" +
            "{\r\n" +
            "    SIGNATURE_STRAY_LOCAL,\r\n" +
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

    private async Task<SourceCodeService> CreateServiceAsync()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("SourceCodePath", _sourceDir),
                new KeyValuePair<string, string?>("MaxSourceFileSizeKB", "800")
            })
            .Build();

        var service = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
        await service.StartAsync(TestContext.Current.CancellationToken);
        return service;
    }

    private static SourceCodeService.ConstantInfo? Find(SourceCodeService service, string name)
        => service.GetConstants(name, null).SingleOrDefault();

    /// <summary>An enum whose brace opens on the line after its name has its members indexed, with their values and lines.</summary>
    [Fact]
    public async Task BraceOnNextLine_MembersAreIndexed()
    {
        using var service = await CreateServiceAsync();

        var illobj = Find(service, "ILLOBJ_CLASS");
        Assert.NotNull(illobj);
        Assert.Equal("0", illobj.Value);
        Assert.Equal("include/brace_next.h", illobj.FilePath);
        Assert.Equal(4, illobj.LineNumber);

        var weapon = Find(service, "WEAPON_CLASS");
        Assert.NotNull(weapon);
        Assert.Equal("", weapon.Value);
        Assert.Equal(5, weapon.LineNumber);

        Assert.NotNull(Find(service, "ARMOR_CLASS"));
    }

    /// <summary>Blank lines between the enum name and its brace still open it, and members on the brace line are indexed.</summary>
    [Fact]
    public async Task BraceAfterBlankLine_MembersOnTheBraceLineAreIndexed()
    {
        using var service = await CreateServiceAsync();

        var first = Find(service, "SPACED_FIRST");
        Assert.NotNull(first);
        Assert.Equal("7", first.Value);
        Assert.Equal(12, first.LineNumber);

        Assert.NotNull(Find(service, "SPACED_SECOND"));
        Assert.NotNull(Find(service, "SPACED_THIRD"));
    }

    /// <summary>The one-line <c>enum name {</c> form, multi-line and fully inline, is still indexed.</summary>
    [Fact]
    public async Task OneLineForm_StillIndexed()
    {
        using var service = await CreateServiceAsync();

        var a = Find(service, "ONE_LINE_A");
        Assert.NotNull(a);
        Assert.Equal("1", a.Value);
        Assert.NotNull(Find(service, "ONE_LINE_B"));

        var inlineA = Find(service, "INLINE_A");
        Assert.NotNull(inlineA);
        Assert.Equal("3", inlineA.Value);
        Assert.NotNull(Find(service, "INLINE_B"));
        Assert.NotNull(Find(service, "INLINE_C"));
    }

    /// <summary>A line mentioning <c>enum</c> followed by a line other than a brace does not open an enum.</summary>
    [Fact]
    public async Task EnumWordFollowedByNonBraceLine_DoesNotOpenAnEnum()
    {
        using var service = await CreateServiceAsync();

        Assert.Null(Find(service, "COMMENT_STRAY_MEMBER"));
    }

    /// <summary>A preprocessor line containing the word <c>enum</c> does not open an enum on the following brace.</summary>
    [Fact]
    public async Task PreprocessorLineWithEnumWord_DoesNotOpenAnEnum()
    {
        using var service = await CreateServiceAsync();

        Assert.Null(Find(service, "PREPROC_STRAY_MEMBER"));
    }

    /// <summary>A function signature taking an enum parameter does not open an enum on the body's brace.</summary>
    [Fact]
    public async Task FunctionSignatureWithEnumParameter_DoesNotOpenAnEnum()
    {
        using var service = await CreateServiceAsync();

        Assert.Null(Find(service, "SIGNATURE_STRAY_LOCAL"));
    }
}

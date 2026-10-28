using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Overseer.Models;
using Overseer.Services.Feedback;
using Xunit;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The example files of the formats dialog: the issues examples parse and carry the documented keys, and the
/// resolutions examples, read the way the import reads a file, pass the import's validation.
/// </summary>
public sealed class FeedbackFormatExamplesTests
{
    private static YamlMappingNode Root(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        Assert.Single(stream.Documents);
        return Assert.IsType<YamlMappingNode>(stream.Documents[0].RootNode);
    }

    private static string[] Keys(YamlMappingNode node) => node.Children.Keys.Select(k => ((YamlScalarNode)k).Value!).ToArray();

    /// <summary>A YAML node as JSON: plain <c>null</c> is null, plain integers and booleans keep their type.</summary>
    private static JsonNode? ToJson(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode map:
                var obj = new JsonObject();
                foreach (var (key, value) in map.Children) obj[((YamlScalarNode)key).Value!] = ToJson(value);
                return obj;
            case YamlSequenceNode sequence:
                var array = new JsonArray();
                foreach (var child in sequence.Children) array.Add(ToJson(child));
                return array;
            default:
                var scalar = (YamlScalarNode)node;
                if (scalar.Style == ScalarStyle.Plain)
                {
                    if (scalar.Value is "null" or "~") return null;
                    if (scalar.Value is "true" or "false") return JsonValue.Create(scalar.Value == "true");
                    if (long.TryParse(scalar.Value, out long number)) return JsonValue.Create(number);
                }
                return JsonValue.Create(scalar.Value);
        }
    }

    /// <summary>A resolution file as the import's request: the YAML's <c>issues</c> become <c>items</c>.</summary>
    private static AdminFeedbackResolutionRequestDto ToRequest(string yaml)
    {
        var json = (JsonObject)ToJson(Root(yaml))!;
        var issues = json["issues"];
        json.Remove("issues");
        json["items"] = issues;
        return json.Deserialize<AdminFeedbackResolutionRequestDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    [Theory]
    [InlineData(FeedbackTransferSources.Overseer)]
    [InlineData(FeedbackTransferSources.GnollBench)]
    public void IssuesExample_ParsesWithTheDocumentedKeys(string source)
    {
        var examples = FeedbackFormatExamples.For(source);

        var root = Root(examples.Issues.Content);
        var expected = source == FeedbackTransferSources.GnollBench
            ? new[] { "format", "version", "source", "exportedAtUtc", "overseerVersion", "occurrencesPerIssue", "includeQuestionText", "notice", "issues" }
            : new[] { "format", "version", "source", "exportedAtUtc", "overseerVersion", "occurrencesPerIssue", "notice", "issues" };
        Assert.Equal(expected, Keys(root));
        Assert.Equal(FeedbackYamlExport.FormatName, ((YamlScalarNode)root.Children[new YamlScalarNode("format")]).Value);
        var issue = Assert.IsType<YamlMappingNode>(Assert.Single(((YamlSequenceNode)root.Children[new YamlScalarNode("issues")]).Children));
        Assert.Equal(2, ((YamlSequenceNode)issue.Children[new YamlScalarNode("occurrences")]).Children.Count);
        Assert.Equal(FeedbackYamlExport.MediaType, examples.Issues.MediaType);
        Assert.EndsWith(".yaml", examples.Issues.FileName);
        Assert.StartsWith("example-", examples.Issues.FileName);
    }

    [Theory]
    [InlineData(FeedbackTransferSources.Overseer)]
    [InlineData(FeedbackTransferSources.GnollBench)]
    public void IssuesExample_LabelsEveryTextAsAnExample(string source)
    {
        var document = FeedbackFormatExamples.Issues(source);
        var issue = Assert.Single(document.Issues);

        Assert.StartsWith("Example:", issue.Subject);
        Assert.StartsWith("Example:", issue.Summary);
        Assert.StartsWith("Example:", issue.TriageNote);
        foreach (var o in issue.Occurrences)
        {
            foreach (var text in new[] { o.Subject, o.Summary, o.Details, o.SearchedFor, o.ExpectedLocation, o.SuspectedCauseNote, o.QuestionText })
                if (text != null) Assert.StartsWith("Example:", text);
        }
    }

    [Theory]
    [InlineData(FeedbackTransferSources.Overseer)]
    [InlineData(FeedbackTransferSources.GnollBench)]
    public void ResolutionsExample_PassesTheImportsValidation_AtVersion2(string source)
    {
        var examples = FeedbackFormatExamples.For(source);
        var request = ToRequest(examples.Resolutions.Content);

        Assert.True(FeedbackResolutionService.TryValidate(request, forApply: false, out var file, out var errors),
            string.Join("; ", errors.Select(e => e.Message)));
        Assert.Equal(2, request.Version);
        Assert.Equal(source, file.Source);
        Assert.Equal(2, file.Items.Count);
        var resolved = file.Items[0];
        Assert.Equal("resolved", resolved.Status);
        Assert.True(resolved.HasResolvedVia);
        Assert.Equal(new[] { FeedbackResolvedVia.Wiki }, resolved.ResolvedVia);
        Assert.Contains("\n\nResolved via: GnollHack wiki\n", resolved.Resolution);
        Assert.Equal("acknowledged", file.Items[1].Status);
        Assert.True(file.Items[1].HasNote);
        Assert.Equal(FeedbackYamlExport.MediaType, examples.Resolutions.MediaType);
        Assert.DoesNotContain("\r", examples.Resolutions.Content);
    }
}

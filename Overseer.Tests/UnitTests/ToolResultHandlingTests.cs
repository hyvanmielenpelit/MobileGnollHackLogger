using System.Text.Json;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ToolResultHandlingTests
{
    private static string ExtractResultContent(ToolResult res)
    {
        return res.Success
            ? (!string.IsNullOrEmpty(res.Content) ? res.Content : "Success")
            : (!string.IsNullOrWhiteSpace(res.ErrorMessage) ? res.ErrorMessage : (!string.IsNullOrWhiteSpace(res.Content) ? res.Content : "Unknown error"));
    }

    [Fact]
    public void ToolResult_DefaultProperties_InitializedCorrectly()
    {
        var result = new ToolResult();
        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Content);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ExtractResultContent_WhenSuccessWithContent_ReturnsContent()
    {
        var result = new ToolResult { Success = true, Content = "Found 5 items" };
        var extracted = ExtractResultContent(result);
        Assert.Equal("Found 5 items", extracted);
    }

    [Fact]
    public void ExtractResultContent_WhenSuccessWithEmptyContent_ReturnsSuccess()
    {
        var result = new ToolResult { Success = true, Content = "" };
        var extracted = ExtractResultContent(result);
        Assert.Equal("Success", extracted);
    }

    [Fact]
    public void ExtractResultContent_WhenFailedWithErrorMessage_ReturnsErrorMessage()
    {
        var result = new ToolResult
        {
            Success = false,
            Content = "", // As defaulted in ToolResult
            ErrorMessage = "NetHack Wiki is temporarily unavailable (HTTP 503 Service Unavailable)."
        };

        var extracted = ExtractResultContent(result);
        Assert.Equal("NetHack Wiki is temporarily unavailable (HTTP 503 Service Unavailable).", extracted);
    }

    [Fact]
    public void ExtractResultContent_WhenFailedWithContentAndNoErrorMessage_ReturnsContent()
    {
        var result = new ToolResult
        {
            Success = false,
            Content = "Execution failed on step 2",
            ErrorMessage = null
        };

        var extracted = ExtractResultContent(result);
        Assert.Equal("Execution failed on step 2", extracted);
    }

    [Fact]
    public void ExtractResultContent_WhenFailedWithEmptyContentAndNoErrorMessage_ReturnsUnknownError()
    {
        var result = new ToolResult
        {
            Success = false,
            Content = "",
            ErrorMessage = null
        };

        var extracted = ExtractResultContent(result);
        Assert.Equal("Unknown error", extracted);
    }

    [Theory]
    [InlineData(ToolGuardMessages.WikiIndexingInProgress)]
    [InlineData(ToolGuardMessages.NetHackWikiIndexingInProgress)]
    [InlineData(ToolGuardMessages.KnowledgeBaseIndexingInProgress)]
    [InlineData(ToolGuardMessages.SourceCodeIndexingInProgress)]
    [InlineData(ToolGuardMessages.NetHackSourceCodeIndexingInProgress)]
    public void ToolGuardMessages_AreDirectiveAndInstructive(string message)
    {
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.Contains("initialization in progress", message);
        Assert.Contains("Do not retry this tool in this turn", message);
        Assert.Contains("warming up", message);
    }

    [Fact]
    public void ExtractResultContent_WhenUnindexedToolError_ExtractsDirectiveMessage()
    {
        var result = new ToolResult
        {
            Success = false,
            ErrorMessage = ToolGuardMessages.WikiIndexingInProgress
        };

        var extracted = ExtractResultContent(result);
        Assert.Equal(ToolGuardMessages.WikiIndexingInProgress, extracted);
    }
}


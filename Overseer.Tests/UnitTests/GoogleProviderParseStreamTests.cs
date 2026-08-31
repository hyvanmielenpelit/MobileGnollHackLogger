using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Overseer.Services;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class GoogleProviderParseStreamTests
{
    private static GoogleProvider CreateProvider()
    {
        var config = new ConfigurationBuilder().Build();
        return new GoogleProvider(config);
    }

    private static HttpResponseMessage CreateSseResponse(string sseContent)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sseContent, Encoding.UTF8, "text/event-stream")
        };
    }

    [Fact]
    public async Task ParseStreamAsync_WithStopFinishReason_EmitsFinishReasonDebugEvent()
    {
        var provider = CreateProvider();
        var sse = "data: {\"candidates\": [{\"finishReason\": \"STOP\", \"content\": {\"parts\": [{\"text\": \"Hello world\"}]}}]}\n\n";
        using var response = CreateSseResponse(sse);

        var events = new List<ChatEvent>();
        await foreach (var evt in provider.ParseStreamAsync(response, showDebugLog: true, CancellationToken.None))
        {
            events.Add(evt);
        }

        var debugEvents = events.Where(e => e.Type == "debug").Select(e => e.Data ?? "").ToList();
        Assert.Contains(debugEvents, d => d == "[Main Chat - Google] candidate finishReason=STOP");

        var chunkEvents = events.Where(e => e.Type == "chunk").Select(e => e.Data ?? "").ToList();
        Assert.Contains(chunkEvents, c => c == "Hello world");
    }

    [Fact]
    public async Task ParseStreamAsync_WithMaxTokensFinishReason_EmitsBothDebugEvents()
    {
        var provider = CreateProvider();
        var sse = "data: {\"candidates\": [{\"finishReason\": \"MAX_TOKENS\", \"content\": {\"parts\": [{\"text\": \"Cut off\"}]}}]}\n\n";
        using var response = CreateSseResponse(sse);

        var events = new List<ChatEvent>();
        await foreach (var evt in provider.ParseStreamAsync(response, showDebugLog: true, CancellationToken.None))
        {
            events.Add(evt);
        }

        var debugEvents = events.Where(e => e.Type == "debug").Select(e => e.Data ?? "").ToList();
        Assert.Contains(debugEvents, d => d == "[Google] Response incomplete: finishReason=MAX_TOKENS");
        Assert.Contains(debugEvents, d => d == "[Main Chat - Google] candidate finishReason=MAX_TOKENS");
    }

    [Fact]
    public async Task ParseStreamAsync_WithMaxTokensAndDebugDisabled_EmitsOnlyUnconditionalEvent()
    {
        var provider = CreateProvider();
        var sse = "data: {\"candidates\": [{\"finishReason\": \"MAX_TOKENS\", \"content\": {\"parts\": [{\"text\": \"Cut off\"}]}}]}\n\n";
        using var response = CreateSseResponse(sse);

        var events = new List<ChatEvent>();
        await foreach (var evt in provider.ParseStreamAsync(response, showDebugLog: false, CancellationToken.None))
        {
            events.Add(evt);
        }

        var debugEvents = events.Where(e => e.Type == "debug").Select(e => e.Data ?? "").ToList();
        Assert.Contains(debugEvents, d => d == "[Google] Response incomplete: finishReason=MAX_TOKENS");
        Assert.DoesNotContain(debugEvents, d => d == "[Main Chat - Google] candidate finishReason=MAX_TOKENS");
    }

    [Fact]
    public async Task ParseStreamAsync_WithUsageMetadata_EmitsUsageDebugEvent()
    {
        var provider = CreateProvider();
        var sse = "data: {\"candidates\": [{\"finishReason\": \"STOP\", \"content\": {\"parts\": [{\"text\": \"Done\"}]}}], \"usageMetadata\": {\"promptTokenCount\": 120, \"candidatesTokenCount\": 45, \"totalTokenCount\": 165, \"thoughtsTokenCount\": 30, \"cachedContentTokenCount\": 10}}\n\n";
        using var response = CreateSseResponse(sse);

        var events = new List<ChatEvent>();
        await foreach (var evt in provider.ParseStreamAsync(response, showDebugLog: true, CancellationToken.None))
        {
            events.Add(evt);
        }

        var debugEvents = events.Where(e => e.Type == "debug").Select(e => e.Data ?? "").ToList();
        Assert.Contains(debugEvents, d => d == "[Main Chat - Google] usage: prompt_tokens=120, output_tokens=45, total_tokens=165, thought_tokens=30, cached_tokens=10");
    }

    [Fact]
    public async Task ParseStreamAsync_WithChunksAndThoughts_EmitsContentAndProviderHistory()
    {
        var provider = CreateProvider();
        var sse = new StringBuilder()
            .AppendLine("data: {\"candidates\": [{\"content\": {\"parts\": [{\"thought\": true, \"thoughtSignature\": \"sig_abc123\", \"text\": \"Analyzing request...\"}]}}]}")
            .AppendLine()
            .AppendLine("data: {\"candidates\": [{\"finishReason\": \"STOP\", \"content\": {\"parts\": [{\"text\": \"Final answer\"}]}}], \"usageMetadata\": {\"promptTokenCount\": 50, \"candidatesTokenCount\": 20, \"totalTokenCount\": 70}}")
            .AppendLine()
            .ToString();

        using var response = CreateSseResponse(sse);

        var events = new List<ChatEvent>();
        await foreach (var evt in provider.ParseStreamAsync(response, showDebugLog: true, CancellationToken.None))
        {
            events.Add(evt);
        }

        var thinkingEvents = events.Where(e => e.Type == "thinking_chunk").Select(e => e.Data ?? "").ToList();
        Assert.Contains(thinkingEvents, t => t == "Analyzing request...");

        var chunkEvents = events.Where(e => e.Type == "chunk").Select(e => e.Data ?? "").ToList();
        Assert.Contains(chunkEvents, c => c == "Final answer");

        var providerItems = events.Where(e => e.Type == "provider_history_item").ToList();
        Assert.NotEmpty(providerItems);

        var debugEvents = events.Where(e => e.Type == "debug").Select(e => e.Data ?? "").ToList();
        Assert.Contains(debugEvents, d => d == "[Main Chat - Google] usage: prompt_tokens=50, output_tokens=20, total_tokens=70");
    }

    [Fact]
    public async Task ParseStreamAsync_WithServiceTierInUsageMetadata_EmitsServiceTierEvent()
    {
        var provider = CreateProvider();
        var sse = "data: {\"usageMetadata\": {\"promptTokenCount\": 8, \"candidatesTokenCount\": 1, \"serviceTier\": \"priority\"}}\n\n";
        using var response = CreateSseResponse(sse);

        var events = new List<ChatEvent>();
        await foreach (var evt in provider.ParseStreamAsync(response, showDebugLog: true, CancellationToken.None))
        {
            events.Add(evt);
        }

        var tierEvents = events.Where(e => e.Type == "service_tier").ToList();
        Assert.Single(tierEvents);
        Assert.Equal("priority", tierEvents[0].Data);

        var tierIndex = events.FindIndex(e => e.Type == "service_tier");
        var usageIndex = events.FindIndex(e => e.Type == "usage");
        Assert.True(tierIndex >= 0, "service_tier event not found");
        Assert.True(usageIndex >= 0, "usage event not found");
        Assert.True(tierIndex < usageIndex, "service_tier event was not emitted before usage event");
    }

    [Fact]
    public async Task ParseStreamAsync_WithServiceTierInEveryChunk_EmitsEventPerChunk()
    {
        var provider = CreateProvider();
        var sse = new StringBuilder()
            .AppendLine("data: {\"candidates\": [{\"content\": {\"parts\": [{\"text\": \"Chunk 1\"}]}}], \"usageMetadata\": {\"promptTokenCount\": 8, \"candidatesTokenCount\": 1, \"serviceTier\": \"priority\"}}")
            .AppendLine()
            .AppendLine("data: {\"candidates\": [{\"finishReason\": \"STOP\", \"content\": {\"parts\": [{\"text\": \"Chunk 2\"}]}}], \"usageMetadata\": {\"promptTokenCount\": 8, \"candidatesTokenCount\": 2, \"serviceTier\": \"priority\"}}")
            .AppendLine()
            .ToString();

        using var response = CreateSseResponse(sse);

        var events = new List<ChatEvent>();
        await foreach (var evt in provider.ParseStreamAsync(response, showDebugLog: true, CancellationToken.None))
        {
            events.Add(evt);
        }

        var tierEvents = events.Where(e => e.Type == "service_tier").ToList();
        Assert.Equal(2, tierEvents.Count);
        Assert.All(tierEvents, t => Assert.Equal("priority", t.Data));
    }

    [Fact]
    public async Task ParseStreamAsync_WithoutServiceTier_EmitsNoServiceTierEvent()
    {
        var provider = CreateProvider();
        var sse = "data: {\"candidates\": [{\"finishReason\": \"STOP\", \"content\": {\"parts\": [{\"text\": \"Done\"}]}}], \"usageMetadata\": {\"promptTokenCount\": 120, \"candidatesTokenCount\": 45, \"totalTokenCount\": 165}}\n\n";
        using var response = CreateSseResponse(sse);

        var events = new List<ChatEvent>();
        await foreach (var evt in provider.ParseStreamAsync(response, showDebugLog: true, CancellationToken.None))
        {
            events.Add(evt);
        }

        var tierEvents = events.Where(e => e.Type == "service_tier").ToList();
        Assert.Empty(tierEvents);
    }
}

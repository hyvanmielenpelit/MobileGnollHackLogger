using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Overseer.Services;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

// ====================================================================================
// IMPORTANT: This test file connects to the live Google Gemini API and consumes quota
// on a paid Tier 2 key.
//
// To run the test suite while SKIPPING this file (to save AI API quota and money), use:
// dotnet test MobileGnollHackLogger.slnx --filter "Category!=UsesExternalApi"
//
// Expected behaviour of the live API is documented in
// docs/overseer/gemini-service-tier-measurements.md — read it before debugging a failure.
// ====================================================================================

namespace Overseer.Tests
{
    [Trait("Category", "UsesExternalApi")]
    public class ServiceTierLiveApiTests
    {
        private readonly ITestOutputHelper _output;

        public ServiceTierLiveApiTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private (IConfiguration config, GoogleProvider provider, string apiKey, string model) Setup()
        {
            var config = new ConfigurationBuilder()
                .AddUserSecrets<ServiceTierLiveApiTests>()
                .Build();

            var apiKey = config["AI:ServiceTier:APIKey"] ?? "";
            var model = config["AI:ServiceTier:Model"] ?? "";
            Assert.False(string.IsNullOrEmpty(apiKey), "AI:ServiceTier:APIKey is not configured in User Secrets.");
            Assert.False(string.IsNullOrEmpty(model), "AI:ServiceTier:Model is not configured in User Secrets.");

            var provider = new GoogleProvider(config);
            return (config, provider, apiKey, model);
        }

        [Fact]
        public async Task Google_NonStreaming_ReportsServiceTier_InHeaderAndBody()
        {
            var (_, provider, apiKey, model) = Setup();

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            var url = provider.GetTitleUrl(model, apiKey);
            var reqBody = provider.BuildTitleRequestBody(model, "You are a test assistant.", "Say hello.", 100, serviceTier: "priority");
            var jsonContent = JsonSerializer.Serialize(reqBody);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };
            provider.ConfigureRequest(httpRequest, apiKey);

            var ct = TestContext.Current.CancellationToken;
            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(httpRequest, ct);
            }
            catch (Exception ex) when (ex is TaskCanceledException || ex is TimeoutException || ex is HttpRequestException)
            {
                _output.WriteLine($"SKIPPED ASSERTIONS: Google request failed or timed out ({ex.GetType().Name}: {ex.Message}). " +
                                  "This is a provider capacity condition, not an Overseer defect. " +
                                  "See docs/overseer/gemini-service-tier-measurements.md.");
                Assert.True(true);
                return;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                    response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    _output.WriteLine($"SKIPPED ASSERTIONS: Google returned {(int)response.StatusCode}. " +
                                      "This is a provider capacity condition, not an Overseer defect. " +
                                      "See docs/overseer/gemini-service-tier-measurements.md.");
                    Assert.True(true);
                    return;
                }

                Assert.True(response.IsSuccessStatusCode, $"Expected success or 429/503, got {(int)response.StatusCode}");

                var headerTier = provider.ExtractServiceTierFromHeaders(response);
                var bodyStr = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(bodyStr);
                var bodyTier = provider.ExtractServiceTierFromBody(doc.RootElement);

                _output.WriteLine($"Google NonStreaming: Header tier='{headerTier}', Body tier='{bodyTier}'");
                Assert.NotNull(headerTier);
                Assert.NotNull(bodyTier);
                Assert.Equal("priority", headerTier);
                Assert.Equal("priority", bodyTier);
            }
        }

        [Fact]
        public async Task Google_Streaming_ReportsServiceTier_InBodyOnly()
        {
            var (_, provider, apiKey, model) = Setup();

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            var url = provider.GetChatStreamUrl(model, apiKey);
            var history = new List<object>
            {
                provider.FormatMessage("user", "Say hello in one word.", null)
            };
            var reqBody = provider.BuildChatRequestBody(model, history, 100, null, new ToolsForRequest(), serviceTier: "priority");
            var jsonContent = JsonSerializer.Serialize(reqBody);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };
            provider.ConfigureRequest(httpRequest, apiKey);

            var ct = TestContext.Current.CancellationToken;
            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (Exception ex) when (ex is TaskCanceledException || ex is TimeoutException || ex is HttpRequestException)
            {
                _output.WriteLine($"SKIPPED ASSERTIONS: Google request failed or timed out ({ex.GetType().Name}: {ex.Message}). " +
                                  "This is a provider capacity condition, not an Overseer defect. " +
                                  "See docs/overseer/gemini-service-tier-measurements.md.");
                Assert.True(true);
                return;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                    response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    _output.WriteLine($"SKIPPED ASSERTIONS: Google returned {(int)response.StatusCode}. " +
                                      "This is a provider capacity condition, not an Overseer defect. " +
                                      "See docs/overseer/gemini-service-tier-measurements.md.");
                    Assert.True(true);
                    return;
                }

                Assert.True(response.IsSuccessStatusCode, $"Expected success or 429/503, got {(int)response.StatusCode}");

                var headerTier = provider.ExtractServiceTierFromHeaders(response);
                _output.WriteLine($"Google Streaming Header tier: '{headerTier}'");
                Assert.Null(headerTier); // Measured absent on streaming endpoint

                var events = new List<ChatEvent>();
                await foreach (var evt in provider.ParseStreamAsync(response, showDebugLog: true, ct))
                {
                    events.Add(evt);
                }

                var tierEvents = events.Where(e => e.Type == "service_tier").ToList();
                _output.WriteLine($"Google Streaming emitted {tierEvents.Count} service_tier event(s)");
                Assert.NotEmpty(tierEvents);
                Assert.False(string.IsNullOrEmpty(tierEvents[0].Data));
            }
        }

        [Fact]
        public async Task Google_Streaming_PriorityRequest_IsHonouredOrDowngraded()
        {
            var (_, provider, apiKey, model) = Setup();

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            var url = provider.GetChatStreamUrl(model, apiKey);
            var history = new List<object>
            {
                provider.FormatMessage("user", "Say OK.", null)
            };
            var reqBody = provider.BuildChatRequestBody(model, history, 100, null, new ToolsForRequest(), serviceTier: "priority");
            var jsonContent = JsonSerializer.Serialize(reqBody);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };
            provider.ConfigureRequest(httpRequest, apiKey);

            var ct = TestContext.Current.CancellationToken;
            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (Exception ex) when (ex is TaskCanceledException || ex is TimeoutException || ex is HttpRequestException)
            {
                _output.WriteLine($"SKIPPED ASSERTIONS: Google request failed or timed out ({ex.GetType().Name}: {ex.Message}). " +
                                  "This is a provider capacity condition, not an Overseer defect. " +
                                  "See docs/overseer/gemini-service-tier-measurements.md.");
                Assert.True(true);
                return;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                    response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    _output.WriteLine($"SKIPPED ASSERTIONS: Google returned {(int)response.StatusCode}. " +
                                      "This is a provider capacity condition, not an Overseer defect. " +
                                      "See docs/overseer/gemini-service-tier-measurements.md.");
                    Assert.True(true);
                    return;
                }

                Assert.True(response.IsSuccessStatusCode, $"Expected success or 429/503, got {(int)response.StatusCode}");

                var events = new List<ChatEvent>();
                await foreach (var evt in provider.ParseStreamAsync(response, showDebugLog: true, ct))
                {
                    events.Add(evt);
                }

                var tierEvents = events.Where(e => e.Type == "service_tier").ToList();
                Assert.NotEmpty(tierEvents);
                var reportedTier = tierEvents[0].Data;
                _output.WriteLine($"Reported service tier: '{reportedTier}'");

                Assert.Contains(reportedTier, new[] { "priority", "standard", "flex" });
                if (reportedTier != "priority")
                {
                    _output.WriteLine($"Request for 'priority' was served as '{reportedTier}' (downgraded).");
                }
                else
                {
                    _output.WriteLine("Request for 'priority' was honoured as 'priority'.");
                }
            }
        }
    }
}

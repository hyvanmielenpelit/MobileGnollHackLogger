using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Overseer.Tests.UnitTests
{
    public class LiveTestConfigTests
    {
        [Fact]
        public void LiveApiModelPolicy_ResolveModel_ReturnsDefault_WhenUnsetOrWhitespace()
        {
            var configEmpty = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();

            var configWhitespace = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Test:Model"] = "   "
                })
                .Build();

            Assert.Equal("gemini-3.5-flash-lite", LiveApiModelPolicy.ResolveModel(configEmpty, "Test:Model"));
            Assert.Equal("gemini-3.5-flash-lite", LiveApiModelPolicy.ResolveModel(configWhitespace, "Test:Model"));
        }

        [Fact]
        public void LiveApiModelPolicy_ResolveModel_ReturnsConfigured_Trimmed()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Test:Model"] = "  gemini-custom-model  "
                })
                .Build();

            Assert.Equal("gemini-custom-model", LiveApiModelPolicy.ResolveModel(config, "Test:Model"));
        }

        [Fact]
        public void LiveApiModelPolicy_IsAllowed_DefaultAllowsOnlyFlashLite_CaseInsensitive()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();

            Assert.True(LiveApiModelPolicy.IsAllowed(config, "gemini-3.5-flash-lite"));
            Assert.True(LiveApiModelPolicy.IsAllowed(config, "GEMINI-3.5-FLASH-LITE"));
            Assert.False(LiveApiModelPolicy.IsAllowed(config, "gemini-3.6-flash"));
            Assert.False(LiveApiModelPolicy.IsAllowed(config, "gemini-3.7-flash"));
        }

        [Fact]
        public void LiveApiModelPolicy_IsAllowed_HonoursAllowedModelsOverride()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [LiveApiModelPolicy.AllowedModelsKey] = "gemini-3.5-flash-lite, gemini-3.6-flash"
                })
                .Build();

            Assert.True(LiveApiModelPolicy.IsAllowed(config, "gemini-3.5-flash-lite"));
            Assert.True(LiveApiModelPolicy.IsAllowed(config, "gemini-3.6-flash"));
            Assert.False(LiveApiModelPolicy.IsAllowed(config, "gemini-3.7-flash"));
        }

        [Fact]
        public void LiveApiModelPolicy_DisallowedMessage_ContainsExpectedInfo()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();

            var message = LiveApiModelPolicy.DisallowedMessage(config, "gemini-3.7-flash", "AI:ServiceTier:Model");

            Assert.Contains("gemini-3.7-flash", message);
            Assert.Contains("AI:ServiceTier:Model", message);
            Assert.Contains(LiveApiModelPolicy.AllowedModelsKey, message);
            Assert.Contains(LiveTestSecrets.DocPath, message);
        }

        [Fact]
        public void LiveTestSecrets_DescribeMissing_ReturnsNull_WhenAllPresent()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Key1"] = "val1",
                    ["Key2"] = "val2"
                })
                .Build();

            var report = LiveTestSecrets.DescribeMissing(
                config,
                "SampleTest",
                new LiveTestSecrets.Required("Key1", "Purpose 1"),
                new LiveTestSecrets.Required("Key2", "Purpose 2"));

            Assert.Null(report);
        }

        [Fact]
        public void LiveTestSecrets_DescribeMissing_ReportsAllMissing_TreatsWhitespaceAsMissing()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Key1"] = "val1",
                    ["Key2"] = "   ",
                    // Key3 omitted
                })
                .Build();

            var report = LiveTestSecrets.DescribeMissing(
                config,
                "SampleTest",
                new LiveTestSecrets.Required("Key1", "Purpose 1"),
                new LiveTestSecrets.Required("Key2", "Purpose 2"),
                new LiveTestSecrets.Required("Key3", "Purpose 3"));

            Assert.NotNull(report);
            Assert.DoesNotContain("Key1", report);
            Assert.Contains("Key2", report);
            Assert.Contains("Purpose 2", report);
            Assert.Contains("Key3", report);
            Assert.Contains("Purpose 3", report);
            Assert.Contains("dotnet user-secrets set \"Key2\"", report);
            Assert.Contains("dotnet user-secrets set \"Key3\"", report);
            Assert.Contains(LiveTestSecrets.DocPath, report);
            Assert.Contains("missing or empty (2)", report);
        }

        [Fact]
        public void LiveTestSecrets_DescribeGoogleConfigError_404NotFound_ReturnsLegibleMessage()
        {
            var payload = "{\n  \"error\": {\n    \"code\": 404,\n    \"message\": \"models/gemini-9.9-nonexistent is not found for API version v1beta, or is not supported for generateContent. Call ModelService.ListModels to see the list of available models.\",\n    \"status\": \"NOT_FOUND\"\n  }\n}";

            var error = LiveTestSecrets.DescribeGoogleConfigError(
                HttpStatusCode.NotFound,
                payload,
                "gemini-9.9-nonexistent",
                "AI:ServiceTier:Model",
                "AI:ServiceTier:APIKey");

            Assert.NotNull(error);
            Assert.Contains("gemini-9.9-nonexistent", error);
            Assert.Contains("AI:ServiceTier:Model", error);
            Assert.Contains("HTTP 404 NOT_FOUND", error);
            Assert.Contains(LiveTestSecrets.DocPath, error);
        }

        [Fact]
        public void LiveTestSecrets_DescribeGoogleConfigError_400InvalidApiKey_ReturnsLegibleMessage()
        {
            var payload = "{\n  \"error\": {\n    \"code\": 400,\n    \"message\": \"API key not valid. Please pass a valid API key.\",\n    \"status\": \"INVALID_ARGUMENT\",\n    \"details\": [\n      {\n        \"@type\": \"type.googleapis.com/google.rpc.ErrorInfo\",\n        \"reason\": \"API_KEY_INVALID\",\n        \"domain\": \"googleapis.com\",\n        \"metadata\": {\n          \"service\": \"generativelanguage.googleapis.com\"\n        }\n      }\n    ]\n  }\n}";

            var error = LiveTestSecrets.DescribeGoogleConfigError(
                HttpStatusCode.BadRequest,
                payload,
                "gemini-3.5-flash-lite",
                "AI:ServiceTier:Model",
                "AI:ServiceTier:APIKey");

            Assert.NotNull(error);
            Assert.Contains("AI:ServiceTier:APIKey", error);
            Assert.Contains("rejected", error);
            Assert.Contains("HTTP 400", error);
            Assert.Contains(LiveTestSecrets.DocPath, error);
        }

        [Fact]
        public void LiveTestSecrets_DescribeGoogleConfigError_400InvalidArgument_WithoutApiKeyInvalid_ReturnsNull()
        {
            var payload = "{\n  \"error\": {\n    \"code\": 400,\n    \"message\": \"Invalid JSON payload received. Unknown name \\\"foo\\\": Cannot find field.\",\n    \"status\": \"INVALID_ARGUMENT\"\n  }\n}";

            var error = LiveTestSecrets.DescribeGoogleConfigError(
                HttpStatusCode.BadRequest,
                payload,
                "gemini-3.5-flash-lite",
                "AI:ServiceTier:Model",
                "AI:ServiceTier:APIKey");

            Assert.Null(error);
        }

        [Fact]
        public void LiveTestSecrets_DescribeGoogleConfigError_CapacityErrors_ReturnNull()
        {
            Assert.Null(LiveTestSecrets.DescribeGoogleConfigError(
                HttpStatusCode.TooManyRequests, "Too Many Requests", "gemini-3.5-flash-lite", "AI:ServiceTier:Model", "AI:ServiceTier:APIKey"));

            Assert.Null(LiveTestSecrets.DescribeGoogleConfigError(
                HttpStatusCode.ServiceUnavailable, "Service Unavailable", "gemini-3.5-flash-lite", "AI:ServiceTier:Model", "AI:ServiceTier:APIKey"));
        }

        [Fact]
        public void LiveTestSecrets_DescribeGoogleConfigError_403Forbidden_ReturnsLegibleMessage()
        {
            var payload = "{\n  \"error\": {\n    \"code\": 403,\n    \"message\": \"Permission denied on resource project.\",\n    \"status\": \"PERMISSION_DENIED\"\n  }\n}";

            var error = LiveTestSecrets.DescribeGoogleConfigError(
                HttpStatusCode.Forbidden,
                payload,
                "gemini-3.5-flash-lite",
                "AI:ServiceTier:Model",
                "AI:ServiceTier:APIKey");

            Assert.NotNull(error);
            Assert.Contains("AI:ServiceTier:APIKey", error);
            Assert.Contains("Permission denied", error);
            Assert.Contains(LiveTestSecrets.DocPath, error);
        }

        [Fact]
        public void LiveTestSecrets_DescribeGoogleConfigError_404NonJsonBody_DoesNotThrow()
        {
            var error = LiveTestSecrets.DescribeGoogleConfigError(
                HttpStatusCode.NotFound,
                "<!DOCTYPE html><html><body>Not Found</body></html>",
                "gemini-9.9-nonexistent",
                "AI:ServiceTier:Model",
                "AI:ServiceTier:APIKey");

            Assert.NotNull(error);
            Assert.Contains("gemini-9.9-nonexistent", error);
            Assert.Contains("AI:ServiceTier:Model", error);
            Assert.Contains("HTTP 404 NOT_FOUND", error);
            Assert.Contains(LiveTestSecrets.DocPath, error);
        }
    }
}

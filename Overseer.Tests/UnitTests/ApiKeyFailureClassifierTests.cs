using System;
using Overseer.Services.ApiKeyAlerts;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ApiKeyFailureClassifierTests
{
    private const string Balance = nameof(ApiKeyFailureKind.InsufficientBalance);
    private const string Rejected = nameof(ApiKeyFailureKind.KeyRejected);

    private static ApiKeyFailureKind? Expected(string? kind) =>
        kind == null ? null : Enum.Parse<ApiKeyFailureKind>(kind);

    [Theory]
    // Anthropic
    [InlineData("Anthropic", 401, """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""", Rejected)]
    [InlineData("Anthropic", 400, """{"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low to access the Anthropic API. Please go to Plans & Billing to upgrade or purchase credits."}}""", Balance)]
    [InlineData("Anthropic", 402, """{"type":"error","error":{"type":"billing_error","message":"There is a problem with your billing."}}""", Balance)]
    [InlineData("Anthropic", 400, """{"type":"error","error":{"type":"invalid_request_error","message":"max_tokens: 100000 > 64000, which is the maximum allowed number of output tokens."}}""", null)]
    [InlineData("Anthropic", 529, """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""", null)]
    // OpenAI
    [InlineData("OpenAI", 401, """{"error":{"message":"Incorrect API key provided.","type":"invalid_request_error","param":null,"code":"invalid_api_key"}}""", Rejected)]
    [InlineData("OpenAI", 429, """{"error":{"message":"You exceeded your current quota, please check your plan and billing details.","type":"insufficient_quota","param":null,"code":"insufficient_quota"}}""", Balance)]
    [InlineData("OpenAI", 429, """{"error":{"message":"Rate limit reached for requests per min.","type":"requests","param":null,"code":"rate_limit_exceeded"}}""", null)]
    [InlineData("OpenAI", 400, """{"error":{"message":"Billing hard limit has been reached.","type":"invalid_request_error","param":null,"code":"billing_hard_limit_reached"}}""", Balance)]
    [InlineData("OpenAI", null, """{"type":"error","error":{"type":"insufficient_quota","code":"insufficient_quota","message":"You exceeded your current quota."}}""", Balance)]
    // Google
    [InlineData("Google", 400, """{"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT","details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"API_KEY_INVALID"}]}}""", Rejected)]
    [InlineData("Google", 400, """{"error":{"code":400,"message":"API key expired. Please renew the API key.","status":"INVALID_ARGUMENT"}}""", Rejected)]
    [InlineData("Google", 403, """{"error":{"code":403,"message":"This API method requires billing to be enabled.","status":"PERMISSION_DENIED","details":[{"reason":"BILLING_DISABLED"}]}}""", Balance)]
    [InlineData("Google", 503, """{"error":{"code":503,"message":"The model is overloaded. Please try again later.","status":"UNAVAILABLE"}}""", null)]
    // Provider names
    [InlineData("openai", 401, """{"error":{"message":"Incorrect API key provided.","type":"invalid_request_error","code":"invalid_api_key"}}""", Rejected)]
    [InlineData("Mistral", 401, """{"error":{"message":"Unauthorized","code":"invalid_api_key"}}""", null)]
    public void Classify_CannedProviderResponses(string provider, int? httpStatus, string body, string? expected)
    {
        Assert.Equal(Expected(expected), ApiKeyFailureClassifier.Classify(provider, httpStatus, body));
    }

    // Pins the deliberate decision that a Google 429 is never a balance failure, even when its
    // message names the quota and billing: Google answers rate limits the same way
    // (see docs/overseer/api-key-alerts.md).
    [Fact]
    public void Classify_Google429_WithQuotaWording_IsNull()
    {
        const string body = """{"error":{"code":429,"message":"You exceeded your current quota, please check your plan and billing details.","status":"RESOURCE_EXHAUSTED"}}""";

        Assert.Null(ApiKeyFailureClassifier.Classify("Google", 429, body));
    }

    [Fact]
    public void Classify_NullProvider_IsNull()
    {
        Assert.Null(ApiKeyFailureClassifier.Classify(null, 401, """{"error":{"code":"invalid_api_key"}}"""));
    }
}

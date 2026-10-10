using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ModelNotFoundClassifierTests
{
    private const string GoogleBody =
        "{\"error\":{\"code\":404,\"message\":\"models/gemini-3.7-flash is not found for API version v1beta, or is not supported for generateContent. Call ListModels to see the list of available models and their supported methods.\",\"status\":\"NOT_FOUND\"}}";

    private const string OpenAiBody =
        "{\"error\":{\"message\":\"The model `gpt-retired` does not exist or you do not have access to it.\",\"type\":\"invalid_request_error\",\"param\":null,\"code\":\"model_not_found\"}}";

    private const string AnthropicBody =
        "{\"type\":\"error\",\"error\":{\"type\":\"not_found_error\",\"message\":\"model: claude-retired\"},\"request_id\":\"req_test\"}";

    [Theory]
    [InlineData("Google", GoogleBody, "gemini-3.7-flash")]
    [InlineData("OpenAI", OpenAiBody, "gpt-retired")]
    [InlineData("Anthropic", AnthropicBody, "claude-retired")]
    public void ProviderNotFoundBody_Matches(string provider, string body, string modelId)
    {
        Assert.True(ModelNotFoundClassifier.IsModelNotFound(provider, 404, body, modelId));
    }

    [Theory]
    [InlineData("google")]
    [InlineData("GOOGLE")]
    public void ProviderName_IsCaseInsensitive(string provider)
    {
        Assert.True(ModelNotFoundClassifier.IsModelNotFound(provider, 404, GoogleBody, "some-other-model"));
    }

    [Fact]
    public void ProviderMarker_MatchesWithoutModelIdInMessage()
    {
        const string body = "{\"error\":{\"code\":404,\"message\":\"Requested entity was not found.\",\"status\":\"NOT_FOUND\"}}";

        Assert.True(ModelNotFoundClassifier.IsModelNotFound("Google", 404, body, "gemini-3.7-flash"));
    }

    [Fact]
    public void HtmlNotFoundPage_DoesNotMatch()
    {
        const string body = "<!DOCTYPE html><html><head><title>404 Not Found</title></head><body><h1>Not Found</h1><p>The requested URL /v1beta/models/gemini-3.7-flash was not found on this server.</p></body></html>";

        Assert.False(ModelNotFoundClassifier.IsModelNotFound("Google", 404, body, "gemini-3.7-flash"));
    }

    [Fact]
    public void BadRequest_DoesNotMatch()
    {
        const string body = "{\"error\":{\"code\":400,\"message\":\"models/gemini-3.7-flash: invalid argument.\",\"status\":\"NOT_FOUND\"}}";

        Assert.False(ModelNotFoundClassifier.IsModelNotFound("Google", 400, body, "gemini-3.7-flash"));
    }

    [Fact]
    public void MalformedJson_DoesNotMatch()
    {
        const string body = "{\"error\":{\"message\":\"models/gemini-3.7-flash is not found\",\"status\":\"NOT_FOUND\"";

        Assert.False(ModelNotFoundClassifier.IsModelNotFound("Google", 404, body, "gemini-3.7-flash"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyBody_DoesNotMatch(string? body)
    {
        Assert.False(ModelNotFoundClassifier.IsModelNotFound("Google", 404, body, "gemini-3.7-flash"));
    }

    [Fact]
    public void NotFoundNamingModelId_Matches()
    {
        const string body = "{\"error\":{\"message\":\"Model my-custom-model is not available on this server.\",\"type\":\"invalid_request_error\"}}";

        Assert.True(ModelNotFoundClassifier.IsModelNotFound("OpenAI", 404, body, "my-custom-model"));
    }

    [Fact]
    public void NotFoundNamingModelId_TopLevelMessage_Matches()
    {
        const string body = "{\"message\":\"Model my-custom-model not found\"}";

        Assert.True(ModelNotFoundClassifier.IsModelNotFound("OpenAI", 404, body, "my-custom-model"));
    }

    [Fact]
    public void JsonNotFoundWithoutMarkerOrModelId_DoesNotMatch()
    {
        // A custom endpoint answering a wrong path.
        const string body = "{\"error\":{\"message\":\"Not Found\",\"type\":\"invalid_request_error\"}}";

        Assert.False(ModelNotFoundClassifier.IsModelNotFound("OpenAI", 404, body, "my-custom-model"));
    }

    [Fact]
    public void OtherProvidersMarker_DoesNotMatch()
    {
        Assert.False(ModelNotFoundClassifier.IsModelNotFound("OpenAI", 404, AnthropicBody, "gpt-5"));
    }

    [Fact]
    public void UnknownProvider_MatchesOnlyByModelId()
    {
        Assert.False(ModelNotFoundClassifier.IsModelNotFound("Custom", 404, GoogleBody, "other-model"));
        Assert.True(ModelNotFoundClassifier.IsModelNotFound("Custom", 404, GoogleBody, "gemini-3.7-flash"));
    }
}

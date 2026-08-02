using Microsoft.Extensions.Options;

namespace Overseer.Services;

public class RecommendedModel
{
    public string Model { get; set; } = string.Empty;
    public string ThinkingLevel { get; set; } = string.Empty;
}

public class RecommendedModelService
{
    private readonly IConfiguration _configuration;

    public RecommendedModelService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public List<RecommendedModel> GetRecommendedModels(string provider)
    {
        var recommendedModels = new List<RecommendedModel>();
        var section = _configuration.GetSection($"RecommendedModels:{provider}");
        
        if (section.Exists())
        {
            var models = section.Get<List<RecommendedModel>>();
            if (models != null)
            {
                recommendedModels.AddRange(models);
            }
        }
        
        return recommendedModels;
    }
}

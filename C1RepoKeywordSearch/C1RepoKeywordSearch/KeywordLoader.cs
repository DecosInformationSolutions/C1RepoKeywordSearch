using Microsoft.Extensions.Configuration;

namespace C1RepoKeywordSearch;

public record KeywordConfig(List<string> Keywords, string? OutputFolder, List<string> WhitelistedUrls);

public static class KeywordLoader
{
    public static KeywordConfig Load()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var keywords = config.GetSection("Keywords")
            .GetChildren()
            .Select(c => c.Value?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Cast<string>()
            .ToList();

        if (keywords.Count == 0)
            throw new InvalidOperationException("No keywords found in appsettings.json under \"Keywords\".");

        var placeholders = config.GetSection("PlaceholderPatterns")
            .GetChildren()
            .Select(c => c.Value?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Cast<string>()
            .ToList();

        PlaceholderDetector.Load(placeholders);

        var whitelistedUrls = config.GetSection("WhitelistedUrls")
            .GetChildren()
            .Select(c => c.Value?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Cast<string>()
            .ToList();

        var outputFolder = config["OutputFolder"]?.Trim();
        return new KeywordConfig(keywords, string.IsNullOrWhiteSpace(outputFolder) ? null : outputFolder, whitelistedUrls);
    }
}


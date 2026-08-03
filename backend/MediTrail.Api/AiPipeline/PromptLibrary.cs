using System.Collections.Concurrent;

namespace MediTrail.Api.AiPipeline;

/// <summary>
/// Loads prompts from files rather than inline strings (§15 maintainability), so a prompt can be
/// edited and diffed without touching code — which matters because §18.4 requires re-running the
/// golden dataset on every prompt change.
/// </summary>
public interface IPromptLibrary
{
    string Get(string name);
    string Get(string name, IReadOnlyDictionary<string, string> placeholders);
}

public sealed class PromptLibrary(ILogger<PromptLibrary> logger) : IPromptLibrary
{
    private readonly ConcurrentDictionary<string, string> _cache = new();

    private static readonly string PromptDirectory =
        Path.Combine(AppContext.BaseDirectory, "AiPipeline", "Prompts");

    public string Get(string name) => _cache.GetOrAdd(name, key =>
    {
        var path = Path.Combine(PromptDirectory, $"{key}.md");

        if (!File.Exists(path))
        {
            // Failing loudly at first use beats sending an empty system prompt and quietly
            // getting garbage back.
            logger.LogError("Prompt '{Name}' not found at {Path}", key, path);
            throw new FileNotFoundException($"Prompt '{key}' was not found at {path}.", path);
        }

        return File.ReadAllText(path);
    });

    /// <summary>Substitutes <c>{{KEY}}</c> placeholders.</summary>
    public string Get(string name, IReadOnlyDictionary<string, string> placeholders)
    {
        var text = Get(name);

        foreach (var (key, value) in placeholders)
        {
            text = text.Replace($"{{{{{key}}}}}", value, StringComparison.Ordinal);
        }

        return text;
    }
}

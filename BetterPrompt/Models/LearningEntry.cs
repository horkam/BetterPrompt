namespace BetterPrompt.Models;

public class LearningEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string OriginalPrompt { get; set; } = string.Empty;
    public string OptimizedPrompt { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = [];
    public List<string> RulesFired { get; set; } = [];
    public OptimizationSource Source { get; set; } = OptimizationSource.Rules;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Machine { get; set; } = Environment.MachineName;
}

public enum OptimizationSource
{
    Cache,
    Rules,
    Ollama,
    RulesAndOllama
}

namespace BetterPrompt.Models;

public class OptimizationResult
{
    public string OriginalPrompt { get; set; } = string.Empty;
    public string OptimizedPrompt { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public List<string> Changes { get; set; } = [];
    public OptimizationSource Source { get; set; } = OptimizationSource.Rules;
    public double? CacheMatchScore { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public string SourceLabel => Source switch
    {
        OptimizationSource.Cache => "From learning cache",
        OptimizationSource.Rules => "Rule-based",
        OptimizationSource.Ollama => "Ollama",
        OptimizationSource.RulesAndOllama => "Rules + Ollama",
        _ => string.Empty
    };
}

using BetterPrompt.Models;

namespace BetterPrompt.Services;

public class PromptOptimizerService
{
    private readonly AppSettings _settings;
    private readonly RuleBasedOptimizer _ruleOptimizer;
    private readonly OllamaOptimizer _ollamaOptimizer;
    private readonly LearningStore _learningStore;

    public PromptOptimizerService(AppSettings settings, LearningStore learningStore)
    {
        _settings = settings;
        _learningStore = learningStore;
        _ruleOptimizer = new RuleBasedOptimizer();
        _ollamaOptimizer = new OllamaOptimizer(settings);
    }

    public async Task<OptimizationResult> OptimizeAsync(
        string rawPrompt,
        CodebaseContext? context,
        IProgress<string>? progress = null)
    {
        // Step 1: Check learning cache
        progress?.Report("Checking learning cache...");
        var cacheHit = _learningStore.FindSimilarWithScore(rawPrompt, _settings.SimilarityThreshold);
        if (cacheHit.HasValue)
        {
            progress?.Report("Cache hit found.");
            return new OptimizationResult
            {
                Success = true,
                OriginalPrompt = rawPrompt,
                OptimizedPrompt = cacheHit.Value.entry.OptimizedPrompt,
                Explanation = $"Matched a prior optimization ({cacheHit.Value.score:P0} similar).",
                Changes = cacheHit.Value.entry.RulesFired,
                Source = OptimizationSource.Cache,
                CacheMatchScore = cacheHit.Value.score
            };
        }

        // Step 2: Rule-based pass
        progress?.Report("Running rule-based optimizer...");
        var (ruledPrompt, rulesFired) = _ruleOptimizer.Optimize(rawPrompt, context);
        var source = OptimizationSource.Rules;
        var finalPrompt = ruledPrompt;

        // Step 3: Ollama pass (if enabled and available)
        if (_settings.UseOllama)
        {
            progress?.Report("Checking Ollama...");
            var ollamaAvailable = await _ollamaOptimizer.IsAvailableAsync();

            if (ollamaAvailable)
            {
                var ollamaResult = await _ollamaOptimizer.OptimizeAsync(ruledPrompt, context, progress);
                if (!string.IsNullOrWhiteSpace(ollamaResult))
                {
                    finalPrompt = ollamaResult;
                    source = rulesFired.Count > 0
                        ? OptimizationSource.RulesAndOllama
                        : OptimizationSource.Ollama;
                    rulesFired.Add("Ollama rewrote for clarity and specificity");
                }
            }
            else
            {
                progress?.Report("Ollama not running — using rules only.");
            }
        }

        // Step 4: Save to learning store
        var entry = new LearningEntry
        {
            OriginalPrompt = rawPrompt,
            OptimizedPrompt = finalPrompt,
            RulesFired = rulesFired,
            Source = source
        };
        _learningStore.Save(entry);

        return new OptimizationResult
        {
            Success = true,
            OriginalPrompt = rawPrompt,
            OptimizedPrompt = finalPrompt,
            Explanation = BuildExplanation(source, rulesFired),
            Changes = rulesFired,
            Source = source
        };
    }

    private static string BuildExplanation(OptimizationSource source, List<string> rules) => source switch
    {
        OptimizationSource.RulesAndOllama => $"{rules.Count - 1} rules applied, then Ollama refined the result.",
        OptimizationSource.Ollama         => "Ollama rewrote the prompt for clarity and specificity.",
        OptimizationSource.Rules          => $"{rules.Count} rule{(rules.Count == 1 ? "" : "s")} applied.",
        _                                 => "Optimized."
    };
}

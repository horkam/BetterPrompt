using BetterPrompt.Models;

namespace BetterPrompt.Services;

public class PromptOptimizerService
{
    private readonly AppSettings _settings;
    private readonly RuleBasedOptimizer _ruleOptimizer;
    private readonly OllamaOptimizer _ollamaOptimizer;
    private readonly LearningStore _learningStore;
    private readonly KeywordExpander _keywordExpander;

    public PromptOptimizerService(AppSettings settings, LearningStore learningStore)
    {
        _settings = settings;
        _learningStore = learningStore;
        _ruleOptimizer = new RuleBasedOptimizer();
        _ollamaOptimizer = new OllamaOptimizer(settings);
        _keywordExpander = new KeywordExpander(settings);
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
                CacheMatchScore = cacheHit.Value.score,
                CachedEntryId = cacheHit.Value.entry.Id
            };
        }

        // Step 2: Expand keywords for richer codebase search
        progress?.Report("Expanding keywords...");
        var baseKeywords = SimilarityMatcher.Tokenize(rawPrompt);
        var expandedKeywords = context is not null
            ? await _keywordExpander.ExpandAsync(baseKeywords, progress)
            : baseKeywords;

        // Step 3: Rule-based pass (uses expanded keywords for reference injection, base keywords for scoring)
        progress?.Report("Running rule-based optimizer...");
        var (ruledPrompt, rulesFired) = _ruleOptimizer.Optimize(rawPrompt, context, expandedKeywords, baseKeywords);
        var source = OptimizationSource.Rules;
        var finalPrompt = ruledPrompt;

        // Split the ruled prompt into prose + locations block so Ollama can't lose the references
        var (promptProse, locationsBlock) = SplitLocationsBlock(ruledPrompt);

        // Step 4: Ollama pass (if enabled and available)
        if (_settings.UseOllama)
        {
            progress?.Report("Checking Ollama...");
            var ollamaAvailable = await _ollamaOptimizer.IsAvailableAsync();

            if (ollamaAvailable)
            {
                // Only send Ollama the prose — never the locations block
                var (ollamaResult, ollamaError) = await _ollamaOptimizer.OptimizeAsync(promptProse, context, progress);

                if (!string.IsNullOrWhiteSpace(ollamaResult))
                {
                    // Always re-attach the locations block regardless of what Ollama returned
                    finalPrompt = string.IsNullOrWhiteSpace(locationsBlock)
                        ? ollamaResult.Trim()
                        : $"{ollamaResult.Trim()}\n\n{locationsBlock.Trim()}";

                    source = rulesFired.Count > 0
                        ? OptimizationSource.RulesAndOllama
                        : OptimizationSource.Ollama;
                    rulesFired.Add("Ollama rewrote prose for clarity and specificity");
                }
                else
                {
                    progress?.Report($"Ollama skipped: {ollamaError ?? "no output returned"}");
                    rulesFired.Add($"Ollama skipped: {ollamaError ?? "no output"}");
                }
            }
            else
            {
                progress?.Report($"Ollama not available for model '{_settings.OllamaModel}' — check it is pulled.");
            }
        }

        // Step 5: Save to learning store
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
        OptimizationSource.RulesAndOllama => $"{rules.Count - 1} rules applied, then Ollama refined the prose.",
        OptimizationSource.Ollama         => "Ollama rewrote the prompt for clarity and specificity.",
        OptimizationSource.Rules          => $"{rules.Count} rule{(rules.Count == 1 ? "" : "s")} applied.",
        _                                 => "Optimized."
    };

    // Splits "...prompt prose...\n\nRelevant locations found in codebase:\n  - ..."
    // into (prose, locationsBlock) so the locations block survives the Ollama pass intact.
    private static (string prose, string locationsBlock) SplitLocationsBlock(string prompt)
    {
        const string Marker = "Relevant locations found in codebase:";
        var idx = prompt.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return (prompt, string.Empty);

        return (
            prompt[..idx].Trim(),
            prompt[idx..].Trim()
        );
    }
}

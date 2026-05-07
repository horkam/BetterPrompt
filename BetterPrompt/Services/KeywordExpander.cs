using BetterPrompt.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace BetterPrompt.Services;

public class KeywordExpander
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    // Static synonym clusters — any word in a group expands to all others in that group
    private static readonly List<HashSet<string>> SynonymGroups =
    [
        // Motion / exercise
        ["movement", "motion", "action", "gesture", "exercise", "activity", "rep", "repetition", "move", "moves"],
        // Workout / training
        ["workout", "training", "session", "routine", "program", "regimen", "drill", "practice"],
        // Auth
        ["auth", "authentication", "login", "signin", "sign-in", "credentials", "access", "permission", "authorize", "authorization", "identity"],
        // API / routing
        ["api", "endpoint", "route", "handler", "controller", "service", "rest", "http", "request", "response"],
        // Error handling
        ["error", "exception", "fault", "bug", "issue", "problem", "failure", "crash", "defect"],
        // User / account
        ["user", "person", "member", "account", "profile", "customer", "client", "player", "athlete"],
        // Data / model
        ["data", "model", "entity", "record", "schema", "object", "dto", "viewmodel", "payload"],
        // Database
        ["database", "db", "store", "repository", "repo", "storage", "persistence", "table", "collection"],
        // Configuration
        ["config", "configuration", "setting", "option", "parameter", "preference"],
        // Notification / messaging
        ["notification", "message", "alert", "event", "signal", "email", "push"],
        // Payment
        ["payment", "billing", "charge", "invoice", "transaction", "order", "purchase", "checkout"],
        // Search
        ["search", "filter", "query", "find", "lookup", "locate", "discover"],
        // File / upload
        ["file", "upload", "attachment", "document", "asset", "media", "image", "photo"],
        // Logging
        ["log", "logging", "trace", "audit", "monitor", "telemetry", "metric"],
        // Test
        ["test", "spec", "unit", "integration", "assertion", "mock", "stub", "fixture"],
        // Navigation / location
        ["navigation", "location", "position", "route", "path", "direction", "gps", "coordinate"],
        // Schedule / time
        ["schedule", "calendar", "appointment", "booking", "reservation", "slot", "time", "date"],
        // Report / analytics
        ["report", "analytics", "stats", "statistics", "dashboard", "chart", "graph", "metric", "summary"],
        // Product / item
        ["product", "item", "sku", "inventory", "stock", "catalog", "listing"],
        // Role / permission
        ["role", "permission", "claim", "policy", "scope", "privilege", "right", "access"],
    ];

    // Build reverse lookup: word → group index for O(1) expansion
    private static readonly Dictionary<string, HashSet<string>> SynonymLookup = BuildLookup();

    private static Dictionary<string, HashSet<string>> BuildLookup()
    {
        var lookup = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in SynonymGroups)
            foreach (var word in group)
                lookup[word] = group;
        return lookup;
    }

    private readonly AppSettings _settings;

    public KeywordExpander(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task<List<string>> ExpandAsync(
        List<string> keywords,
        IProgress<string>? progress = null)
    {
        var expanded = ExpandStatic(keywords);

        // Ollama expansion for anything not already in the static groups
        if (_settings.UseOllama)
        {
            var ollamaExpanded = await ExpandWithOllamaAsync(keywords, progress);
            foreach (var term in ollamaExpanded)
                expanded.Add(term.ToLowerInvariant());
        }

        return expanded.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public List<string> ExpandStatic(List<string> keywords)
    {
        var expanded = new HashSet<string>(keywords, StringComparer.OrdinalIgnoreCase);

        foreach (var keyword in keywords)
        {
            // Direct lookup
            if (SynonymLookup.TryGetValue(keyword, out var group))
            {
                foreach (var syn in group)
                    expanded.Add(syn);
                continue;
            }

            // Stem check: try stripping common suffixes to find a root in the lookup
            // e.g. "movements" → "movement", "exercises" → "exercise"
            foreach (var stem in GetStems(keyword))
            {
                if (SynonymLookup.TryGetValue(stem, out var stemGroup))
                {
                    foreach (var syn in stemGroup)
                        expanded.Add(syn);
                    // Also add the stems themselves
                    foreach (var s in GetStems(keyword))
                        expanded.Add(s);
                    break;
                }
            }
        }

        return [.. expanded];
    }

    private async Task<List<string>> ExpandWithOllamaAsync(
        List<string> keywords,
        IProgress<string>? progress)
    {
        progress?.Report("Expanding keywords with Ollama...");

        var keywordList = string.Join(", ", keywords);
        var requestBody = new
        {
            model = _settings.OllamaModel,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = """
                        You expand search keywords for code searching.
                        Given a list of keywords from a user's prompt, return related terms,
                        synonyms, and alternative names a developer might use in code.
                        Focus on: variable names, class names, method names, file names.
                        Return ONLY a comma-separated list of lowercase single words. No explanation.
                        Limit to 10 terms total.
                        """
                },
                new
                {
                    role = "user",
                    content = $"Keywords: {keywordList}"
                }
            },
            stream = false,
            options = new { temperature = 0.2 }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await Http.PostAsync($"{_settings.OllamaUrl}/api/chat", content);
            if (!response.IsSuccessStatusCode) return [];

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var text = doc.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;

            return text
                .Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length > 2 && !t.Contains(' '))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> GetStems(string word)
    {
        var w = word.ToLowerInvariant();
        // Common English suffixes to try stripping
        string[] suffixes = ["ments", "ment", "ings", "ing", "tions", "tion", "sions",
                              "sion", "ities", "ity", "ness", "ers", "er", "ors", "or",
                              "ies", "es", "s"];
        foreach (var suffix in suffixes)
        {
            if (w.EndsWith(suffix) && w.Length - suffix.Length >= 3)
                yield return w[..^suffix.Length];
        }
    }
}

using BetterPrompt.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace BetterPrompt.Services;

public class OllamaOptimizer
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private const string SystemPrompt = """
        You are a prompt optimizer for Claude Code, an AI coding assistant.
        You receive a draft prompt and optional codebase context.

        Rewrite the prompt to be specific and actionable:
        - Start with a clear action verb (Add, Fix, Refactor, Update, Move, etc.)
        - Use exact file paths and class/method names from the context when relevant
        - Remove vague phrases like "look at the code", "find where", "search for"
        - Be concise — every word must add meaning
        - Do NOT add explanations or commentary

        Return ONLY the rewritten prompt. Nothing else.
        """;

    private readonly AppSettings _settings;

    public OllamaOptimizer(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await Http.GetAsync($"{_settings.OllamaUrl}/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> OptimizeAsync(string prompt, CodebaseContext? context, IProgress<string>? progress = null)
    {
        progress?.Report("Sending to Ollama...");

        var userContent = context is not null
            ? $"{context.ToContextBlock()}\n---\nDraft prompt to optimize:\n{prompt}"
            : $"Draft prompt to optimize:\n{prompt}";

        var requestBody = new
        {
            model = _settings.OllamaModel,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user",   content = userContent }
            },
            stream = false,
            options = new { temperature = 0.3 }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await Http.PostAsync($"{_settings.OllamaUrl}/api/chat", content);
            if (!response.IsSuccessStatusCode) return null;

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            return doc.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
                ?.Trim();
        }
        catch
        {
            return null;
        }
    }
}

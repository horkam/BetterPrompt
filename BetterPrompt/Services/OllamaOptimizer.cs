using BetterPrompt.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace BetterPrompt.Services;

public class OllamaOptimizer : IAiChatService
{
    // Generous timeout — first inference call loads the model into VRAM which can take 20-30s
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(180) };

    private const string SystemPrompt = """
        You are a prompt optimizer for Claude Code, an AI coding assistant.
        You receive a draft prompt that has already been partially cleaned by rule-based passes.

        Your job:
        - Rewrite it to be specific and actionable
        - Start with a clear action verb (Add, Fix, Refactor, Update, Move, etc.)
        - Remove any remaining vague phrases ("look at the code", "I don't know where", etc.)
        - Be concise — every word must add meaning
        - Do NOT add explanations, preamble, or commentary

        Return ONLY the rewritten prompt text. Nothing else.
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
            if (!response.IsSuccessStatusCode) return false;

            // Also verify the configured model is actually pulled
            var json = await response.Content.ReadAsStringAsync();
            return json.Contains(_settings.OllamaModel.Split(':')[0], StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<(string? result, string? error)> OptimizeAsync(
        string prompt,
        CodebaseContext? context,
        IProgress<string>? progress = null)
    {
        progress?.Report($"Sending to Ollama ({_settings.OllamaModel})...");

        // Only send the prompt text to Ollama — codebase context is too large for small models
        // and the rules pass already injected the relevant locations
        var userContent = $"Draft prompt to optimize:\n{prompt}";

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
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            progress?.Report($"Waiting for {_settings.OllamaModel} (may take a moment on first run)...");
            var response = await Http.PostAsync($"{_settings.OllamaUrl}/api/chat", httpContent);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                return (null, $"Ollama returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 200)]}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            var result = doc.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
                ?.Trim();

            return string.IsNullOrWhiteSpace(result)
                ? (null, "Ollama returned empty content")
                : (result, null);
        }
        catch (TaskCanceledException)
        {
            return (null, "Ollama timed out (model may still be loading — try again in a few seconds)");
        }
        catch (Exception ex)
        {
            return (null, $"Ollama error: {ex.Message}");
        }
    }

    public async Task<(string? result, string? error)> ChatAsync(
        string systemPrompt,
        IEnumerable<(string role, string content)> history)
    {
        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        foreach (var (role, content) in history)
            messages.Add(new { role, content });

        var json = JsonSerializer.Serialize(new { model = _settings.OllamaModel, messages, stream = false });
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await Http.PostAsync($"{_settings.OllamaUrl}/api/chat", httpContent);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                return (null, $"Ollama returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 200)]}");
            }
            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var result = doc.RootElement.GetProperty("message").GetProperty("content").GetString()?.Trim();
            return string.IsNullOrWhiteSpace(result) ? (null, "Ollama returned empty content") : (result, null);
        }
        catch (TaskCanceledException)
        {
            return (null, "Timed out — model may still be loading, try again in a moment");
        }
        catch (Exception ex)
        {
            return (null, $"Ollama error: {ex.Message}");
        }
    }
}

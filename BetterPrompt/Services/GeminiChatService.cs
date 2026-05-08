using BetterPrompt.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace BetterPrompt.Services;

public class GeminiChatService : IAiChatService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly AppSettings _settings;

    public GeminiChatService(AppSettings settings) => _settings = settings;

    public async Task<(bool success, string message)> TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.GeminiApiKey))
            return (false, "Gemini: no API key set");

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={_settings.GeminiApiKey}";
            var response = await Http.GetAsync(url);
            return response.IsSuccessStatusCode
                ? (true, $"Gemini: connected ({_settings.GeminiModel})")
                : (false, $"Gemini: error {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"Gemini: {ex.Message}");
        }
    }

    public async Task<(string? result, string? error)> ChatAsync(
        string systemPrompt,
        IEnumerable<(string role, string content)> history)
    {
        // Gemini uses "user"/"model" roles and system_instruction separately
        var contents = history.Select(m => new
        {
            role = m.role == "assistant" ? "model" : "user",
            parts = new[] { new { text = m.content } }
        }).ToArray();

        var body = JsonSerializer.Serialize(new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents
        });

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_settings.GeminiModel}:generateContent?key={_settings.GeminiApiKey}";

        try
        {
            var response = await Http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return (null, $"Gemini error {(int)response.StatusCode}: {json[..Math.Min(json.Length, 200)]}");

            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString()
                ?.Trim();

            return string.IsNullOrWhiteSpace(text)
                ? (null, "Gemini returned empty content")
                : (text, null);
        }
        catch (TaskCanceledException)
        {
            return (null, "Gemini request timed out");
        }
        catch (Exception ex)
        {
            return (null, $"Gemini error: {ex.Message}");
        }
    }
}

using BetterPrompt.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace BetterPrompt.Services;

public class ClaudeChatService : IAiChatService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly AppSettings _settings;

    public ClaudeChatService(AppSettings settings) => _settings = settings;

    public async Task<(bool success, string message)> TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.AnthropicApiKey))
            return (false, "Claude: no API key set");

        var body = JsonSerializer.Serialize(new
        {
            model = _settings.ClaudeModel,
            max_tokens = 1,
            messages = new[] { new { role = "user", content = "hi" } }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", _settings.AnthropicApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            var response = await Http.SendAsync(request);
            return response.IsSuccessStatusCode
                ? (true, $"Claude: connected ({_settings.ClaudeModel})")
                : (false, $"Claude: error {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"Claude: {ex.Message}");
        }
    }

    public async Task<(string? result, string? error)> ChatAsync(
        string systemPrompt,
        IEnumerable<(string role, string content)> history)
    {
        var messages = history
            .Select(m => new { role = m.role == "assistant" ? "assistant" : "user", content = m.content })
            .ToArray();

        var body = JsonSerializer.Serialize(new
        {
            model = _settings.ClaudeModel,
            max_tokens = 1024,
            system = systemPrompt,
            messages
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", _settings.AnthropicApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            var response = await Http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return (null, $"Claude error {(int)response.StatusCode}: {json[..Math.Min(json.Length, 200)]}");

            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()
                ?.Trim();

            return string.IsNullOrWhiteSpace(text)
                ? (null, "Claude returned empty content")
                : (text, null);
        }
        catch (TaskCanceledException)
        {
            return (null, "Claude request timed out");
        }
        catch (Exception ex)
        {
            return (null, $"Claude error: {ex.Message}");
        }
    }
}

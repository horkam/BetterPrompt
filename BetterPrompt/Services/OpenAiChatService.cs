using BetterPrompt.Models;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BetterPrompt.Services;

public class OpenAiChatService : IAiChatService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly AppSettings _settings;

    public OpenAiChatService(AppSettings settings) => _settings = settings;

    public async Task<(bool success, string message)> TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.OpenAiApiKey))
            return (false, "OpenAI: no API key set");

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.OpenAiApiKey);

        try
        {
            var response = await Http.SendAsync(request);
            return response.IsSuccessStatusCode
                ? (true, $"OpenAI: connected ({_settings.OpenAiModel})")
                : (false, $"OpenAI: error {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"OpenAI: {ex.Message}");
        }
    }

    public async Task<(string? result, string? error)> ChatAsync(
        string systemPrompt,
        IEnumerable<(string role, string content)> history)
    {
        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        messages.AddRange(history.Select(m => (object)new { role = m.role, content = m.content }));

        var body = JsonSerializer.Serialize(new { model = _settings.OpenAiModel, messages });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.OpenAiApiKey);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            var response = await Http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return (null, $"OpenAI error {(int)response.StatusCode}: {json[..Math.Min(json.Length, 200)]}");

            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
                ?.Trim();

            return string.IsNullOrWhiteSpace(text)
                ? (null, "OpenAI returned empty content")
                : (text, null);
        }
        catch (TaskCanceledException)
        {
            return (null, "OpenAI request timed out");
        }
        catch (Exception ex)
        {
            return (null, $"OpenAI error: {ex.Message}");
        }
    }
}

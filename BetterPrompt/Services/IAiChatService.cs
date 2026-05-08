namespace BetterPrompt.Services;

public interface IAiChatService
{
    Task<(string? result, string? error)> ChatAsync(
        string systemPrompt,
        IEnumerable<(string role, string content)> history);
}

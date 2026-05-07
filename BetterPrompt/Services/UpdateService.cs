using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace BetterPrompt.Services;

public class UpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/horkam/BetterPrompt/releases/latest";
    public const string ReleasesUrl = "https://github.com/horkam/BetterPrompt/releases/latest";
    public const string GitHubUrl = "https://github.com/horkam/BetterPrompt";

    public string CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v && v != new Version(0, 0, 0, 0)
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "dev";

    public async Task<UpdateCheckResult> CheckAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "BetterPrompt");
            var release = await client.GetFromJsonAsync<GitHubRelease>(ApiUrl);
            if (release?.TagName is null) return UpdateCheckResult.Failed;

            var latest = release.TagName.TrimStart('v');
            bool isNewer = IsNewerVersion(latest, CurrentVersion);
            return new UpdateCheckResult(true, isNewer, isNewer ? latest : null);
        }
        catch
        {
            return UpdateCheckResult.Failed;
        }
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        return Version.TryParse(latest, out var l) &&
               Version.TryParse(current, out var c) &&
               l > c;
    }
}

public record UpdateCheckResult(bool CheckSucceeded, bool UpdateAvailable, string? LatestVersion)
{
    public static readonly UpdateCheckResult Failed = new(false, false, null);
}

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }
}

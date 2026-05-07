using BetterPrompt.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterPrompt.Services;

public class LearningStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private string? _storePath;
    private List<LearningEntry> _entries = [];

    public int EntryCount => _entries.Count;

    public void Load(string codebaseRoot)
    {
        var dir = Path.Combine(codebaseRoot, ".betterPrompt");
        _storePath = Path.Combine(dir, "learning.json");

        if (!File.Exists(_storePath))
        {
            _entries = [];
            return;
        }

        try
        {
            var json = File.ReadAllText(_storePath);
            _entries = JsonSerializer.Deserialize<List<LearningEntry>>(json, JsonOptions) ?? [];
        }
        catch
        {
            _entries = [];
        }
    }

    public LearningEntry? FindSimilar(string prompt, double threshold = 0.65)
    {
        if (_entries.Count == 0) return null;

        var tokens = SimilarityMatcher.Tokenize(prompt);

        return _entries
            .Select(e => (entry: e, score: SimilarityMatcher.JaccardSimilarity(tokens, e.Keywords)))
            .Where(x => x.score >= threshold)
            .OrderByDescending(x => x.score)
            .Select(x => x.entry)
            .FirstOrDefault();
    }

    public (LearningEntry entry, double score)? FindSimilarWithScore(string prompt, double threshold = 0.65)
    {
        if (_entries.Count == 0) return null;

        var tokens = SimilarityMatcher.Tokenize(prompt);

        var best = _entries
            .Select(e => (entry: e, score: SimilarityMatcher.JaccardSimilarity(tokens, e.Keywords)))
            .Where(x => x.score >= threshold)
            .OrderByDescending(x => x.score)
            .FirstOrDefault();

        if (best == default) return null;
        return (best.entry, best.score);
    }

    public void Save(LearningEntry entry)
    {
        if (_storePath is null) return;

        entry.Keywords = SimilarityMatcher.Tokenize(entry.OriginalPrompt);
        _entries.Add(entry);
        Flush();
    }

    public void Update(LearningEntry entry)
    {
        var existing = _entries.FirstOrDefault(e => e.Id == entry.Id);
        if (existing is not null)
            _entries.Remove(existing);
        _entries.Add(entry);
        Flush();
    }

    private void Flush()
    {
        if (_storePath is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
        File.WriteAllText(_storePath, JsonSerializer.Serialize(_entries, JsonOptions));
    }
}

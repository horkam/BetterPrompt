namespace BetterPrompt.Services;

public static class SimilarityMatcher
{
    private static readonly HashSet<string> StopWords =
    [
        "a","an","the","is","are","was","were","be","been","being",
        "have","has","had","do","does","did","will","would","could",
        "should","may","might","can","to","of","in","for","on","with",
        "at","by","from","as","it","its","this","that","i","you","we",
        "they","he","she","and","or","but","if","not","no","so","up",
        "about","into","than","then","there","when","where","which","who",
        "please","need","want","make","just","also","like","how","what"
    ];

    public static List<string> Tokenize(string text)
    {
        return text.ToLowerInvariant()
            .Split([' ', '\t', '\n', '\r', '.', ',', '?', '!', ':', ';', '"', '\'', '(', ')', '[', ']', '{', '}', '`', '_', '-', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2 && !StopWords.Contains(t))
            .Distinct()
            .ToList();
    }

    public static double JaccardSimilarity(IEnumerable<string> a, IEnumerable<string> b)
    {
        var setA = new HashSet<string>(a);
        var setB = new HashSet<string>(b);
        if (setA.Count == 0 && setB.Count == 0) return 1.0;
        if (setA.Count == 0 || setB.Count == 0) return 0.0;

        var intersection = setA.Count(setB.Contains);
        var union = setA.Count + setB.Count - intersection;
        return (double)intersection / union;
    }

    public static double Similarity(string a, string b)
        => JaccardSimilarity(Tokenize(a), Tokenize(b));
}

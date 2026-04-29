namespace C1RepoKeywordSearch;

public static class PlaceholderDetector
{
    private static List<string> _patterns = [];

    public static void Load(IEnumerable<string> patterns)
    {
        _patterns = patterns.Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length > 0).ToList();
    }

    public static bool IsPlaceholder(string line, string keyword, int matchIndex)
    {
        string extractedValue = ExtractValue(line, matchIndex + keyword.Length);
        if (string.IsNullOrWhiteSpace(extractedValue)) return false;

        string valueLower = extractedValue.ToLowerInvariant();

        // Option A: value contains the keyword itself (e.g. apikey="apikey123")
        if (valueLower.Contains(keyword.ToLowerInvariant())) return true;

        // Option B: value matches a known placeholder pattern
        return _patterns.Any(p => valueLower.Contains(p));
    }

    private static string ExtractValue(string line, int fromIndex)
    {
        // Find '=' after the match, then extract the value between quotes or up to ; < space
        int eqIndex = line.IndexOf('=', fromIndex);
        if (eqIndex < 0) return string.Empty;

        int start = eqIndex + 1;
        while (start < line.Length && (line[start] == ' ' || line[start] == '"' || line[start] == '\'')) start++;

        int end = start;
        while (end < line.Length && line[end] != '"' && line[end] != '\'' && line[end] != ';'
               && line[end] != '<' && line[end] != '>' && line[end] != ' ') end++;

        return end > start ? line[start..end] : string.Empty;
    }
}

namespace C1RepoKeywordSearch;

using System.Diagnostics;
using System.Text.RegularExpressions;

public record ScanMatch(string Keyword, string FilePath, string MatchType, int? LineNumber, string? LinePreview, string? BlameCommit = null);

public static class Scanner
{
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", "node_modules", "packages", ".vs"
    };

    // Names excluded from name-hit matching only — their contents are still scanned
    private static readonly HashSet<string> SkippedNameMatches = new(StringComparer.OrdinalIgnoreCase)
    {
        "wwwroot"
    };

    private static List<string> _whitelistedUrls = [];

    public static void LoadWhitelistedUrls(IEnumerable<string> urls)
    {
        _whitelistedUrls = urls.Select(u => u.Trim()).Where(u => u.Length > 0).ToList();
    }

    private static bool IsInSkippedFolder(string path, string repoPath)
    {
        var relative = Path.GetRelativePath(repoPath, path);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => SkippedFolders.Contains(p));
    }

    private static bool IsBinaryFile(string filePath)
    {
        try
        {
            const int sampleSize = 8000;
            using var stream = File.OpenRead(filePath);
            var buffer = new byte[Math.Min(sampleSize, stream.Length)];
            int read = stream.Read(buffer, 0, buffer.Length);
            return Array.IndexOf(buffer, (byte)0, 0, read) >= 0;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsCommentLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("//")   // line comment and XML doc comment
            || trimmed.StartsWith("*")    // inside block comment
            || trimmed.StartsWith("/*")   // block comment start
            || trimmed.StartsWith("<!--") // XML/HTML comment
            || trimmed.StartsWith("#");   // preprocessor / script comment
    }

    private static bool IsNamespaceOrInjectLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("using ")      // C# using directive
            || trimmed.StartsWith("namespace ")  // C# namespace declaration
            || trimmed.StartsWith("@using ")     // Razor @using directive
            || trimmed.StartsWith("@inject ")    // Razor @inject directive
            || trimmed.Contains("xmlns=")        // XML/SVG/XAML namespace attribute
            || trimmed.Contains("xmlns:");       // XML/SVG/XAML prefixed namespace attribute
    }

    private static readonly Regex XmlnsAttributePattern = new(@"xmlns(:\w+)?\s*=\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CssClassSelectorPattern = new(@"\.[a-zA-Z0-9_-]+", RegexOptions.Compiled);
    private static readonly Regex HtmlClassAttributePattern = new(@"class\s*=\s*['""'][^'""']*['""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Returns true if the match is inside a CSS class selector or HTML class attribute
    private static bool IsCssClassContext(string line, int matchIndex)
    {
        // CSS selector: .my-class { ... }
        var selectorMatches = CssClassSelectorPattern.Matches(line);
        foreach (Match m in selectorMatches)
        {
            if (matchIndex >= m.Index && matchIndex < m.Index + m.Length)
                return true;
        }

        // HTML class attribute: class="my-class"
        var attrMatches = HtmlClassAttributePattern.Matches(line);
        foreach (Match m in attrMatches)
        {
            int valueStart = m.Value.IndexOf('=') + 1;
            if (valueStart > 0)
            {
                // Find the start and end of the quoted value
                char quote = m.Value[valueStart];
                int start = m.Index + valueStart + 1;
                int end = m.Index + m.Value.Length - 1;
                if (matchIndex >= start && matchIndex < end)
                    return true;
            }
        }
        return false;
    }

    private static bool IsUrlContext(string line, int matchIndex)
    {
        // Find the start of the URL token (walk back to whitespace or quote)
        int start = matchIndex;
        while (start > 0 && line[start - 1] != ' ' && line[start - 1] != '"' && line[start - 1] != '\'') start--;

        if (!line[start..].StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !line[start..].StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;

        // Extract just the URL — stop at whitespace, quote, or closing bracket
        int end = start;
        while (end < line.Length && line[end] != ' ' && line[end] != '"' && line[end] != '\'' && line[end] != '>') end++;
        var url = line[start..end];

        // xmlns attributes are always harmless namespace identifiers — suppress unconditionally
        // e.g. xmlns="http://..." or xmlns:xlink="http://..."
        int quotePos = start - 1;
        if (quotePos >= 0 && (line[quotePos] == '"' || line[quotePos] == '\''))
        {
            var beforeQuote = line[..quotePos].TrimEnd();
            if (XmlnsAttributePattern.IsMatch(beforeQuote))
                return true;
        }

        // Never suppress other URLs with query strings or parameters — namespace URIs don't have them,
        // but a malicious endpoint could use a trusted prefix to hide credential exfiltration
        if (url.Contains('?') || url.Contains('='))
            return false;

        // Only suppress if the URL starts with a whitelisted prefix
        return _whitelistedUrls.Any(u => url.StartsWith(u, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasHardcodedStringValue(string line, int matchIndex)
    {
        // Look for = or , after the keyword, then check if the value is a quoted string literal
        int searchFrom = matchIndex;
        int eqOrComma = -1;
        char separator = ' ';

        for (int i = searchFrom; i < line.Length; i++)
        {
            if (line[i] == '=' || line[i] == ',')
            {
                eqOrComma = i;
                separator = line[i];
                break;
            }
        }

        if (eqOrComma < 0) return false;

        int valueStart = eqOrComma + 1;
        while (valueStart < line.Length && line[valueStart] == ' ') valueStart++;

        if (valueStart >= line.Length) return false;

        // Value must start with a quote to be a hardcoded string literal
        return line[valueStart] == '"' || line[valueStart] == '\'';
    }

    private static bool IsHtmlTextContent(string line, int matchIndex)
    {
        // Find the nearest < and > around the match position
        int lastClose = line.LastIndexOf('>', matchIndex);
        int nextOpen  = line.IndexOf('<', matchIndex);

        // Match is between >...< meaning it's display text, not an attribute or code
        return lastClose >= 0 && (nextOpen < 0 || nextOpen > matchIndex) && lastClose < matchIndex;
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '-';

    private static readonly Regex DeclarationPattern = new(
        @"(?:var|string|int|bool|object|dynamic|char|byte|short|long|float|double|decimal)" +
        @"|(?:public|private|protected|internal|static|readonly|const)\s+\w+\s+\w+\s*[{(;,]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string ClassifyLine(string line, string keyword)
    {
        var trimmed = line.TrimStart();

        // Property auto-property: public string ApiKey { get; set; } ? exclude
        if (Regex.IsMatch(trimmed, @"\{\s*get\s*[;{]", RegexOptions.IgnoreCase))
            return "Declaration";

        // Declaration WITHOUT assignment: public string ApiKey; / private string _pwd; ? exclude
        if (DeclarationPattern.IsMatch(trimmed) && !trimmed.Contains('='))
            return "Declaration";

        // Declaration WITH assignment or plain assignment ? include as Content
        return "Content";
    }

    private static string ExtractFullWord(string line, int matchIndex)
    {
        int start = matchIndex;
        while (start > 0 && IsWordChar(line[start - 1])) start--;

        int end = matchIndex;
        while (end < line.Length && IsWordChar(line[end])) end++;

        return line[start..end];
    }

    private static bool IsWholeWordMatch(string text, string keyword)
    {
        int index = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            bool startOk = index == 0 || !IsWordChar(text[index - 1]);
            bool endOk = index + keyword.Length == text.Length || !IsWordChar(text[index + keyword.Length]);
            if (startOk && endOk) return true;
            index = text.IndexOf(keyword, index + 1, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static int WholeWordIndex(string text, string keyword)
    {
        int index = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            bool startOk = index == 0 || !IsWordChar(text[index - 1]);
            bool endOk = index + keyword.Length == text.Length || !IsWordChar(text[index + keyword.Length]);
            if (startOk && endOk) return index;
            index = text.IndexOf(keyword, index + 1, StringComparison.OrdinalIgnoreCase);
        }
        return -1;
    }

    private static readonly Dictionary<string, Dictionary<int, string>> BlameCache = new();

    private static Dictionary<int, string> GetBlameForFile(string repoPath, string filePath)
    {
        if (BlameCache.TryGetValue(filePath, out var cached)) return cached;

        var lineCommits = new Dictionary<int, string>();
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                Arguments = $"-C \"{repoPath}\" blame --porcelain -- \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            string? line;
            int lineNumber = 0;
            string? currentCommit = null;
            while ((line = process.StandardOutput.ReadLine()) != null)
            {
                // Porcelain format: lines starting with 40-char hex are commit headers
                if (line.Length >= 40 && line[..40].All(c => "0123456789abcdef".Contains(c)))
                {
                    var parts = line.Split(' ');
                    if (parts.Length >= 3 && int.TryParse(parts[2], out int ln))
                    {
                        lineNumber = ln;
                        currentCommit = parts[0];
                    }
                }
                else if (line.StartsWith('\t') && currentCommit != null)
                {
                    lineCommits[lineNumber] = currentCommit;
                }
            }
            process.WaitForExit();
        }
        catch { }

        BlameCache[filePath] = lineCommits;
        return lineCommits;
    }

    public static int CountFiles(string repoPath) =>
        Directory.EnumerateFileSystemEntries(repoPath, "*", SearchOption.AllDirectories)
            .Count(e => !IsInSkippedFolder(e, repoPath));

    public static (List<ScanMatch> Matches, List<string> Skipped, int ScannedFiles) Scan(string repoPath, List<string> keywords, Action<int, int, string>? onProgress = null)
    {
        var matches = new List<ScanMatch>();
        var skipped = new List<string>();

        var allEntries = Directory.EnumerateFileSystemEntries(repoPath, "*", SearchOption.AllDirectories)
            .Where(e => !IsInSkippedFolder(e, repoPath))
            .ToList();

        int total = allEntries.Count;
        int current = 0;
        int scannedFiles = 0;

        foreach (var entry in allEntries)
        {
            current++;
            onProgress?.Invoke(current, total, Path.GetFileName(entry));
            var name = Path.GetFileName(entry);

            // Match on folder/file name (skip known infrastructure names like wwwroot)
            foreach (var keyword in keywords)
            {
                if (!SkippedNameMatches.Contains(name) && name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    matches.Add(new ScanMatch(keyword, entry, "Name", null, null));
            }

            // Skip directories for content scanning
            if (Directory.Exists(entry)) continue;

            if (IsBinaryFile(entry)) continue;

            scannedFiles++;

            // Content scan
            try
            {
                var lines = File.ReadAllText(entry).Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
                var blameMap = GetBlameForFile(repoPath, entry);
                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (var keyword in keywords)
                    {
                        if (IsCommentLine(lines[i])) continue;
                        if (lines[i].Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        {
                            blameMap.TryGetValue(i + 1, out var blameCommit);
                            int matchIndex = lines[i].IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                            string fullWord = ExtractFullWord(lines[i], matchIndex);

                            if (CSharpKeywords.IsKeyword(fullWord)) continue;
                            if (IsNamespaceOrInjectLine(lines[i])) continue;
                            if (IsCssClassContext(lines[i], matchIndex)) continue;
                            if (IsUrlContext(lines[i], matchIndex)) continue;
                            if (IsHtmlTextContent(lines[i], matchIndex)) continue;
                            if (PlaceholderDetector.IsPlaceholder(lines[i], keyword, matchIndex)) continue;
                            var matchType = ClassifyLine(lines[i], keyword);
                            matches.Add(new ScanMatch(
                                keyword,
                                entry,
                                matchType,
                                i + 1,
                                null,
                                blameCommit
                            ));
                        }
                    }
                }
            }
            catch
            {
                skipped.Add(entry);
            }
        }

        var deduped = matches
            .Where(m => m.MatchType != "Declaration")
            .DistinctBy(m => (m.Keyword, m.FilePath, m.MatchType, m.LineNumber))
            .ToList();
        return (Matches: deduped, Skipped: skipped, ScannedFiles: scannedFiles);
    }
}

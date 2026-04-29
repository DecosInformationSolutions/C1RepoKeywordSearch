namespace C1RepoKeywordSearch;

public static class ScanLogger
{
    public static string Write(
        string repoPath,
        string outputFolder,
        GitInfo? gitInfo,
        List<string> keywords,
        List<ScanMatch> matches,
        List<string> skipped,
        TimeSpan duration,
        int scannedFiles)
    {
        var timestamp = DateTime.UtcNow;
        var repoName = Path.GetFileName(repoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var logFileName = $"{timestamp:yyyy-MM-dd_HH-mm-ss}_{repoName}_scan.log";
        var logPath = Path.Combine(outputFolder, logFileName);

        using var writer = new StreamWriter(logPath);

        writer.WriteLine("=== C1 Solution Search - Scan Report ===");
        writer.WriteLine($"Timestamp     : {timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        writer.WriteLine($"Repo Path     : {repoPath}");
        writer.WriteLine($"Commit ID     : {gitInfo?.CommitId ?? "N/A"}");
        writer.WriteLine($"Commit Date   : {gitInfo?.Date ?? "N/A"}");
        writer.WriteLine($"Scan Duration : {duration.TotalSeconds:F2}s");
        writer.WriteLine($"Files Scanned : {scannedFiles:N0}");
        writer.WriteLine($"Keywords      : {string.Join(", ", keywords)}");
        writer.WriteLine();

        // Summary per keyword (only keywords with more than 1 match)
        writer.WriteLine("--- Match Summary (keywords with >1 match) ---");
        foreach (var keyword in keywords)
        {
            int count = matches.Count(m => m.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase));
            if (count > 1)
                writer.WriteLine($"  {keyword}: {count} match(es)");
        }
        writer.WriteLine($"  TOTAL: {matches.Count} match(es)");
        writer.WriteLine();

        // File summary - grouped by file then line
        var qualifiedKeywords = matches
            .GroupBy(m => m.Keyword, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var byFile = matches
            .Where(m => qualifiedKeywords.Contains(m.Keyword))
            .GroupBy(m => m.FilePath);

        writer.WriteLine("--- File Summary ---");
        foreach (var fileGroup in byFile)
        {
            writer.WriteLine($"  {fileGroup.Key}");
            foreach (var lineGroup in fileGroup.GroupBy(m => m.LineNumber))
            {
                var location = lineGroup.Key.HasValue ? $"Line {lineGroup.Key}" : "Name";
                var matchedKws = string.Join(", ", lineGroup.Select(m => $"\"{m.Keyword}\"").Distinct());
                var type = lineGroup.First().MatchType.ToUpper();
                writer.WriteLine($"    [{type}] {matchedKws} | {location}");
            }
        }
        writer.WriteLine();

        // Detailed matches - grouped by file + line
        writer.WriteLine("--- Matches ---");
        foreach (var fileGroup in byFile)
        {
            foreach (var lineGroup in fileGroup.GroupBy(m => m.LineNumber))
            {
                var first = lineGroup.First();
                var matchedKws = string.Join(", ", lineGroup.Select(m => $"\"{m.Keyword}\"").Distinct());
                if (first.MatchType == "Name")
                {
                    writer.WriteLine($"[NAME]    Keywords: {matchedKws} | Location: {first.FilePath}");
                }
                else
                {
                    writer.WriteLine($"[{first.MatchType.ToUpper()}] Keywords: {matchedKws} | Location: {first.FilePath} | Line: {first.LineNumber}");
                    writer.WriteLine($"          Introduced: {first.BlameCommit ?? "N/A"}");
                }
            }
        }
        writer.WriteLine();

        // Skipped files
        if (skipped.Count > 0)
        {
            writer.WriteLine("--- Skipped Files ---");
            foreach (var s in skipped)
                writer.WriteLine($"  {s}");
            writer.WriteLine();
        }

        writer.WriteLine("=== End of Report ===");

        return logPath;
    }
}

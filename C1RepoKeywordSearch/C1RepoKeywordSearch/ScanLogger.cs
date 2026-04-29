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

        if (!Directory.Exists(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
        }

        using var writer = new StreamWriter(logPath);

        writer.WriteLine("=== C1 Repo Keyword Search - Scan Report ===");
        writer.WriteLine($"Timestamp     : {timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        writer.WriteLine($"Repo Path     : {repoPath}");
        writer.WriteLine($"Latest Commit : {gitInfo?.CommitId ?? "N/A"} ({gitInfo?.Date ?? "N/A"})");
        writer.WriteLine($"First Commit  : {gitInfo?.CommitId ?? "N/A"} ({gitInfo?.Date ?? "N/A"})");
        writer.WriteLine($"Scan Duration : {duration.TotalSeconds:F2}s");
        writer.WriteLine($"Files Scanned : {scannedFiles:N0}");
        writer.WriteLine($"Keywords      : {string.Join(", ", keywords)}");
        writer.WriteLine();

        // Group matches by file and keyword, and write to the log file
        if (matches.Count > 0)
        {
            writer.WriteLine("--- Matches ---");
            foreach (var fileGroup in matches.GroupBy(m => m.FilePath))
            {
                writer.WriteLine(fileGroup.Key);
                foreach (var keywordGroup in fileGroup.GroupBy(m => m.Keyword))
                {
                    writer.WriteLine($"  [CONTENT] \"{keywordGroup.Key}\"");
                    foreach (var commitGroup in keywordGroup.GroupBy(m => m.BlameCommit))
                    {
                        writer.WriteLine($"    Commit: {commitGroup.Key ?? "N/A"}");
                        foreach (var match in commitGroup)
                        {
                            writer.WriteLine($"      Line {match.LineNumber}");
                        }
                    }
                }
            }
        }

        writer.WriteLine("\n=== End of Report ===");
        return logPath;
    }
}

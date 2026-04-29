using System.Diagnostics;

namespace C1RepoKeywordSearch;

public record GitInfo(string CommitId, string Date);

public static class GitInfoLoader
{
    public static GitInfo? TryLoad(string repoPath)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                Arguments = $"-C \"{repoPath}\" log -1 --format=%H|%ci",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (string.IsNullOrWhiteSpace(output)) return null;

            var parts = output.Split('|');
            return new GitInfo(parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : string.Empty);
        }
        catch
        {
            return null;
        }
    }

    public static GitInfo? TryLoadFirstCommit(string repoPath)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                Arguments = $"-C \"{repoPath}\" log --reverse --format=%H|%ci | head -1",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (string.IsNullOrWhiteSpace(output)) return null;

            var parts = output.Split('|');
            return new GitInfo(parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : string.Empty);
        }
        catch
        {
            return null;
        }
    }
}

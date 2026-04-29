// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using C1RepoKeywordSearch;

try
{
    Console.WriteLine("=== C1 Repo Keyword Search ===");
    Console.WriteLine();

    string? repoPath = null;
    while (true)
    {
        if (string.IsNullOrWhiteSpace(repoPath))
        {
            // Prompt for repo root
            Console.Write("Enter repo root folder path: ");
            repoPath = Console.ReadLine()?.Trim().Trim('"');
        }

        if (string.IsNullOrWhiteSpace(repoPath))
        {
            Console.WriteLine("Exiting. Goodbye!");
            break;
        }

        if (!Directory.Exists(repoPath))
        {
            Console.WriteLine("ERROR: Repo path does not exist.");
            repoPath = null;
            continue;
        }

        var gitInfo = GitInfoLoader.TryLoad(repoPath);
        var firstCommit = GitInfoLoader.TryLoadFirstCommit(repoPath);
        if (gitInfo != null)
            Console.WriteLine($"Latest Commit : {gitInfo.CommitId} ({gitInfo.Date})");
        else
            Console.WriteLine("Latest Commit : Not available");
        if (firstCommit != null)
            Console.WriteLine($"First Commit  : {firstCommit.CommitId} ({firstCommit.Date})");
        else
            Console.WriteLine("First Commit  : Not available");

        Console.WriteLine($"Repo Path     : {repoPath}");

        KeywordConfig keywordConfig = KeywordLoader.Load();
        var keywords = keywordConfig.Keywords;
        Scanner.LoadWhitelistedUrls(keywordConfig.WhitelistedUrls);

        // Restore interactive output folder selection logic
        string outputFolder;
        if (keywordConfig.OutputFolder != null)
        {
            outputFolder = keywordConfig.OutputFolder;
            Console.WriteLine($"Output folder : {outputFolder} (from appsettings.json)");
        }
        else
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var desktopLogFolder = Path.Combine(desktopPath, "C1ScanResults");

            if (Directory.Exists(desktopLogFolder))
            {
                outputFolder = desktopLogFolder;
                Console.WriteLine($"Output folder : {outputFolder}");
            }
            else
            {
                Console.Write($"Create output folder on Desktop? \"{desktopLogFolder}\" [Y/n]: ");
                var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (answer is "" or "y" or "yes")
                {
                    Directory.CreateDirectory(desktopLogFolder);
                    outputFolder = desktopLogFolder;
                    Console.WriteLine($"Output folder : {outputFolder} (created)");
                }
                else
                {
                    // Manual path prompt with validation
                    string? manualFolder = null;
                    while (true)
                    {
                        Console.Write("Enter output folder path (or press Enter to use repo folder as fallback): ");
                        var input = Console.ReadLine()?.Trim().Trim('"');
                        if (string.IsNullOrWhiteSpace(input))
                            break;
                        if (Directory.Exists(input))
                        {
                            manualFolder = input;
                            break;
                        }
                        Console.WriteLine($"ERROR: Folder does not exist: {input}");
                    }

                    if (manualFolder != null)
                    {
                        outputFolder = manualFolder;
                        Console.WriteLine($"Output folder : {outputFolder}");
                    }
                    else
                    {
                        outputFolder = repoPath;
                        Console.WriteLine($"Output folder : {outputFolder} (repo folder fallback)");
                    }
                }
            }
        }

        int fileCount = Scanner.CountFiles(repoPath);
        Console.WriteLine($"Files to scan: {fileCount:N0}");
        int progressRow = Console.CursorTop;
        Console.WriteLine();

        var sw = Stopwatch.StartNew();
        var (matches, skipped, scannedFiles) = Scanner.Scan(repoPath, keywords, (current, total, fileName) =>
        {
            Console.SetCursorPosition(0, progressRow);
            string truncated = fileName.Length > 50 ? "..." + fileName[^47..] : fileName.PadRight(50);
            Console.Write($"[{total - current} remaining] {truncated}");
        });
        sw.Stop();

        Console.SetCursorPosition(0, progressRow);
        Console.WriteLine(new string(' ', Console.WindowWidth - 1));

        Console.WriteLine("Scan complete.");

        string logPath = ScanLogger.Write(repoPath, outputFolder, gitInfo, keywords, matches, skipped, sw.Elapsed, scannedFiles);
        Console.WriteLine($"Log written to: {logPath}");

        Console.WriteLine();
        Console.Write("Scan a different folder? Enter path or press Enter to exit: ");
        var next = Console.ReadLine()?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(next))
        {
            Console.WriteLine("Exiting. Goodbye!");
            break;
        }
        else
        {
            repoPath = next;
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Unhandled Exception: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}


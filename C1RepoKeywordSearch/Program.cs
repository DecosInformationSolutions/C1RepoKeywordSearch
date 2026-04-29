// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using C1RepoKeywordSearch;

Console.WriteLine("=== C1 Repo Keyword Search ===");
Console.WriteLine();

// Prompt for repo root
Console.Write("Enter repo root folder path: ");
string repoPath = Console.ReadLine()?.Trim().Trim('"') ?? string.Empty;

if (!Directory.Exists(repoPath))
{
	Console.WriteLine("ERROR: Repo path does not exist.");
	return;
}

KeywordConfig keywordConfig;
try
{
	keywordConfig = KeywordLoader.Load();
}
catch (Exception ex)
{
	Console.WriteLine($"ERROR: {ex.Message}");
	return;
}

var keywords = keywordConfig.Keywords;

if (keywords.Count == 0)
{
	Console.WriteLine("ERROR: No keywords found in file.");
	return;
}

// Resolve output folder: appsettings → desktop folder → manual path prompt → repo folder fallback
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

Console.WriteLine($"\nLoaded {keywords.Count} keyword(s). Starting scan...\n");

var gitInfo = GitInfoLoader.TryLoad(repoPath);
if (gitInfo != null)
	Console.WriteLine($"Git Commit : {gitInfo.CommitId} ({gitInfo.Date})");
else
	Console.WriteLine("Git info   : Not available");

int fileCount = Scanner.CountFiles(repoPath);
Console.WriteLine($"Files to scan: {fileCount:N0}");
Console.WriteLine();

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

Console.WriteLine($"Scan complete. {matches.Count} match(es) found in {sw.Elapsed.TotalSeconds:F2}s.");

// Print qualified matches to console (keywords with >1 match)
var qualifiedKeywords = matches
	.GroupBy(m => m.Keyword, StringComparer.OrdinalIgnoreCase)
	.Where(g => g.Count() > 1)
	.Select(g => g.Key)
	.ToHashSet(StringComparer.OrdinalIgnoreCase);

if (qualifiedKeywords.Count > 0)
{
	Console.WriteLine("\n--- File Summary ---");
	foreach (var fileGroup in matches.Where(m => qualifiedKeywords.Contains(m.Keyword)).GroupBy(m => m.FilePath))
	{
		Console.WriteLine($"  {fileGroup.Key}");
		foreach (var lineGroup in fileGroup.GroupBy(m => m.LineNumber))
		{
			var location = lineGroup.Key.HasValue ? $"Line {lineGroup.Key}" : "Name";
			var kws = string.Join(", ", lineGroup.Select(m => $"\"{m.Keyword}\"").Distinct());
			Console.WriteLine($"    [{lineGroup.First().MatchType.ToUpper()}] {kws} | {location}");
		}
	}

	Console.WriteLine("\n--- Matches ---");
	foreach (var fileGroup in matches.Where(m => qualifiedKeywords.Contains(m.Keyword)).GroupBy(m => m.FilePath))
	{
		foreach (var lineGroup in fileGroup.GroupBy(m => m.LineNumber))
		{
			var first = lineGroup.First();
			var kws = string.Join(", ", lineGroup.Select(m => $"\"{m.Keyword}\"").Distinct());
			if (first.MatchType == "Name")
				Console.WriteLine($"[NAME]    {kws} | {first.FilePath}");
			else
				Console.WriteLine($"[{first.MatchType.ToUpper()}] {kws} | {first.FilePath} | Line: {first.LineNumber} | Introduced: {first.BlameCommit ?? "N/A"}");
		}
	}
}

string logPath = ScanLogger.Write(repoPath, outputFolder, gitInfo, keywords, matches, skipped, sw.Elapsed, scannedFiles);
Console.WriteLine($"\nLog written to: {logPath}");


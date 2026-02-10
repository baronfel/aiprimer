using Spectre.Console;
using Primer.Services;

namespace Primer.Utils;

/// <summary>
/// Logging and console output utilities using Spectre.Console.
/// </summary>
public static class Logger
{
    /// <summary>
    /// Prints a formatted summary of repository analysis results.
    /// </summary>
    /// <param name="analysis">The repository analysis to display.</param>
    public static void PrettyPrintSummary(RepoAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        AnsiConsole.MarkupLine("[bold]Repository analysis[/]");
        AnsiConsole.MarkupLine($"- Path: {Markup.Escape(analysis.Path)}");
        AnsiConsole.MarkupLine($"- Git: {(analysis.IsGitRepo ? "yes" : "no")}");

        var languages = analysis.Languages.Count > 0
            ? string.Join(", ", analysis.Languages)
            : "unknown";
        AnsiConsole.MarkupLine($"- Languages: {Markup.Escape(languages)}");

        var frameworks = analysis.Frameworks.Count > 0
            ? string.Join(", ", analysis.Frameworks)
            : "none";
        AnsiConsole.MarkupLine($"- Frameworks: {Markup.Escape(frameworks)}");

        var packageManager = analysis.PackageManager ?? "unknown";
        AnsiConsole.MarkupLine($"- Package manager: {Markup.Escape(packageManager)}");

        var testing = analysis.TestingFrameworks.Count > 0
            ? string.Join(", ", analysis.TestingFrameworks)
            : "none";
        AnsiConsole.MarkupLine($"- Testing: {Markup.Escape(testing)}");

        var cicd = analysis.CiCdTools.Count > 0
            ? string.Join(", ", analysis.CiCdTools)
            : "none";
        AnsiConsole.MarkupLine($"- CI/CD: {Markup.Escape(cicd)}");

        AnsiConsole.MarkupLine($"- Docker: {(analysis.HasDocker ? "yes" : "no")}");

        if (analysis.FileStats.CountsByCategory.Count > 0)
        {
            AnsiConsole.MarkupLine($"- Files: {analysis.FileStats.TotalFiles} source files");
            var topCategories = analysis.FileStats.CountsByCategory
                .OrderByDescending(kvp => kvp.Value)
                .Take(5)
                .Select(kvp => $"{kvp.Key} ({kvp.Value})");
            AnsiConsole.MarkupLine($"  Top: {Markup.Escape(string.Join(", ", topCategories))}");
        }

        if (analysis.KeyDirectories.Count > 0)
        {
            AnsiConsole.MarkupLine($"- Key dirs: {Markup.Escape(string.Join(", ", analysis.KeyDirectories.Take(8).Select(d => d.Path)))}");
        }
    }
}

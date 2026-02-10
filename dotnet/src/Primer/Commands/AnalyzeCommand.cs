using System.CommandLine;
using System.Text.Json;
using Primer.Services;
using Primer.Utils;

namespace Primer.Commands;

/// <summary>
/// Command for analyzing a repository to detect languages, frameworks, and package managers.
/// </summary>
internal static class AnalyzeCommand
{
    /// <summary>
    /// Builds the analyze command.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build()
    {
        var command = new Command("analyze", "Analyze a repository to detect languages, frameworks, and package managers");

        var pathArgument = new Argument<string?>("path")
        {
            Description = "Path to the repository (defaults to current directory)",
            DefaultValueFactory = _ => null
        };

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Output results in JSON format",
            DefaultValueFactory = _ => false
        };

        command.Arguments.Add(pathArgument);
        command.Options.Add(jsonOption);

        command.SetAction(parseResult =>
        {
            try
            {
                var repoPathArg = parseResult.GetValue(pathArgument);
                var json = parseResult.GetValue(jsonOption);

                var repoPath = Path.GetFullPath(repoPathArg ?? Directory.GetCurrentDirectory());
                var analysis = AnalyzerService.AnalyzeRepo(repoPath);

                if (json)
                {
                    var jsonOutput = JsonSerializer.Serialize(analysis, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    Console.WriteLine(jsonOutput);
                }
                else
                {
                    Logger.PrettyPrintSummary(analysis);
                }
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error analyzing repository: {ex.Message}");
                return 1;
            }

            return 0;
        });

        return command;
    }
}

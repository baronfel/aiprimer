using System.CommandLine;
using Primer.Services;

namespace Primer.Commands;

/// <summary>
/// Command for generating configuration files (MCP server config, VS Code settings).
/// </summary>
internal static class GenerateCommand
{
    private static readonly HashSet<string> AllowedTypes = ["mcp", "vscode"];

    /// <summary>
    /// Builds the generate command.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build()
    {
        var command = new Command("generate", "Generate configuration files for the repository");

        var typeArgument = new Argument<string>("type")
        {
            Description = "Type of configuration to generate (mcp, vscode)"
        };

        var pathArgument = new Argument<string?>("path")
        {
            Description = "Path to the repository (defaults to current directory)",
            DefaultValueFactory = _ => null
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Force overwrite existing files",
            DefaultValueFactory = _ => false
        };

        command.Arguments.Add(typeArgument);
        command.Arguments.Add(pathArgument);
        command.Options.Add(forceOption);

        command.SetAction(async parseResult =>
        {
            try
            {
                var type = parseResult.GetValue(typeArgument)!;
                var repoPathArg = parseResult.GetValue(pathArgument);
                var force = parseResult.GetValue(forceOption);

                if (!AllowedTypes.Contains(type))
                {
                    Console.Error.WriteLine($"Error: Invalid type '{type}'. Use: mcp, vscode.");
                    return 1;
                }

                var repoPath = Path.GetFullPath(repoPathArg ?? Directory.GetCurrentDirectory());
                var analysis = AnalyzerService.AnalyzeRepo(repoPath);

                var options = new GenerateOptions(
                    RepoPath: repoPath,
                    Analysis: analysis,
                    Selections: [type],
                    Force: force
                );

                var result = await GeneratorService.GenerateConfigsAsync(options);
                Console.WriteLine(result);
                return 0;
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error generating configuration: {ex.Message}");
                return 1;
            }
        });

        return command;
    }
}

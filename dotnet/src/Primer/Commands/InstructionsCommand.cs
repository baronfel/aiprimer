using System.CommandLine;
using Primer.Services;
using Primer.Utils;

namespace Primer.Commands;

/// <summary>
/// Command for generating GitHub Copilot instructions.
/// </summary>
internal static class InstructionsCommand
{
    /// <summary>
    /// Builds the instructions command.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build()
    {
        var command = new Command("instructions", "Generate GitHub Copilot instructions for a repository");

        // Option for repository path
        var repoOption = new Option<string?>("--repo")
        {
            Description = "Path to the repository (defaults to current directory)",
            DefaultValueFactory = _ => null
        };

        // Option for output path
        var outputOption = new Option<string?>("--output")
        {
            Description = "Path to save the generated instructions file",
            DefaultValueFactory = _ => null
        };

        // Option for model
        var modelOption = new Option<string>("--model")
        {
            Description = "Model to use for generating instructions",
            DefaultValueFactory = _ => "gpt-4.1"
        };

        command.Options.Add(repoOption);
        command.Options.Add(outputOption);
        command.Options.Add(modelOption);

        command.SetAction(parseResult =>
        {
            try
            {
                // Get values from parseResult
                var repoPathArg = parseResult.GetValue(repoOption);
                var outputArg = parseResult.GetValue(outputOption);
                var model = parseResult.GetValue(modelOption)!;

                // Resolve repository path
                var repoPath = Path.GetFullPath(repoPathArg ?? Directory.GetCurrentDirectory());

                if (!Directory.Exists(repoPath))
                {
                    Console.Error.WriteLine($"Error: Repository path not found: {repoPath}");
                    return 1;
                }

                // Resolve output path (default to {repoPath}/.github/copilot-instructions.md)
                var outputPath = outputArg != null
                    ? Path.GetFullPath(outputArg)
                    : Path.Combine(repoPath, ".github", "copilot-instructions.md");

                // Check if Copilot CLI is available (synchronously wait for async operation)
                try
                {
                    InstructionsService.AssertCopilotCliReadyAsync().GetAwaiter().GetResult();
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Please ensure you have:");
                    Console.Error.WriteLine("  1. GitHub Copilot CLI installed and in your PATH");
                    Console.Error.WriteLine("  2. Authenticated with GitHub Copilot (run 'copilot auth')");
                    return 1;
                }

                // Generate instructions (synchronously wait for async operation)
                Console.WriteLine("Generating Copilot instructions...");
                var options = new GenerateInstructionsOptions(
                    RepoPath: repoPath,
                    Model: model,
                    OnProgress: msg => Console.WriteLine(msg)
                );

                var content = InstructionsService.GenerateCopilotInstructionsAsync(options).GetAwaiter().GetResult();

                if (string.IsNullOrWhiteSpace(content))
                {
                    Console.Error.WriteLine("Error: Failed to generate instructions (empty content).");
                    return 1;
                }

                // Ensure output directory exists
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir))
                {
                    FileUtils.EnsureDir(outputDir);
                }

                // Write to output path
                File.WriteAllText(outputPath, content);

                // Print relative path
                var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), outputPath);
                Console.WriteLine();
                Console.WriteLine($"✓ Generated instructions: {relativePath}");

                return 0;
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error generating instructions: {ex.Message}");
                return 1;
            }
        });

        return command;
    }
}

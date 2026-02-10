using System.CommandLine;
using Primer.Services;
using Primer.Utils;
using Spectre.Console;

namespace Primer.Commands;

/// <summary>
/// Command for initializing a repository with Primer configurations.
/// </summary>
public static class InitCommand
{
    /// <summary>
    /// Builds the init command with all options and arguments.
    /// </summary>
    /// <returns>Configured Command instance.</returns>
    public static Command Build()
    {
        var command = new Command("init", "Initialize a repository with GitHub Copilot configurations");

        var pathArgument = new Argument<string?>("path")
        {
            Description = "Path to the repository (defaults to current directory)",
            DefaultValueFactory = _ => null
        };

        var githubOption = new Option<bool>("--github")
        {
            Description = "Select a repository from GitHub",
            DefaultValueFactory = _ => false
        };

        var yesOption = new Option<bool>("--yes")
        {
            Description = "Automatically select all configuration options",
            DefaultValueFactory = _ => false
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite existing configuration files",
            DefaultValueFactory = _ => false
        };

        command.Arguments.Add(pathArgument);
        command.Options.Add(githubOption);
        command.Options.Add(yesOption);
        command.Options.Add(forceOption);

        command.SetAction(async parseResult =>
        {
            var path = parseResult.GetValue(pathArgument);
            var github = parseResult.GetValue(githubOption);
            var yes = parseResult.GetValue(yesOption);
            var force = parseResult.GetValue(forceOption);
            return await ExecuteAsync(path, github, yes, force);
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        string? path,
        bool github,
        bool yes,
        bool force)
    {
        try
        {
            string repoPath;

            if (github)
            {
                repoPath = await HandleGitHubSelectionAsync();
            }
            else
            {
                repoPath = path ?? Directory.GetCurrentDirectory();
                if (!Directory.Exists(repoPath))
                {
                    AnsiConsole.MarkupLine($"[red]Error: Directory not found: {repoPath}[/]");
                    return 1;
                }
            }

            // Analyze the repository
            AnsiConsole.MarkupLine("[bold]Analyzing repository...[/]");
            var analysis = AnalyzerService.AnalyzeRepo(repoPath);
            Logger.PrettyPrintSummary(analysis);
            AnsiConsole.WriteLine();

            // Determine which configurations to generate
            var selections = new List<string>();

            if (yes)
            {
                // Default to all options
                selections.AddRange(["instructions", "mcp", "vscode"]);
                AnsiConsole.MarkupLine("[yellow]Generating all configurations (--yes flag)[/]");
            }
            else
            {
                // Use interactive selection
                var choices = AnsiConsole.Prompt(
                    new MultiSelectionPrompt<string>()
                        .Title("What would you like to [green]generate[/]?")
                        .Required()
                        .PageSize(10)
                        .InstructionsText(
                            "[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                        .AddChoices(new[]
                        {
                            "instructions",
                            "mcp",
                            "vscode"
                        }));

                selections.AddRange(choices);
            }

            // Generate instructions if selected
            if (selections.Contains("instructions"))
            {
                await GenerateInstructionsAsync(repoPath);
            }

            // Generate other configurations
            var otherSelections = selections.Where(s => s != "instructions").ToList();
            if (otherSelections.Count > 0)
            {
                var generateOptions = new GenerateOptions(
                    RepoPath: repoPath,
                    Analysis: analysis,
                    Selections: otherSelections,
                    Force: force);

                var summary = await GeneratorService.GenerateConfigsAsync(generateOptions);
                AnsiConsole.WriteLine(summary);
            }

            AnsiConsole.MarkupLine("[green]✓ Initialization complete![/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static async Task<string> HandleGitHubSelectionAsync()
    {
        AnsiConsole.MarkupLine("[yellow]Fetching repositories from GitHub...[/]");

        var token = await GitHubService.GetGitHubTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "GitHub token not found. Please set GITHUB_TOKEN or GH_TOKEN environment variable, " +
                "or authenticate with 'gh auth login'.");
        }

        var repos = await GitHubService.ListAccessibleReposAsync(token, limit: 100);
        if (repos.Length == 0)
        {
            throw new InvalidOperationException("No repositories found.");
        }

        var selectedRepo = AnsiConsole.Prompt(
            new SelectionPrompt<GitHubRepo>()
                .Title("Select a [green]repository[/]:")
                .PageSize(15)
                .MoreChoicesText("[grey](Move up and down to reveal more repositories)[/]")
                .AddChoices(repos)
                .UseConverter(repo => $"{repo.FullName} {(repo.IsPrivate ? "[grey](private)[/]" : "")}"));

        AnsiConsole.MarkupLine($"[yellow]Cloning {selectedRepo.FullName}...[/]");

        var localPath = Path.Combine(Directory.GetCurrentDirectory(), selectedRepo.Name);
        
        await GitService.CloneRepoAsync(
            repoUrl: selectedRepo.CloneUrl,
            destination: localPath,
            onProgress: (message, percentage) =>
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    AnsiConsole.MarkupLine($"[grey]{message}[/]");
                }
            });

        AnsiConsole.MarkupLine($"[green]✓ Cloned to {localPath}[/]");
        return localPath;
    }

    private static async Task GenerateInstructionsAsync(string repoPath)
    {
        AnsiConsole.MarkupLine("[yellow]Generating Copilot instructions...[/]");

        var options = new GenerateInstructionsOptions(
            RepoPath: repoPath,
            OnProgress: message => AnsiConsole.MarkupLine($"[grey]{message}[/]"));

        var instructions = await InstructionsService.GenerateCopilotInstructionsAsync(options);

        var instructionsPath = Path.Combine(repoPath, ".github", "copilot-instructions.md");
        FileUtils.EnsureDir(Path.GetDirectoryName(instructionsPath)!);
        await File.WriteAllTextAsync(instructionsPath, instructions);

        var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), instructionsPath);
        AnsiConsole.MarkupLine($"[green]✓ Wrote {relativePath}[/]");
    }
}

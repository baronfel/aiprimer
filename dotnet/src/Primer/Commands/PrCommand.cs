using System.CommandLine;
using Primer.Services;
using Primer.Utils;
using Spectre.Console;

namespace Primer.Commands;

/// <summary>
/// Command for creating a pull request with Primer configurations.
/// </summary>
public static class PrCommand
{
    /// <summary>
    /// Builds the pr command with all options and arguments.
    /// </summary>
    /// <returns>Configured Command instance.</returns>
    public static Command Build()
    {
        var command = new Command("pr", "Create a pull request with generated configurations");

        var repoArgument = new Argument<string?>("repo")
        {
            Description = "Repository in owner/name format (e.g., microsoft/primer)",
            DefaultValueFactory = _ => null
        };

        var branchOption = new Option<string>("--branch")
        {
            Description = "Branch name to create",
            DefaultValueFactory = _ => "primer/add-configs"
        };

        command.Arguments.Add(repoArgument);
        command.Options.Add(branchOption);

        command.SetAction(async parseResult =>
        {
            var repo = parseResult.GetValue(repoArgument);
            var branch = parseResult.GetValue(branchOption)!;
            return await ExecuteAsync(repo, branch);
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        string? repo,
        string branch)
    {
        try
        {
            // Validate token exists
            var token = await GitHubService.GetGitHubTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                AnsiConsole.MarkupLine(
                    "[red]Error: GitHub token not found. Please set GITHUB_TOKEN or GH_TOKEN environment variable, " +
                    "or authenticate with 'gh auth login'.[/]");
                return 1;
            }

            // Validate repo format
            if (string.IsNullOrWhiteSpace(repo))
            {
                AnsiConsole.MarkupLine("[red]Error: Repository argument is required (format: owner/name)[/]");
                return 1;
            }

            var repoParts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (repoParts.Length != 2)
            {
                AnsiConsole.MarkupLine($"[red]Error: Invalid repository format. Expected 'owner/name', got '{repo}'[/]");
                return 1;
            }

            var owner = repoParts[0];
            var repoName = repoParts[1];

            // Get repository info
            AnsiConsole.MarkupLine($"[yellow]Fetching repository information for {repo}...[/]");
            var repoInfo = await GitHubService.GetRepoAsync(token, owner, repoName);
            AnsiConsole.MarkupLine($"[green]✓ Found repository: {repoInfo.FullName}[/]");

            // Clone to .primer-cache
            var cacheDir = Path.Combine(Directory.GetCurrentDirectory(), ".primer-cache");
            var localPath = Path.Combine(cacheDir, repoName);

            // Clean up existing directory if it exists
            if (Directory.Exists(localPath))
            {
                AnsiConsole.MarkupLine($"[yellow]Removing existing cache directory...[/]");
                Directory.Delete(localPath, recursive: true);
            }

            AnsiConsole.MarkupLine($"[yellow]Cloning {repo} to {localPath}...[/]");
            await GitService.CloneRepoAsync(
                repoUrl: repoInfo.CloneUrl,
                destination: localPath,
                onProgress: (message, percentage) =>
                {
                    if (!string.IsNullOrWhiteSpace(message) && !message.Contains("Receiving objects"))
                    {
                        AnsiConsole.MarkupLine($"[grey]{message}[/]");
                    }
                });

            AnsiConsole.MarkupLine($"[green]✓ Cloned repository[/]");

            // Checkout branch
            AnsiConsole.MarkupLine($"[yellow]Creating and checking out branch '{branch}'...[/]");
            await GitService.CheckoutBranchAsync(localPath, branch);
            AnsiConsole.MarkupLine($"[green]✓ Checked out branch {branch}[/]");

            // Analyze repository
            AnsiConsole.MarkupLine("[yellow]Analyzing repository...[/]");
            var analysis = AnalyzerService.AnalyzeRepo(localPath);
            Logger.PrettyPrintSummary(analysis);
            AnsiConsole.WriteLine();

            // Generate configs with force=true
            AnsiConsole.MarkupLine("[yellow]Generating configurations...[/]");
            var generateOptions = new GenerateOptions(
                RepoPath: localPath,
                Analysis: analysis,
                Selections: ["mcp", "vscode"],
                Force: true);

            var summary = await GeneratorService.GenerateConfigsAsync(generateOptions);
            AnsiConsole.WriteLine(summary);

            // Commit changes
            AnsiConsole.MarkupLine("[yellow]Committing changes...[/]");
            await GitService.CommitAllAsync(
                repoPath: localPath,
                message: "Add Primer configurations\n\n- Add MCP server configuration\n- Add VS Code settings for GitHub Copilot");
            AnsiConsole.MarkupLine("[green]✓ Changes committed[/]");

            // Push branch
            AnsiConsole.MarkupLine($"[yellow]Pushing branch '{branch}' to origin...[/]");
            await GitService.PushBranchAsync(
                repoPath: localPath,
                branchName: branch,
                token: token);
            AnsiConsole.MarkupLine($"[green]✓ Branch pushed[/]");

            // Create pull request
            AnsiConsole.MarkupLine("[yellow]Creating pull request...[/]");
            var prUrl = await GitHubService.CreatePullRequestAsync(
                token: token,
                owner: owner,
                repo: repoName,
                title: "Add Primer configurations",
                body: @"This PR adds Primer configurations for GitHub Copilot:

- **MCP Server Configuration** (`.vscode/mcp.json`): Configures Model Context Protocol servers for enhanced Copilot functionality
- **VS Code Settings** (`.vscode/settings.json`): Enables GitHub Copilot with recommended settings

These configurations help maximize the effectiveness of GitHub Copilot in this repository.

---
*Generated by [Primer](https://github.com/microsoft/primer)*",
                head: branch,
                baseRef: repoInfo.DefaultBranch);

            AnsiConsole.MarkupLine($"[green]✓ Pull request created: {prUrl}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold]Pull Request URL:[/] [link]{prUrl}[/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            
            // Print stack trace in debug mode
            if (Environment.GetEnvironmentVariable("DEBUG") == "1")
            {
                AnsiConsole.WriteException(ex);
            }
            
            return 1;
        }
    }
}

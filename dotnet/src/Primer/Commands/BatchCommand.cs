using System.CommandLine;
using System.Text.Json;
using Primer.Services;
using Primer.Utils;
using Spectre.Console;

namespace Primer.Commands;

/// <summary>
/// Result of processing a single repository in batch mode.
/// </summary>
internal record BatchProcessResult(
    string Repo,
    bool Success,
    string? PrUrl = null,
    string? Error = null);

/// <summary>
/// Command for batch processing multiple repos across organizations.
/// </summary>
internal static class BatchCommand
{
    /// <summary>
    /// Builds the batch command.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build()
    {
        var command = new Command("batch", "Batch process multiple repos across orgs");

        var outputOption = new Option<string?>("--output")
        {
            Description = "Write results JSON to file"
        };

        command.Options.Add(outputOption);

        command.SetAction(async parseResult =>
        {
            var outputPath = parseResult.GetValue(outputOption);
            await ExecuteAsync(outputPath);
        });

        return command;
    }

    private static async Task ExecuteAsync(string? outputPath)
    {
        // Get GitHub token
        var token = await GitHubService.GetGitHubTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] GitHub authentication required.");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Option 1 (recommended):[/] Install and authenticate GitHub CLI");
            AnsiConsole.MarkupLine("  [dim]gh auth login[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Option 2:[/] Set a token environment variable");
            AnsiConsole.MarkupLine("  [dim]export GITHUB_TOKEN=<your-token>[/]");
            Environment.ExitCode = 1;
            return;
        }

        try
        {
            // Load organizations
            var orgs = await LoadOrganizationsAsync(token);
            if (orgs.Length == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No organizations found.[/]");
                return;
            }

            // Select organizations
            var selectedOrgs = SelectOrganizations(orgs);
            if (selectedOrgs.Length == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No organizations selected.[/]");
                return;
            }

            // Load repositories for selected organizations
            var repos = await LoadRepositoriesAsync(token, selectedOrgs);
            if (repos.Length == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No repositories found.[/]");
                return;
            }

            // Check which repos have instructions
            var reposWithStatus = await CheckRepositoriesForInstructionsAsync(token, repos);

            // Select repositories
            var selectedRepos = SelectRepositories(reposWithStatus);
            if (selectedRepos.Length == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No repositories selected.[/]");
                return;
            }

            // Confirm before processing
            if (!ConfirmProcessing(selectedRepos.Length))
            {
                AnsiConsole.MarkupLine("[yellow]Batch processing cancelled.[/]");
                return;
            }

            // Process repositories
            var results = await ProcessRepositoriesAsync(token, selectedRepos);

            // Display summary
            DisplaySummary(results);

            // Write results to file if output path specified
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                await WriteResultsAsync(outputPath, results);
                AnsiConsole.MarkupLine($"[green]Results written to:[/] {outputPath}");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static async Task<GitHubOrg[]> LoadOrganizationsAsync(string token)
    {
        return await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Fetching organizations...", async ctx =>
            {
                var userOrgs = await GitHubService.ListUserOrgsAsync(token);
                
                // Add "Personal Repositories" option
                var allOrgs = new List<GitHubOrg>
                {
                    new GitHubOrg(Login: "__personal__", Name: "Personal Repositories")
                };
                allOrgs.AddRange(userOrgs);

                return allOrgs.ToArray();
            });
    }

    private static GitHubOrg[] SelectOrganizations(GitHubOrg[] orgs)
    {
        var prompt = new MultiSelectionPrompt<GitHubOrg>()
            .Title("Select [green]organizations[/] to process:")
            .PageSize(15)
            .MoreChoicesText("[grey](Move up and down to reveal more organizations)[/]")
            .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
            .UseConverter(org => org.Name ?? org.Login);

        foreach (var org in orgs)
        {
            prompt.AddChoice(org);
        }

        var selected = AnsiConsole.Prompt(prompt);
        return selected.ToArray();
    }

    private static async Task<GitHubRepo[]> LoadRepositoriesAsync(string token, GitHubOrg[] selectedOrgs)
    {
        return await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var allRepos = new List<GitHubRepo>();
                var task = ctx.AddTask("[cyan]Fetching repositories...[/]", maxValue: selectedOrgs.Length);

                for (var i = 0; i < selectedOrgs.Length; i++)
                {
                    var org = selectedOrgs[i];
                    task.Description = $"[cyan]Fetching repos from {org.Name ?? org.Login}[/] ({i + 1}/{selectedOrgs.Length})";

                    GitHubRepo[] repos;
                    if (org.Login == "__personal__")
                    {
                        // Fetch personal repos
                        var personalRepos = await GitHubService.ListAccessibleReposAsync(token, 100);
                        // Filter to only repos owned by the user (exclude org repos already fetched)
                        repos = personalRepos
                            .Where(r => !selectedOrgs.Any(o => o.Login != "__personal__" && o.Login == r.Owner))
                            .ToArray();
                    }
                    else
                    {
                        // Fetch org repos (this method needs to be added or we reuse existing)
                        // For now, we'll use ListAccessibleReposAsync and filter
                        var accessibleRepos = await GitHubService.ListAccessibleReposAsync(token, 1000);
                        repos = accessibleRepos.Where(r => r.Owner == org.Login).Take(100).ToArray();
                    }

                    allRepos.AddRange(repos);
                    task.Increment(1);
                }

                task.StopTask();

                // Deduplicate by FullName
                var uniqueRepos = allRepos
                    .GroupBy(r => r.FullName)
                    .Select(g => g.First())
                    .ToArray();

                return uniqueRepos;
            });
    }

    private static async Task<GitHubRepo[]> CheckRepositoriesForInstructionsAsync(string token, GitHubRepo[] repos)
    {
        return await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[cyan]Checking {repos.Length} repos for instructions...[/]", maxValue: repos.Length);

                var reposWithStatus = await GitHubService.CheckReposForInstructionsAsync(
                    token,
                    repos,
                    (completed, total) =>
                    {
                        task.Value = completed;
                        task.Description = $"[cyan]Checking for instructions[/] ({completed}/{total})";
                    });

                task.StopTask();

                // Sort: repos without instructions first
                var sorted = reposWithStatus
                    .OrderBy(r => r.HasInstructions ?? false)
                    .ToArray();

                var withInstructions = sorted.Count(r => r.HasInstructions == true);
                var withoutInstructions = sorted.Length - withInstructions;

                AnsiConsole.MarkupLine($"[green]Found {sorted.Length} repos[/]: {withoutInstructions} need instructions, {withInstructions} already have them");

                return sorted;
            });
    }

    private static GitHubRepo[] SelectRepositories(GitHubRepo[] repos)
    {
        var prompt = new MultiSelectionPrompt<GitHubRepo>()
            .Title("Select [green]repositories[/] to process:")
            .PageSize(15)
            .MoreChoicesText("[grey](Move up and down to reveal more repositories)[/]")
            .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
            .UseConverter(repo =>
            {
                var indicator = repo.HasInstructions == true ? "[green]✓[/]" : "[red]✗[/]";
                var privacy = repo.IsPrivate ? " [yellow](private)[/]" : "";
                return $"{indicator} {repo.FullName}{privacy}";
            });

        foreach (var repo in repos)
        {
            prompt.AddChoice(repo);
        }

        var selected = AnsiConsole.Prompt(prompt);
        return selected.ToArray();
    }

    private static bool ConfirmProcessing(int count)
    {
        return AnsiConsole.Confirm($"Ready to process [green]{count}[/] repositories. Continue?");
    }

    private static async Task<List<BatchProcessResult>> ProcessRepositoriesAsync(string token, GitHubRepo[] selectedRepos)
    {
        var results = new List<BatchProcessResult>();
        var cacheRoot = Path.Combine(Directory.GetCurrentDirectory(), ".primer-cache");

        await AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var mainTask = ctx.AddTask("[cyan]Processing repositories[/]", maxValue: selectedRepos.Length);

                for (var i = 0; i < selectedRepos.Length; i++)
                {
                    var repo = selectedRepos[i];
                    mainTask.Description = $"[cyan]Processing[/] {repo.FullName} ({i + 1}/{selectedRepos.Length})";

                    try
                    {
                        var repoPath = Path.Combine(cacheRoot, repo.Owner, repo.Name);
                        FileUtils.EnsureDir(repoPath);

                        // Clone if not already cloned
                        if (!GitService.IsGitRepo(repoPath))
                        {
                            var authedUrl = repo.CloneUrl.Replace("https://", $"https://x-access-token:{token}@");
                            await GitService.CloneRepoAsync(
                                authedUrl,
                                repoPath,
                                onProgress: (msg, pct) =>
                                {
                                    if (pct >= 0)
                                    {
                                        mainTask.Description = $"[cyan]Cloning[/] {repo.FullName} ({pct}%)";
                                    }
                                },
                                depth: 1);
                        }

                        // Create branch
                        var branch = "primer/add-instructions";
                        await GitService.CheckoutBranchAsync(repoPath, branch);

                        // Generate instructions
                        mainTask.Description = $"[cyan]Generating instructions for[/] {repo.FullName}";
                        var instructions = await InstructionsService.GenerateCopilotInstructionsAsync(
                            new GenerateInstructionsOptions(
                                RepoPath: repoPath,
                                OnProgress: msg => mainTask.Description = $"[cyan]{repo.Name}:[/] {msg}"));

                        if (string.IsNullOrWhiteSpace(instructions))
                        {
                            throw new InvalidOperationException("Generated instructions were empty");
                        }

                        // Write instructions
                        var instructionsPath = Path.Combine(repoPath, ".github", "copilot-instructions.md");
                        FileUtils.EnsureDir(Path.GetDirectoryName(instructionsPath)!);
                        await File.WriteAllTextAsync(instructionsPath, instructions);

                        // Commit
                        mainTask.Description = $"[cyan]Committing[/] {repo.FullName}";
                        await GitService.CommitAllAsync(repoPath, "chore: add copilot instructions via Primer");

                        // Push
                        mainTask.Description = $"[cyan]Pushing[/] {repo.FullName}";
                        await GitService.PushBranchAsync(repoPath, branch, token);

                        // Create PR
                        mainTask.Description = $"[cyan]Creating PR for[/] {repo.FullName}";
                        var prUrl = await GitHubService.CreatePullRequestAsync(
                            token,
                            repo.Owner,
                            repo.Name,
                            "🤖 Add Copilot instructions via Primer",
                            BuildPrBody(),
                            branch,
                            repo.DefaultBranch);

                        results.Add(new BatchProcessResult(repo.FullName, true, prUrl));
                        AnsiConsole.MarkupLine($"[green]✓[/] {repo.FullName} → [link]{prUrl}[/]");
                    }
                    catch (Exception ex)
                    {
                        var errorMsg = ex.Message;
                        results.Add(new BatchProcessResult(repo.FullName, false, Error: errorMsg));
                        AnsiConsole.MarkupLine($"[red]✗[/] {repo.FullName}: {errorMsg}");
                    }

                    mainTask.Increment(1);
                }

                mainTask.StopTask();
            });

        return results;
    }

    private static void DisplaySummary(List<BatchProcessResult> results)
    {
        AnsiConsole.WriteLine();
        var rule = new Rule("[bold cyan]Batch Processing Summary[/]");
        rule.LeftJustified();
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        var succeeded = results.Count(r => r.Success);
        var failed = results.Count - succeeded;

        var table = new Table();
        table.AddColumn("Status");
        table.AddColumn("Count");

        table.AddRow("[green]Succeeded[/]", $"[green]{succeeded}[/]");
        table.AddRow("[red]Failed[/]", $"[red]{failed}[/]");
        table.AddRow("[cyan]Total[/]", $"[cyan]{results.Count}[/]");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Show detailed results
        if (results.Count > 0)
        {
            var detailsTable = new Table();
            detailsTable.AddColumn("Repository");
            detailsTable.AddColumn("Status");
            detailsTable.AddColumn("Result");

            foreach (var result in results)
            {
                var status = result.Success ? "[green]✓[/]" : "[red]✗[/]";
                var details = result.Success && result.PrUrl != null
                    ? $"[link]{result.PrUrl}[/]"
                    : result.Error ?? "";

                detailsTable.AddRow(result.Repo, status, details);
            }

            AnsiConsole.Write(detailsTable);
        }
    }

    private static async Task WriteResultsAsync(string outputPath, List<BatchProcessResult> results)
    {
        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(outputPath, json);
    }

    private static string BuildPrBody()
    {
        return string.Join("\n", new[]
        {
            "## 🤖 Copilot Instructions Added",
            "",
            "This PR adds a `.github/copilot-instructions.md` file to help GitHub Copilot understand this codebase better.",
            "",
            "### What's Included",
            "",
            "The instructions file contains:",
            "- Project overview and architecture",
            "- Tech stack and conventions",
            "- Build/test commands",
            "- Key directories and files",
            "",
            "### Benefits",
            "",
            "With these instructions, Copilot will:",
            "- Generate more contextually-aware code suggestions",
            "- Follow project-specific patterns and conventions",
            "- Understand the codebase structure",
            "",
            "---",
            "*Generated by [Primer](https://github.com/pierceboggan/primer) - Prime your repos for AI*"
        });
    }
}

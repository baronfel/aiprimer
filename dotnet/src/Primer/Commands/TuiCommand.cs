using System.CommandLine;
using Primer.Services;
using Primer.Utils;
using Spectre.Console;

namespace Primer.Commands;

/// <summary>
/// Command for interactive TUI mode.
/// </summary>
internal static class TuiCommand
{
    private const string BannerText = @"██████╗ ██████╗ ██╗███╗   ███╗███████╗██████╗ 
██╔══██╗██╔══██╗██║████╗ ████║██╔════╝██╔══██╗
██████╔╝██████╔╝██║██╔████╔██║█████╗  ██████╔╝
██╔═══╝ ██╔══██╗██║██║╚██╔╝██║██╔══╝  ██╔══██╗
██║     ██║  ██║██║██║ ╚═╝ ██║███████╗██║  ██║
╚═╝     ╚═╝  ╚═╝╚═╝╚═╝     ╚═╝╚══════╝╚═╝  ╚═╝";

    /// <summary>
    /// Builds the TUI command.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build()
    {
        var command = new Command("tui", "Interactive text user interface");

        var repoOption = new Option<string?>("--repo")
        {
            Description = "Repository path",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory()
        };

        var noAnimationOption = new Option<bool>("--no-animation")
        {
            Description = "Skip the animated banner intro",
            DefaultValueFactory = _ => false
        };

        command.Options.Add(repoOption);
        command.Options.Add(noAnimationOption);

        command.SetAction(async parseResult =>
        {
            var repoPath = parseResult.GetValue(repoOption);
            var noAnimation = parseResult.GetValue(noAnimationOption);
            await ExecuteAsync(repoPath ?? Directory.GetCurrentDirectory(), noAnimation);
        });

        return command;
    }

    private static async Task ExecuteAsync(string repoPath, bool noAnimation)
    {
        // Display banner
        if (!noAnimation)
        {
            AnimateBanner();
        }
        else
        {
            DisplayBanner();
        }

        AnsiConsole.MarkupLine("[cyan]Prime your repo for AI.[/]");
        AnsiConsole.MarkupLine($"[grey]Repo: {repoPath}[/]");
        AnsiConsole.WriteLine();

        // State
        RepoAnalysis? analysis = null;
        string? generatedContent = null;

        // Main loop
        while (true)
        {
            // Display current state
            if (analysis != null)
            {
                DisplayAnalysis(analysis);
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[cyan]Keys: [[A]]nalyze  [[G]]enerate  [[E]]val  [[B]]atch  [[Q]]uit[/]");
            AnsiConsole.Write("Select action: ");

            var key = Console.ReadKey(intercept: true);
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine();

            if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape)
            {
                AnsiConsole.MarkupLine("[yellow]Goodbye![/]");
                break;
            }

            switch (char.ToLowerInvariant(key.KeyChar))
            {
                case 'a':
                    analysis = await AnalyzeRepositoryAsync(repoPath);
                    break;

                case 'g':
                    generatedContent = await GenerateInstructionsAsync(repoPath);
                    if (!string.IsNullOrWhiteSpace(generatedContent))
                    {
                        await HandlePreviewAsync(repoPath, generatedContent);
                        generatedContent = null; // Clear after handling
                    }
                    break;

                case 'e':
                    await RunEvaluationAsync(repoPath);
                    break;

                case 'b':
                    await RunBatchProcessingAsync();
                    // After batch, redisplay banner for context
                    Console.Clear();
                    DisplayBanner();
                    AnsiConsole.MarkupLine("[cyan]Prime your repo for AI.[/]");
                    AnsiConsole.MarkupLine($"[grey]Repo: {repoPath}[/]");
                    AnsiConsole.WriteLine();
                    break;

                default:
                    AnsiConsole.MarkupLine("[yellow]Unknown command. Press A, G, E, B, or Q.[/]");
                    break;
            }

            AnsiConsole.WriteLine();
        }
    }

    private static void AnimateBanner()
    {
        var lines = BannerText.Split('\n');
        var width = lines[0].Length;

        // Simple animation: slide in from right
        for (var offset = width; offset >= 0; offset -= 4)
        {
            Console.Clear();
            foreach (var line in lines)
            {
                if (offset >= line.Length)
                {
                    AnsiConsole.WriteLine();
                }
                else
                {
                    var spaces = new string(' ', Math.Max(0, offset));
                    var visible = line.Substring(0, Math.Max(0, line.Length - offset));
                    AnsiConsole.MarkupLine($"[magenta bold]{spaces}{visible}[/]");
                }
            }
            Thread.Sleep(50);
        }

        // Final frame
        Console.Clear();
        DisplayBanner();
    }

    private static void DisplayBanner()
    {
        AnsiConsole.Write(new FigletText("PRIMER")
            .Centered()
            .Color(Color.Magenta));
        AnsiConsole.WriteLine();
    }

    private static void DisplayAnalysis(RepoAnalysis analysis)
    {
        var panel = new Panel(
            new Rows(
                new Markup($"[cyan]Languages:[/] {string.Join(", ", analysis.Languages)}"),
                new Markup($"[cyan]Frameworks:[/] {string.Join(", ", analysis.Frameworks)}"),
                new Markup($"[cyan]Package Manager:[/] {analysis.PackageManager ?? "unknown"}"),
                new Markup($"[cyan]Is Git Repo:[/] {(analysis.IsGitRepo ? "Yes" : "No")}")
            ))
        {
            Header = new PanelHeader("[bold cyan]Analysis Results[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Cyan)
        };

        AnsiConsole.Write(panel);
    }

    private static async Task<RepoAnalysis?> AnalyzeRepositoryAsync(string repoPath)
    {
        try
        {
            return await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Analyzing repository...", async ctx =>
                {
                    var result = AnalyzerService.AnalyzeRepo(repoPath);
                    await Task.Delay(100); // Small delay for visual feedback
                    return result;
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> GenerateInstructionsAsync(string repoPath)
    {
        try
        {
            return await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Generating instructions...", async ctx =>
                {
                    var instructions = await InstructionsService.GenerateCopilotInstructionsAsync(
                        new GenerateInstructionsOptions(
                            RepoPath: repoPath,
                            OnProgress: msg =>
                            {
                                ctx.Status = msg;
                            }));

                    return instructions;
                });
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (message.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("login", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {message}");
                AnsiConsole.MarkupLine("[yellow]Try running 'copilot' and then '/login' in a separate terminal.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {message}");
            }
            return null;
        }
    }

    private static async Task HandlePreviewAsync(string repoPath, string content)
    {
        // Display preview
        var lines = content.Split('\n');
        var previewLines = lines.Take(20).ToArray();
        var truncated = lines.Length > 20;

        var previewText = string.Join("\n", previewLines);
        if (truncated)
        {
            previewText += "\n[grey]...[/]";
        }

        var panel = new Panel(new Markup(Markup.Escape(previewText)))
        {
            Header = new PanelHeader("[bold cyan]Preview: .github/copilot-instructions.md[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        // Ask to save or discard
        AnsiConsole.MarkupLine("[cyan]Keys: [[S]]ave  [[D]]iscard[/]");
        AnsiConsole.Write("Action: ");

        var key = Console.ReadKey(intercept: true);
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();

        if (char.ToLowerInvariant(key.KeyChar) == 's')
        {
            try
            {
                var outputPath = Path.Combine(repoPath, ".github", "copilot-instructions.md");
                FileUtils.EnsureDir(Path.GetDirectoryName(outputPath)!);
                await File.WriteAllTextAsync(outputPath, content);
                AnsiConsole.MarkupLine($"[green]✓ Saved to {outputPath}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error saving:[/] {ex.Message}");
            }
        }
        else if (char.ToLowerInvariant(key.KeyChar) == 'd')
        {
            AnsiConsole.MarkupLine("[yellow]Discarded generated instructions.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]No action taken.[/]");
        }
    }

    private static async Task RunEvaluationAsync(string repoPath)
    {
        var configPath = Path.Combine(repoPath, "primer.eval.json");
        
        if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine($"[yellow]No primer.eval.json found.[/]");
            AnsiConsole.MarkupLine($"[grey]Run 'primer eval --init' to create one.[/]");
            return;
        }

        try
        {
            var results = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Running evaluations...", async ctx =>
                {
                    return await EvaluatorService.RunEvalAsync(
                        new EvalRunOptions(
                            ConfigPath: configPath,
                            RepoPath: repoPath,
                            Model: "gpt-4.1",
                            JudgeModel: "gpt-4.1",
                            OnProgress: msg =>
                            {
                                ctx.Status = msg;
                            }));
                });

            // Display results
            var passed = results.Count(r => r.Verdict == "with-instructions");
            var failed = results.Count(r => r.Verdict == "without-instructions");
            var tie = results.Count(r => r.Verdict == "tie");

            var table = new Table();
            table.AddColumn("Case");
            table.AddColumn("Verdict");
            table.AddColumn("Score");

            foreach (var result in results)
            {
                var verdictColor = result.Verdict == "with-instructions" ? "green" :
                                   result.Verdict == "without-instructions" ? "red" : "yellow";
                var verdictMark = result.Verdict == "with-instructions" ? "✓" :
                                  result.Verdict == "without-instructions" ? "✗" : "~";
                
                table.AddRow(
                    result.Id,
                    $"[{verdictColor}]{verdictMark} {result.Verdict}[/]",
                    result.Score?.ToString() ?? "N/A");
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[cyan]Summary:[/] [green]{passed} better with instructions[/], [red]{failed} better without[/], [yellow]{tie} tie[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
        }
    }

    private static async Task RunBatchProcessingAsync()
    {
        // Check for GitHub token
        var token = await GitHubService.GetGitHubTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] GitHub authentication required.");
            AnsiConsole.MarkupLine("[yellow]Run 'gh auth login' or set GITHUB_TOKEN.[/]");
            return;
        }

        AnsiConsole.MarkupLine("[cyan]Switching to batch mode...[/]");
        await Task.Delay(500);

        // Clear and run batch command logic
        Console.Clear();
        
        // Execute batch processing inline (simplified version)
        try
        {
            // Load organizations
            var orgs = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching organizations...", async ctx =>
                {
                    var userOrgs = await GitHubService.ListUserOrgsAsync(token);
                    var allOrgs = new List<GitHubOrg>
                    {
                        new GitHubOrg(Login: "__personal__", Name: "Personal Repositories")
                    };
                    allOrgs.AddRange(userOrgs);
                    return allOrgs.ToArray();
                });

            if (orgs.Length == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No organizations found.[/]");
                return;
            }

            // Select organizations
            var selectedOrgs = new MultiSelectionPrompt<GitHubOrg>()
                .Title("Select [green]organizations[/] to process:")
                .PageSize(15)
                .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept, [red]<esc>[/] to cancel)[/]")
                .UseConverter(org => org.Name ?? org.Login)
                .AddChoices(orgs);

            var selected = AnsiConsole.Prompt(selectedOrgs);
            
            if (!selected.Any())
            {
                AnsiConsole.MarkupLine("[yellow]No organizations selected.[/]");
                return;
            }

            AnsiConsole.MarkupLine($"[green]Selected {selected.Count} organization(s)[/]");
            AnsiConsole.MarkupLine("[grey]Press any key to continue...[/]");
            Console.ReadKey(intercept: true);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
        }
    }
}

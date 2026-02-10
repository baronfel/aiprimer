using System.CommandLine;
using System.Text.Json;
using Primer.Services;
using Primer.Utils;

namespace Primer.Commands;

/// <summary>
/// Command for running Copilot evaluation tests.
/// </summary>
internal static class EvalCommand
{
    private static readonly EvalConfig EvalScaffold = new(
        Cases: [
            new EvalCase(
                Prompt: "Summarize what this project does and list the main entry points.",
                Expectation: "Should mention the primary purpose and key files/directories. Should be specific to this repo, not generic.",
                Id: "project-overview",
                Category: "understanding"
            ),
            new EvalCase(
                Prompt: "What languages, frameworks, and key dependencies does this project use?",
                Expectation: "Should correctly identify the main languages, frameworks, package manager, and notable libraries.",
                Id: "tech-stack",
                Category: "understanding"
            ),
            new EvalCase(
                Prompt: "How do I build, test, and run this project locally?",
                Expectation: "Should provide correct build, test, and run commands. Should mention prerequisites (e.g. SDK versions, environment variables).",
                Id: "build-test-run",
                Category: "development"
            ),
            new EvalCase(
                Prompt: "Describe the project directory structure and what each key directory contains.",
                Expectation: "Should list the main directories (src, tests, config, etc.) with accurate descriptions of their contents.",
                Id: "project-structure",
                Category: "understanding"
            ),
            new EvalCase(
                Prompt: "What coding conventions and style rules does this project follow?",
                Expectation: "Should mention language-specific conventions, linter/formatter config, naming patterns, and any documented style guidelines.",
                Id: "coding-conventions",
                Category: "conventions"
            ),
            new EvalCase(
                Prompt: "How are tests organized in this project? What testing frameworks are used and how do I run a single test?",
                Expectation: "Should identify the testing framework(s), test file locations, and provide the command to run a specific test.",
                Id: "testing",
                Category: "development"
            ),
            new EvalCase(
                Prompt: "What CI/CD pipelines does this project use? Describe the workflow steps.",
                Expectation: "Should identify CI/CD system (GitHub Actions, Azure Pipelines, etc.) and describe the key workflow steps and triggers.",
                Id: "ci-cd",
                Category: "infrastructure"
            ),
            new EvalCase(
                Prompt: "How should I handle errors and exceptions in this codebase? Show the pattern used.",
                Expectation: "Should describe the error handling patterns used in the project (e.g., try/catch, Result types, error middleware) with specific examples.",
                Id: "error-handling",
                Category: "conventions"
            ),
            new EvalCase(
                Prompt: "I want to add a new feature. Walk me through the typical steps: where to add code, what patterns to follow, and how to test it.",
                Expectation: "Should outline the workflow: where new code goes, naming/file conventions, how to wire it up, and how to add tests. Should be specific to this project's architecture.",
                Id: "new-feature-workflow",
                Category: "development"
            ),
            new EvalCase(
                Prompt: "What configuration files exist in this project and what do they control?",
                Expectation: "Should list key config files (tsconfig, .eslintrc, .editorconfig, Dockerfile, CI configs, etc.) and explain what each controls.",
                Id: "config-files",
                Category: "understanding"
            )
        ],
        InstructionFile: ".github/copilot-instructions.md"
    );

    /// <summary>
    /// Builds the eval command.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build()
    {
        var command = new Command("eval", "Run Copilot evaluation tests");

        // Optional argument for config path (defaults to {repoPath}/primer.eval.json)
        var configPathArgument = new Argument<string?>("config")
        {
            Description = "Path to evaluation config file (defaults to {repo}/primer.eval.json)",
            DefaultValueFactory = _ => null
        };

        // Option for repository path
        var repoOption = new Option<string?>("--repo")
        {
            Description = "Path to the repository (defaults to current directory)",
            DefaultValueFactory = _ => null
        };

        // Option for model
        var modelOption = new Option<string>("--model")
        {
            Description = "Model to use for evaluation",
            DefaultValueFactory = _ => "gpt-5"
        };

        // Option for judge model
        var judgeModelOption = new Option<string>("--judge-model")
        {
            Description = "Model to use for judging results",
            DefaultValueFactory = _ => "gpt-5"
        };

        // Option for output path
        var outputOption = new Option<string?>("--output")
        {
            Description = "Path to save evaluation results JSON",
            DefaultValueFactory = _ => null
        };

        // Option to initialize config file
        var initOption = new Option<bool>("--init")
        {
            Description = "Create a starter primer.eval.json file",
            DefaultValueFactory = _ => false
        };

        command.Arguments.Add(configPathArgument);
        command.Options.Add(repoOption);
        command.Options.Add(modelOption);
        command.Options.Add(judgeModelOption);
        command.Options.Add(outputOption);
        command.Options.Add(initOption);

        command.SetAction(parseResult =>
        {
            try
            {
                // Get values from parseResult
                var configPathArg = parseResult.GetValue(configPathArgument);
                var repoPathArg = parseResult.GetValue(repoOption);
                var model = parseResult.GetValue(modelOption)!;
                var judgeModel = parseResult.GetValue(judgeModelOption)!;
                var output = parseResult.GetValue(outputOption);
                var init = parseResult.GetValue(initOption);

                // Resolve repository path
                var repoPath = Path.GetFullPath(repoPathArg ?? Directory.GetCurrentDirectory());

                // Handle --init flag
                if (init)
                {
                    var outputPath = Path.Combine(repoPath, "primer.eval.json");
                    if (File.Exists(outputPath))
                    {
                        Console.Error.WriteLine($"primer.eval.json already exists at {outputPath}");
                        return 1;
                    }

                    var json = JsonSerializer.Serialize(EvalScaffold, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    });

                    File.WriteAllText(outputPath, json);
                    Console.WriteLine($"Created {outputPath}");
                    Console.WriteLine("Edit the file to add your own test cases, then run 'primer eval' to test.");
                    return 0;
                }

                // Resolve config path (default to {repoPath}/primer.eval.json)
                var configPath = Path.GetFullPath(configPathArg ?? Path.Combine(repoPath, "primer.eval.json"));

                if (!File.Exists(configPath))
                {
                    Console.Error.WriteLine($"Error: Config file not found: {configPath}");
                    Console.Error.WriteLine("Run 'primer eval --init' to create a starter config file.");
                    return 1;
                }

                // Run evaluation (synchronously wait for async operation)
                var options = new EvalRunOptions(
                    ConfigPath: configPath,
                    RepoPath: repoPath,
                    Model: model,
                    JudgeModel: judgeModel,
                    OutputPath: output,
                    OnProgress: msg => Console.WriteLine(msg)
                );

                var results = EvaluatorService.RunEvalAsync(options).GetAwaiter().GetResult();

                // Print summary
                var summary = EvaluatorService.FormatSummary(results);
                Console.WriteLine();
                Console.WriteLine(summary);

                return 0;
            }
            catch (FileNotFoundException ex)
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
                Console.Error.WriteLine($"Error running evaluation: {ex.Message}");
                return 1;
            }
        });

        return command;
    }
}

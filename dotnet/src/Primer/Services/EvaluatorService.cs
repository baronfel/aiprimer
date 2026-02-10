using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Primer.Services;

/// <summary>
/// Evaluator service for running Copilot evaluation tests.
/// Uses process-based integration with the Copilot CLI since the Copilot SDK is not available in .NET.
/// </summary>
public static class EvaluatorService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Runs the evaluation with the given options.
    /// </summary>
    public static async Task<List<EvalResult>> RunEvalAsync(EvalRunOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConfigPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RepoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.JudgeModel);

        // Fail fast: verify copilot CLI is reachable before running any cases
        options.OnProgress?.Invoke("Verifying Copilot CLI is available...");
        await VerifyCopilotCliAsync(ct);
        options.OnProgress?.Invoke("✓ Copilot CLI is available");

        options.OnProgress?.Invoke("Loading evaluation config...");
        var config = await LoadConfigAsync(options.ConfigPath, ct);

        string? instructions = null;
        if (!string.IsNullOrWhiteSpace(config.InstructionFile))
        {
            var instructionPath = Path.Combine(options.RepoPath, config.InstructionFile);
            if (File.Exists(instructionPath))
            {
                options.OnProgress?.Invoke($"Loading instructions from {config.InstructionFile}...");
                instructions = await File.ReadAllTextAsync(instructionPath, ct);
            }
            else
            {
                options.OnProgress?.Invoke($"Warning: Instruction file not found: {instructionPath}");
            }
        }

        var results = new List<EvalResult>();
        var totalCases = config.Cases.Count;

        for (var i = 0; i < config.Cases.Count; i++)
        {
            var evalCase = config.Cases[i];
            var caseId = evalCase.Id ?? $"case-{i + 1}";

            options.OnProgress?.Invoke($"[{i + 1}/{totalCases}] Running case: {caseId}");

            // Run with instructions
            string? withInstructions = null;
            if (!string.IsNullOrWhiteSpace(instructions))
            {
                options.OnProgress?.Invoke($"  → With instructions...");
                var promptWithInstructions = $"{instructions}\n\n{evalCase.Prompt}";
                withInstructions = await AskOnceAsync(
                    promptWithInstructions,
                    options.Model,
                    config.SystemMessage,
                    ct);
            }

            // Run without instructions
            options.OnProgress?.Invoke($"  → Without instructions...");
            var withoutInstructions = await AskOnceAsync(
                evalCase.Prompt,
                options.Model,
                config.SystemMessage,
                ct);

            // Judge the results
            options.OnProgress?.Invoke($"  → Judging results...");
            var (verdict, score, rationale) = await JudgeAsync(
                evalCase.Prompt,
                evalCase.Expectation,
                withInstructions,
                withoutInstructions,
                options.JudgeModel,
                ct);

            results.Add(new EvalResult(
                Id: caseId,
                Prompt: evalCase.Prompt,
                Expectation: evalCase.Expectation,
                Category: evalCase.Category,
                WithInstructions: withInstructions,
                WithoutInstructions: withoutInstructions,
                Verdict: verdict,
                Score: score,
                Rationale: rationale
            ));

            options.OnProgress?.Invoke($"  ✓ Score: {score}/10 - {verdict}");
        }

        // Save results if output path provided
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            options.OnProgress?.Invoke($"Saving results to {options.OutputPath}...");
            var json = JsonSerializer.Serialize(results, JsonOptions);
            await File.WriteAllTextAsync(options.OutputPath, json, ct);
        }

        return results;
    }

    /// <summary>
    /// Loads the evaluation configuration from a JSON file.
    /// </summary>
    public static async Task<EvalConfig> LoadConfigAsync(string configPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Config file not found: {configPath}");
        }

        var json = await File.ReadAllTextAsync(configPath, ct);
        var config = JsonSerializer.Deserialize<EvalConfig>(json, JsonOptions);

        if (config == null)
        {
            throw new InvalidOperationException($"Failed to deserialize config from {configPath}");
        }

        if (config.Cases.Count == 0)
        {
            throw new InvalidOperationException($"Config must contain at least one test case");
        }

        return config;
    }

    /// <summary>
    /// Formats a summary of the evaluation results.
    /// </summary>
    public static string FormatSummary(List<EvalResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (results.Count == 0)
        {
            return "No results to summarize.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== Evaluation Summary ===");
        sb.AppendLine();

        var scoredResults = results.Where(r => r.Score.HasValue).ToList();
        var avgScore = scoredResults.Count > 0 ? scoredResults.Average(r => r.Score!.Value) : 0;
        var totalCases = results.Count;
        var withInstructionsBetter = results.Count(r => r.Verdict == "with-instructions");
        var withoutInstructionsBetter = results.Count(r => r.Verdict == "without-instructions");
        var tie = results.Count(r => r.Verdict == "tie");
        var errors = results.Count(r => r.Verdict == "error" || r.Verdict == "unknown");

        sb.AppendLine($"Total Cases: {totalCases}");
        sb.AppendLine($"Average Score: {avgScore:F1}/10");
        sb.AppendLine();
        sb.AppendLine($"With Instructions Better: {withInstructionsBetter} ({100.0 * withInstructionsBetter / totalCases:F0}%)");
        sb.AppendLine($"Without Instructions Better: {withoutInstructionsBetter} ({100.0 * withoutInstructionsBetter / totalCases:F0}%)");
        sb.AppendLine($"Tie: {tie} ({100.0 * tie / totalCases:F0}%)");
        if (errors > 0)
            sb.AppendLine($"Errors: {errors}");
        sb.AppendLine();

        // Group by category if categories exist
        var categories = results.Where(r => r.Category is not null).Select(r => r.Category!).Distinct().ToList();
        if (categories.Count > 1)
        {
            sb.AppendLine("=== By Category ===");
            sb.AppendLine();
            foreach (var cat in categories)
            {
                var catResults = results.Where(r => r.Category == cat).ToList();
                var catScored = catResults.Where(r => r.Score.HasValue).ToList();
                var catAvg = catScored.Count > 0 ? catScored.Average(r => r.Score!.Value) : 0;
                var catWin = catResults.Count(r => r.Verdict == "with-instructions");
                sb.AppendLine($"  {cat}: avg {catAvg:F1}/10, {catWin}/{catResults.Count} better with instructions");
            }
            sb.AppendLine();
        }

        sb.AppendLine("=== Individual Results ===");
        sb.AppendLine();

        foreach (var result in results)
        {
            var label = result.Category is not null ? $"{result.Id} [{result.Category}]" : result.Id;
            sb.AppendLine($"Case: {label}");
            sb.AppendLine($"  Score: {result.Score}/10");
            sb.AppendLine($"  Verdict: {result.Verdict}");
            if (result.Rationale is not null && result.Rationale.Length < 300)
                sb.AppendLine($"  Rationale: {result.Rationale}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Verifies that the Copilot CLI is reachable. Throws if not.
    /// </summary>
    private static async Task VerifyCopilotCliAsync(CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "copilot",
                ArgumentList = { "--version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                throw new InvalidOperationException(
                    "Failed to start the 'copilot' process. Ensure the Copilot CLI is installed and in your PATH.");
            }

            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(ct);
                throw new InvalidOperationException(
                    $"Copilot CLI returned exit code {process.ExitCode}: {error.Trim()}\n" +
                    "Ensure you are authenticated (run 'copilot auth').");
            }

            // Also verify pwsh is available (used for prompt passing)
            var pwshPsi = new ProcessStartInfo
            {
                FileName = "pwsh",
                ArgumentList = { "-NoProfile", "-Command", "echo ok" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var pwshProcess = Process.Start(pwshPsi);
            if (pwshProcess == null)
            {
                throw new InvalidOperationException(
                    "PowerShell (pwsh) is required but could not be started. Install PowerShell 7+.");
            }

            await pwshProcess.WaitForExitAsync(ct);
        }
        catch (Exception ex) when (ex is not InvalidOperationException and not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Prerequisite check failed: {ex.Message}\n\n" +
                "The eval command requires:\n" +
                "  1. Copilot CLI installed and in PATH\n" +
                "  2. PowerShell 7+ (pwsh) installed\n" +
                "  3. Copilot authenticated (run 'copilot auth')", ex);
        }
    }

    /// <summary>
    /// Asks the Copilot CLI once with the given prompt.
    /// Throws on failure instead of returning error strings.
    /// </summary>
    private static async Task<string> AskOnceAsync(
        string prompt,
        string model,
        string? systemMessage,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        // Build the full prompt, prepending system message if provided
        var fullPrompt = string.IsNullOrWhiteSpace(systemMessage)
            ? prompt
            : $"{systemMessage}\n\n{prompt}";

        // Write prompt to a temp file to avoid command-line argument escaping issues.
        // .NET's ArgumentList escaping can corrupt prompts containing quotes, backticks,
        // and other special characters from AI responses, crashing the copilot CLI.
        // We invoke through pwsh which reads the file and passes it correctly.
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, fullPrompt, ct);

            const int maxRetries = 2;
            for (var attempt = 0; attempt <= maxRetries; attempt++)
            {
                // Escape single quotes in the file path for PowerShell
                var escapedPath = tempFile.Replace("'", "''");
                var psCommand = $"copilot -p (Get-Content -LiteralPath '{escapedPath}' -Raw) --model {model} -s --no-ask-user --disable-builtin-mcps";

                var psi = new ProcessStartInfo
                {
                    FileName = "pwsh",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-NonInteractive");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add(psCommand);

                using var process = Process.Start(psi)
                    ?? throw new InvalidOperationException("Failed to start process.");

                var outputTask = process.StandardOutput.ReadToEndAsync(ct);
                var errorTask = process.StandardError.ReadToEndAsync(ct);

                await process.WaitForExitAsync(ct);

                var output = await outputTask;
                var error = await errorTask;

                // Negative exit codes on Windows indicate process crashes — retry those
                if (process.ExitCode < 0 && attempt < maxRetries)
                {
                    await Task.Delay(1000 * (attempt + 1), ct);
                    continue;
                }

                if (process.ExitCode != 0)
                {
                    var detail = !string.IsNullOrWhiteSpace(error) ? error.Trim()
                        : !string.IsNullOrWhiteSpace(output) ? $"stdout: {output.Trim()}"
                        : "(no output)";
                    throw new InvalidOperationException(
                        $"Copilot CLI exited with code {process.ExitCode}: {detail}");
                }

                return string.IsNullOrWhiteSpace(output)
                    ? throw new InvalidOperationException("Copilot CLI returned empty output.")
                    : output.Trim();
            }

            throw new InvalidOperationException("Copilot CLI crashed repeatedly. The process may be unstable.");
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    /// <summary>
    /// Judges the evaluation results using the Copilot CLI.
    /// Returns (verdict, score, rationale).
    /// </summary>
    private static async Task<(string? verdict, int? score, string? rationale)> JudgeAsync(
        string prompt,
        string expectation,
        string? withInstructions,
        string? withoutInstructions,
        string judgeModel,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectation);
        ArgumentException.ThrowIfNullOrWhiteSpace(judgeModel);

        var judgePrompt = BuildJudgePrompt(prompt, expectation, withInstructions, withoutInstructions);

        try
        {
            var judgeResponse = await AskOnceAsync(
                judgePrompt,
                judgeModel,
                "You are an impartial judge evaluating AI assistant responses.",
                ct);

            // Parse the judge response as JSON
            try
            {
                var judgeResult = JsonSerializer.Deserialize<JudgeResponse>(judgeResponse, JsonOptions);
                if (judgeResult != null)
                {
                    return (judgeResult.Verdict, judgeResult.Score, judgeResult.Rationale);
                }
            }
            catch
            {
                // If JSON parsing fails, try to extract information from text
                return ParseJudgeResponseText(judgeResponse);
            }

            return (verdict: "unknown", score: 5, rationale: judgeResponse);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (verdict: "error", score: 0, rationale: $"Judge error: {ex.Message}");
        }
    }

    private static string BuildJudgePrompt(
        string prompt,
        string expectation,
        string? withInstructions,
        string? withoutInstructions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an impartial judge comparing two AI assistant responses to a user prompt about a codebase.");
        sb.AppendLine("Evaluate which response better meets the stated expectation.");
        sb.AppendLine();
        sb.AppendLine("## Scoring Rubric");
        sb.AppendLine("- **10**: Perfectly meets the expectation with specific, accurate details");
        sb.AppendLine("- **7-9**: Mostly meets the expectation with good specificity");
        sb.AppendLine("- **4-6**: Partially meets the expectation, missing key details");
        sb.AppendLine("- **1-3**: Largely fails to meet the expectation");
        sb.AppendLine("- **0**: Completely irrelevant or wrong");
        sb.AppendLine();
        sb.AppendLine("## User Prompt");
        sb.AppendLine(prompt);
        sb.AppendLine();
        sb.AppendLine("## Expected Outcome");
        sb.AppendLine(expectation);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(withInstructions))
        {
            sb.AppendLine("## Response A (with instructions)");
            sb.AppendLine(withInstructions);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(withoutInstructions))
        {
            sb.AppendLine("## Response B (without instructions)");
            sb.AppendLine(withoutInstructions);
            sb.AppendLine();
        }

        sb.AppendLine("## Your Task");
        sb.AppendLine("Compare the two responses. Respond ONLY with valid JSON (no markdown fencing):");
        sb.AppendLine("{");
        sb.AppendLine("  \"verdict\": \"response-a\" | \"response-b\" | \"tie\",");
        sb.AppendLine("  \"score\": <0-10 score for the BETTER response>,");
        sb.AppendLine("  \"rationale\": \"<brief explanation of why one is better>\"");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static (string? verdict, int? score, string? rationale) ParseJudgeResponseText(string response)
    {
        string? verdict = null;
        int? score = null;
        string? rationale = response;

        // Try to extract JSON block if wrapped in markdown fences
        var jsonMatch = Regex.Match(response, @"\{[^{}]*""verdict""[^{}]*\}", RegexOptions.Singleline);
        if (jsonMatch.Success)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<JudgeResponse>(jsonMatch.Value, JsonOptions);
                if (parsed is not null)
                {
                    var v = parsed.Verdict?.ToLowerInvariant()?.Trim();
                    verdict = v switch
                    {
                        "response-a" or "with-instructions" => "with-instructions",
                        "response-b" or "without-instructions" => "without-instructions",
                        "tie" => "tie",
                        _ => v
                    };
                    return (verdict, parsed.Score, parsed.Rationale);
                }
            }
            catch { }
        }

        // Fallback text parsing — use word boundaries to avoid substring false matches
        if (Regex.IsMatch(response, @"\bresponse[- ]?a\b", RegexOptions.IgnoreCase))
            verdict = "with-instructions";
        else if (Regex.IsMatch(response, @"\bresponse[- ]?b\b", RegexOptions.IgnoreCase))
            verdict = "without-instructions";
        else if (Regex.IsMatch(response, @"\btie\b", RegexOptions.IgnoreCase))
            verdict = "tie";

        // Try to extract score
        var scoreMatch = Regex.Match(response, @"(?:score|rating)[\s:""]+(\d+)(?:\s*/\s*10)?", RegexOptions.IgnoreCase);
        if (scoreMatch.Success && int.TryParse(scoreMatch.Groups[1].Value, out var parsedScore))
        {
            score = Math.Clamp(parsedScore, 0, 10);
        }

        return (verdict, score, rationale);
    }

    private record JudgeResponse(
        [property: JsonPropertyName("verdict")] string? Verdict,
        [property: JsonPropertyName("score")] int? Score,
        [property: JsonPropertyName("rationale")] string? Rationale
    );
}

public record EvalCase(
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("expectation")] string Expectation,
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("category")] string? Category = null
);

public record EvalConfig(
    [property: JsonPropertyName("cases")] List<EvalCase> Cases,
    [property: JsonPropertyName("instructionFile")] string? InstructionFile = null,
    [property: JsonPropertyName("systemMessage")] string? SystemMessage = null
);

public record EvalResult(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("expectation")] string Expectation,
    [property: JsonPropertyName("category")] string? Category = null,
    [property: JsonPropertyName("withInstructions")] string? WithInstructions = null,
    [property: JsonPropertyName("withoutInstructions")] string? WithoutInstructions = null,
    [property: JsonPropertyName("verdict")] string? Verdict = null,
    [property: JsonPropertyName("score")] int? Score = null,
    [property: JsonPropertyName("rationale")] string? Rationale = null
);

public record EvalRunOptions(
    string ConfigPath,
    string RepoPath,
    string Model,
    string JudgeModel,
    string? OutputPath = null,
    Action<string>? OnProgress = null
);

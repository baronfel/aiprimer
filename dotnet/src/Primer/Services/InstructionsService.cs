using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Primer.Services;

/// <summary>
/// Options for generating Copilot instructions.
/// </summary>
/// <param name="RepoPath">Path to the repository root.</param>
/// <param name="Model">Optional model to use for generation.</param>
/// <param name="OnProgress">Optional callback for progress updates.</param>
public record GenerateInstructionsOptions(
    string RepoPath,
    string? Model = null,
    Action<string>? OnProgress = null
);

/// <summary>
/// Service for generating GitHub Copilot instructions by analyzing codebases.
/// </summary>
public static class InstructionsService
{
    private static readonly string[] VsCodeExtensionPaths = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "bin", "copilot"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "resources", "app", "extensions")
        }
        : new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions"),
            "/usr/share/code/resources/app/extensions",
            "/usr/local/share/code/resources/app/extensions"
        };

    /// <summary>
    /// Finds the GitHub Copilot CLI path.
    /// </summary>
    /// <returns>The path to the copilot CLI, or null if not found.</returns>
    public static async Task<string?> FindCopilotCliPathAsync()
    {
        // First, check if copilot is in PATH
        var pathCliPath = await FindInPathAsync();
        if (pathCliPath is not null)
        {
            return pathCliPath;
        }

        // Check VS Code extension locations
        foreach (var basePath in VsCodeExtensionPaths)
        {
            if (!Directory.Exists(basePath))
            {
                continue;
            }

            var extensionPath = FindCopilotExtension(basePath);
            if (extensionPath is not null)
            {
                return extensionPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Verifies that the Copilot CLI is available and functional.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the CLI is not found or not functional.</exception>
    public static async Task AssertCopilotCliReadyAsync()
    {
        var cliPath = await FindCopilotCliPathAsync();
        if (cliPath is null)
        {
            throw new InvalidOperationException(
                "GitHub Copilot CLI not found. Please ensure it is installed and in your PATH, " +
                "or available in VS Code extensions.");
        }

        // Try to run copilot --version to verify it works
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = cliPath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"Copilot CLI is not functional: {error}");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to verify Copilot CLI: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Generates GitHub Copilot instructions for a repository.
    /// Tries CLI-based generation first, falls back to basic analysis.
    /// </summary>
    /// <param name="options">Generation options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated instructions markdown content.</returns>
    public static async Task<string> GenerateCopilotInstructionsAsync(
        GenerateInstructionsOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RepoPath);

        if (!Directory.Exists(options.RepoPath))
        {
            throw new DirectoryNotFoundException($"Repository path not found: {options.RepoPath}");
        }

        // Try CLI-based generation first
        try
        {
            var cliResult = await GenerateCopilotInstructionsViaCliAsync(options, cancellationToken);
            if (!string.IsNullOrWhiteSpace(cliResult))
            {
                options.OnProgress?.Invoke("✓ Generated instructions using Copilot CLI");
                return cliResult;
            }
        }
        catch (Exception ex)
        {
            options.OnProgress?.Invoke($"⚠ CLI generation failed: {ex.Message}");
            options.OnProgress?.Invoke("→ Falling back to basic analysis...");
        }

        // Fall back to basic generation
        var basicResult = await GenerateBasicInstructionsAsync(options.RepoPath, cancellationToken);
        options.OnProgress?.Invoke("✓ Generated basic instructions from repository analysis");
        return basicResult;
    }

    /// <summary>
    /// Generates instructions by invoking the Copilot CLI process.
    /// </summary>
    /// <param name="options">Generation options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated instructions, or empty string if CLI approach fails.</returns>
    public static async Task<string> GenerateCopilotInstructionsViaCliAsync(
        GenerateInstructionsOptions options,
        CancellationToken cancellationToken = default)
    {
        var cliPath = await FindCopilotCliPathAsync();
        if (cliPath is null)
        {
            return string.Empty;
        }

        // Note: The actual implementation of CLI-based generation would require
        // the Copilot SDK or a way to script the CLI, which may not be directly
        // supported. This is a best-effort approach that could be extended
        // if the CLI supports scripting or if we can use the SDK via Node.js interop.

        // For now, we return empty to indicate this approach is not implemented
        // and let the caller fall back to basic generation.
        options.OnProgress?.Invoke("⚠ CLI-based generation requires Copilot SDK integration");
        return string.Empty;
    }

    /// <summary>
    /// Generates basic instructions by analyzing repository files directly.
    /// </summary>
    /// <param name="repoPath">Path to the repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated instructions markdown content.</returns>
    public static async Task<string> GenerateBasicInstructionsAsync(
        string repoPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);

        var md = new StringBuilder();
        var repoAnalysis = AnalyzerService.AnalyzeRepo(repoPath);
        var projectInfo = await AnalyzeProjectStructureAsync(repoPath, cancellationToken);

        md.AppendLine("# Project Guidelines");
        md.AppendLine();

        // Overview
        md.AppendLine("## Overview");
        md.AppendLine();
        if (!string.IsNullOrWhiteSpace(projectInfo.Description))
        {
            md.AppendLine(projectInfo.Description);
            md.AppendLine();
        }

        // Tech Stack
        var techItems = new List<string>();
        techItems.AddRange(repoAnalysis.Languages.Select(l => $"**{l}**"));
        techItems.AddRange(repoAnalysis.Frameworks.Select(f => $"{f}"));
        if (repoAnalysis.PackageManager is not null) techItems.Add($"{repoAnalysis.PackageManager} (package manager)");
        if (techItems.Count > 0)
        {
            md.AppendLine("## Tech Stack");
            md.AppendLine();
            foreach (var tech in techItems) md.AppendLine($"- {tech}");
            md.AppendLine();
        }

        // Project Structure
        if (repoAnalysis.KeyDirectories.Count > 0)
        {
            md.AppendLine("## Project Structure");
            md.AppendLine();
            foreach (var dir in repoAnalysis.KeyDirectories)
            {
                md.AppendLine($"- `{dir.Path}` — {dir.Description}");
            }
            md.AppendLine();
        }

        // Coding Conventions
        md.AppendLine("## Coding Conventions");
        md.AppendLine();
        foreach (var convention in projectInfo.Conventions)
        {
            md.AppendLine($"- {convention}");
        }
        md.AppendLine();

        // Build & Test
        if (projectInfo.BuildCommands.Count > 0 || projectInfo.TestCommands.Count > 0)
        {
            md.AppendLine("## Development");
            md.AppendLine();

            if (projectInfo.BuildCommands.Count > 0)
            {
                md.AppendLine("### Build");
                md.AppendLine();
                foreach (var cmd in projectInfo.BuildCommands)
                {
                    md.AppendLine("```bash");
                    md.AppendLine(cmd);
                    md.AppendLine("```");
                }
                md.AppendLine();
            }

            if (projectInfo.TestCommands.Count > 0)
            {
                md.AppendLine("### Test");
                md.AppendLine();
                foreach (var cmd in projectInfo.TestCommands)
                {
                    md.AppendLine("```bash");
                    md.AppendLine(cmd);
                    md.AppendLine("```");
                }
                md.AppendLine();
            }
        }

        // Testing
        if (repoAnalysis.TestingFrameworks.Count > 0)
        {
            md.AppendLine("## Testing");
            md.AppendLine();
            md.AppendLine($"This project uses: {string.Join(", ", repoAnalysis.TestingFrameworks)}.");
            md.AppendLine();
            foreach (var tf in repoAnalysis.TestingFrameworks)
            {
                var guidance = GetTestingGuidance(tf);
                if (guidance is not null) md.AppendLine($"- {guidance}");
            }
            md.AppendLine();
        }

        // CI/CD
        if (repoAnalysis.CiCdTools.Count > 0)
        {
            md.AppendLine("## CI/CD");
            md.AppendLine();
            md.AppendLine($"This project uses {string.Join(", ", repoAnalysis.CiCdTools)} for continuous integration.");
            if (repoAnalysis.CiCdTools.Contains("GitHub Actions"))
            {
                md.AppendLine("- Check `.github/workflows/` for workflow definitions.");
                md.AppendLine("- Ensure all CI checks pass before merging pull requests.");
            }
            md.AppendLine();
        }

        // Docker
        if (repoAnalysis.HasDocker)
        {
            md.AppendLine("## Docker");
            md.AppendLine();
            md.AppendLine("This project includes Docker configuration.");
            md.AppendLine("- Use `docker build` to build the container image.");
            md.AppendLine("- Use `docker compose up` to start services (if docker-compose is present).");
            md.AppendLine();
        }

        return md.ToString();
    }

    private static string? GetTestingGuidance(string framework)
    {
        return framework switch
        {
            "Jest" => "Run tests with `npm test` or `npx jest`. Place test files alongside source with `.test.ts`/`.test.js` suffix.",
            "Vitest" => "Run tests with `npx vitest`. Uses Vite-native test runner with Jest-compatible API.",
            "Mocha" => "Run tests with `npx mocha`. Test files typically in `test/` directory.",
            "Playwright" => "Run E2E tests with `npx playwright test`. Page objects and fixtures recommended.",
            "Cypress" => "Run E2E tests with `npx cypress run`. Test files in `cypress/e2e/`.",
            "Testing Library" => "Use Testing Library queries (`getByRole`, `getByText`) for component tests. Prefer user-centric queries.",
            "xUnit" => "Run tests with `dotnet test`. Use `[Fact]` for single tests and `[Theory]` for parameterized tests.",
            "NUnit" => "Run tests with `dotnet test`. Use `[Test]` and `[TestCase]` attributes.",
            "MSTest" => "Run tests with `dotnet test`. Use `[TestMethod]` attribute.",
            "FluentAssertions" => "Use FluentAssertions for expressive assertions (e.g., `result.Should().Be(expected)`).",
            "pytest" => "Run tests with `pytest`. Test files use `test_` prefix. Use fixtures for setup/teardown.",
            "Go testing" => "Run tests with `go test ./...`. Test files use `_test.go` suffix.",
            "RSpec" => "Run tests with `bundle exec rspec`. Spec files in `spec/` directory.",
            "JUnit" => "Run tests with `mvn test` or `gradle test`. Use `@Test` annotation.",
            _ => null
        };
    }

    private static async Task<string?> FindInPathAsync()
    {
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which";
        var arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "copilot" : "copilot";

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                // Return the first line (in case multiple matches)
                return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            }
        }
        catch
        {
            // Ignore errors, CLI not in PATH
        }

        return null;
    }

    private static string? FindCopilotExtension(string basePath)
    {
        try
        {
            var extensions = Directory.GetDirectories(basePath, "github.copilot-*", SearchOption.TopDirectoryOnly);
            foreach (var ext in extensions.OrderByDescending(x => x))
            {
                // Look for copilot executable in the extension
                var possiblePaths = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? new[] { Path.Combine(ext, "dist", "copilot.exe"), Path.Combine(ext, "copilot.exe") }
                    : new[] { Path.Combine(ext, "dist", "copilot"), Path.Combine(ext, "copilot") };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }
        }
        catch
        {
            // Ignore errors accessing directories
        }

        return null;
    }

    private static async Task<ProjectInfo> AnalyzeProjectStructureAsync(
        string repoPath,
        CancellationToken cancellationToken)
    {
        var info = new ProjectInfo();

        // Read README for description (extract more content)
        var readmePath = Directory.GetFiles(repoPath, "README.md", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (readmePath is not null)
        {
            var readmeContent = await File.ReadAllTextAsync(readmePath, cancellationToken);
            info.Description = ExtractDescriptionFromReadme(readmeContent);
        }

        // Detect .NET projects
        var csprojFiles = Directory.GetFiles(repoPath, "*.csproj", SearchOption.AllDirectories);
        var slnFiles = Directory.GetFiles(repoPath, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(repoPath, "*.slnx", SearchOption.TopDirectoryOnly)).ToArray();
        if (csprojFiles.Length > 0 || slnFiles.Length > 0)
        {
            info.TechStack.Add(".NET / C#");
            if (slnFiles.Length > 0)
                info.BuildCommands.Add($"dotnet build {Path.GetFileName(slnFiles[0])}");
            else
                info.BuildCommands.Add("dotnet build");
            info.TestCommands.Add("dotnet test");

            // Scan csproj for conventions
            foreach (var csproj in csprojFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(csproj, cancellationToken);
                    if (content.Contains("<Nullable>enable</Nullable>", StringComparison.OrdinalIgnoreCase))
                        AddUnique(info.Conventions, "Nullable reference types are enabled — avoid null where possible");
                    if (content.Contains("<ImplicitUsings>enable</ImplicitUsings>", StringComparison.OrdinalIgnoreCase))
                        AddUnique(info.Conventions, "Implicit usings are enabled");
                    if (content.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", StringComparison.OrdinalIgnoreCase))
                        AddUnique(info.Conventions, "Warnings are treated as errors — fix all warnings");
                    if (content.Contains("net10.0", StringComparison.OrdinalIgnoreCase))
                        AddUnique(info.TechStack, ".NET 10");
                    else if (content.Contains("net9.0", StringComparison.OrdinalIgnoreCase))
                        AddUnique(info.TechStack, ".NET 9");
                    else if (content.Contains("net8.0", StringComparison.OrdinalIgnoreCase))
                        AddUnique(info.TechStack, ".NET 8");
                }
                catch { }
            }

            AddUnique(info.Conventions, "Follow standard .NET naming conventions (PascalCase for public members)");
            AddUnique(info.Conventions, "Use async/await for I/O operations");
        }

        // Detect Node.js/TypeScript projects
        var packageJsonPath = Path.Combine(repoPath, "package.json");
        if (File.Exists(packageJsonPath))
        {
            var packageContent = await File.ReadAllTextAsync(packageJsonPath, cancellationToken);
            var packageJson = JsonDocument.Parse(packageContent);

            if (packageJson.RootElement.TryGetProperty("description", out var desc))
            {
                info.Description ??= desc.GetString();
            }

            info.TechStack.Add("Node.js");

            if (File.Exists(Path.Combine(repoPath, "tsconfig.json")))
            {
                info.TechStack.Add("TypeScript");
                AddUnique(info.Conventions, "Use TypeScript for type safety — avoid `any` where possible");
            }

            // Extract scripts
            if (packageJson.RootElement.TryGetProperty("scripts", out var scripts))
            {
                if (scripts.TryGetProperty("build", out _)) info.BuildCommands.Add("npm run build");
                if (scripts.TryGetProperty("dev", out _)) info.BuildCommands.Add("npm run dev");
                if (scripts.TryGetProperty("test", out _)) info.TestCommands.Add("npm test");
                if (scripts.TryGetProperty("lint", out _)) AddUnique(info.Conventions, "Run `npm run lint` before committing");
                if (scripts.TryGetProperty("format", out _)) AddUnique(info.Conventions, "Run `npm run format` to auto-format code");
            }

            // Detect code style tools
            if (packageJson.RootElement.TryGetProperty("devDependencies", out var devDeps))
            {
                var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in devDeps.EnumerateObject()) deps.Add(prop.Name);
                if (deps.Contains("eslint")) AddUnique(info.Conventions, "ESLint is configured — follow the project's lint rules");
                if (deps.Contains("prettier")) AddUnique(info.Conventions, "Prettier is configured — code is auto-formatted");
                if (deps.Contains("tailwindcss")) AddUnique(info.TechStack, "Tailwind CSS");
            }
        }

        // Detect Python projects
        var requirementsTxt = Path.Combine(repoPath, "requirements.txt");
        var pyprojectToml = Path.Combine(repoPath, "pyproject.toml");
        if (File.Exists(requirementsTxt) || File.Exists(pyprojectToml))
        {
            info.TechStack.Add("Python");
            info.TestCommands.Add("pytest");
            AddUnique(info.Conventions, "Follow PEP 8 style guide");
            AddUnique(info.Conventions, "Use type hints for function signatures");

            if (File.Exists(pyprojectToml))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(pyprojectToml, cancellationToken);
                    if (content.Contains("ruff", StringComparison.OrdinalIgnoreCase))
                        AddUnique(info.Conventions, "Ruff is configured for linting/formatting — run `ruff check` and `ruff format`");
                    if (content.Contains("black", StringComparison.OrdinalIgnoreCase))
                        AddUnique(info.Conventions, "Black is configured for formatting");
                    if (content.Contains("mypy", StringComparison.OrdinalIgnoreCase))
                        AddUnique(info.Conventions, "mypy is configured for type checking");
                }
                catch { }
            }
        }

        // Detect Go projects
        if (File.Exists(Path.Combine(repoPath, "go.mod")))
        {
            info.TechStack.Add("Go");
            info.BuildCommands.Add("go build ./...");
            info.TestCommands.Add("go test ./...");
            AddUnique(info.Conventions, "Follow Go conventions — use `gofmt` and `go vet`");
            AddUnique(info.Conventions, "Error handling: always check returned errors");
        }

        // Detect Rust projects
        if (File.Exists(Path.Combine(repoPath, "Cargo.toml")))
        {
            info.TechStack.Add("Rust");
            info.BuildCommands.Add("cargo build");
            info.TestCommands.Add("cargo test");
            AddUnique(info.Conventions, "Use `cargo clippy` for linting and `cargo fmt` for formatting");
        }

        // Detect Java projects
        if (File.Exists(Path.Combine(repoPath, "pom.xml")))
        {
            info.TechStack.Add("Java / Maven");
            info.BuildCommands.Add("mvn package");
            info.TestCommands.Add("mvn test");
        }
        else if (File.Exists(Path.Combine(repoPath, "build.gradle")) || File.Exists(Path.Combine(repoPath, "build.gradle.kts")))
        {
            info.TechStack.Add("Java / Gradle");
            info.BuildCommands.Add("./gradlew build");
            info.TestCommands.Add("./gradlew test");
        }

        // Detect Ruby projects
        if (File.Exists(Path.Combine(repoPath, "Gemfile")))
        {
            info.TechStack.Add("Ruby");
            info.BuildCommands.Add("bundle install");
            if (File.Exists(Path.Combine(repoPath, ".rspec")))
                info.TestCommands.Add("bundle exec rspec");
        }

        // Detect editorconfig (coding style)
        if (File.Exists(Path.Combine(repoPath, ".editorconfig")))
        {
            AddUnique(info.Conventions, "An `.editorconfig` is present — editors should respect it for indentation and formatting");
        }

        // Default conventions if none detected
        if (info.Conventions.Count == 0)
        {
            info.Conventions.Add("Write clean, maintainable code");
            info.Conventions.Add("Include appropriate error handling");
            info.Conventions.Add("Add comments for complex logic");
        }

        return info;
    }

    private static void AddUnique(List<string> list, string item)
    {
        if (!list.Contains(item)) list.Add(item);
    }

    private static string? ExtractDescriptionFromReadme(string readmeContent)
    {
        var lines = readmeContent.Split('\n');
        var paragraphs = new List<string>();
        var currentParagraph = new StringBuilder();

        foreach (var line in lines.Skip(1)) // Skip title line
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#'))
            {
                // Save current paragraph if we have one, then stop at next header
                if (currentParagraph.Length > 0)
                    break;
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                if (currentParagraph.Length > 0)
                {
                    paragraphs.Add(currentParagraph.ToString().Trim());
                    currentParagraph.Clear();
                    if (paragraphs.Count >= 2) break; // Grab up to 2 paragraphs
                }
                continue;
            }

            // Skip badge lines and links-only lines
            if (trimmed.StartsWith("[![") || trimmed.StartsWith("![") || (trimmed.StartsWith("[") && trimmed.EndsWith(")")))
                continue;

            if (currentParagraph.Length > 0) currentParagraph.Append(' ');
            currentParagraph.Append(trimmed);
        }

        if (currentParagraph.Length > 0 && paragraphs.Count < 2)
            paragraphs.Add(currentParagraph.ToString().Trim());

        return paragraphs.Count > 0 ? string.Join("\n\n", paragraphs) : null;
    }

    private sealed class ProjectInfo
    {
        public string? Description { get; set; }
        public List<string> TechStack { get; } = new();
        public List<string> Conventions { get; } = new();
        public List<string> BuildCommands { get; } = new();
        public List<string> TestCommands { get; } = new();
    }
}

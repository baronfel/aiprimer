using System.Text.Json;
using System.Text.RegularExpressions;

namespace Primer.Services;

/// <summary>
/// Represents a key directory discovered during analysis.
/// </summary>
public record KeyDirectory(string Path, string Description);

/// <summary>
/// Represents file count statistics grouped by language category.
/// </summary>
public record FileStats(Dictionary<string, int> CountsByCategory, int TotalFiles);

/// <summary>
/// Represents the analysis result of a repository.
/// </summary>
public record RepoAnalysis(
    string Path,
    bool IsGitRepo,
    List<string> Languages,
    List<string> Frameworks,
    string? PackageManager,
    List<string> TestingFrameworks,
    List<string> CiCdTools,
    bool HasDocker,
    FileStats FileStats,
    List<KeyDirectory> KeyDirectories);

/// <summary>
/// Service for analyzing repository structure and detecting languages, frameworks, and package managers.
/// </summary>
public static class AnalyzerService
{
    private static readonly (string File, string Name)[] PackageManagers =
    [
        ("pnpm-lock.yaml", "pnpm"),
        ("yarn.lock", "yarn"),
        ("package-lock.json", "npm"),
        ("bun.lockb", "bun"),
        ("Pipfile.lock", "pipenv"),
        ("poetry.lock", "poetry"),
        ("uv.lock", "uv")
    ];

    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "bin", "obj", "dist", "build", "out", ".next",
        "__pycache__", ".venv", "venv", "env", ".tox", "vendor", "target",
        ".vs", ".idea", "packages", "coverage", ".nyc_output", ".cache"
    };

    private static readonly Dictionary<string, string> ExtensionToCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#", [".fs"] = "F#", [".vb"] = "VB.NET",
        [".js"] = "JavaScript", [".jsx"] = "JavaScript", [".mjs"] = "JavaScript", [".cjs"] = "JavaScript",
        [".ts"] = "TypeScript", [".tsx"] = "TypeScript", [".mts"] = "TypeScript",
        [".py"] = "Python", [".pyw"] = "Python",
        [".go"] = "Go",
        [".rs"] = "Rust",
        [".java"] = "Java", [".kt"] = "Kotlin", [".kts"] = "Kotlin",
        [".rb"] = "Ruby",
        [".php"] = "PHP",
        [".swift"] = "Swift",
        [".cpp"] = "C++", [".cc"] = "C++", [".cxx"] = "C++", [".h"] = "C/C++ Header", [".hpp"] = "C++",
        [".c"] = "C",
        [".sql"] = "SQL",
        [".md"] = "Markdown", [".mdx"] = "Markdown",
        [".json"] = "JSON", [".yaml"] = "YAML", [".yml"] = "YAML", [".toml"] = "TOML",
        [".html"] = "HTML", [".htm"] = "HTML",
        [".css"] = "CSS", [".scss"] = "SCSS", [".less"] = "Less",
        [".sh"] = "Shell", [".bash"] = "Shell", [".zsh"] = "Shell", [".ps1"] = "PowerShell",
    };

    /// <summary>
    /// Analyzes a repository to detect languages, frameworks, package managers, testing, CI/CD, and more.
    /// </summary>
    public static RepoAnalysis AnalyzeRepo(string repoPath)
    {
        ArgumentNullException.ThrowIfNull(repoPath);

        if (!Directory.Exists(repoPath))
        {
            throw new DirectoryNotFoundException($"Repository path does not exist: {repoPath}");
        }

        var rootFiles = SafeReadDir(repoPath);
        var allFiles = CollectAllFiles(repoPath);
        var languages = DetectLanguages(repoPath, rootFiles, allFiles);
        var frameworks = DetectFrameworks(repoPath, rootFiles, allFiles);
        var packageManager = DetectPackageManager(repoPath, rootFiles, allFiles);
        var testingFrameworks = DetectTestingFrameworks(repoPath, rootFiles, allFiles);
        var ciCdTools = DetectCiCd(repoPath, rootFiles);
        var hasDocker = rootFiles.Any(f => f.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase))
                     || rootFiles.Any(f => f.StartsWith("docker-compose", StringComparison.OrdinalIgnoreCase))
                     || rootFiles.Any(f => f.Equals(".dockerignore", StringComparison.OrdinalIgnoreCase))
                     || allFiles.Any(f => System.IO.Path.GetFileName(f).Equals("Dockerfile", StringComparison.OrdinalIgnoreCase));
        var fileStats = ComputeFileStats(allFiles);
        var keyDirectories = DetectKeyDirectories(repoPath);
        var isGitRepo = GitService.IsGitRepo(repoPath);

        return new RepoAnalysis(repoPath, isGitRepo, languages, frameworks, packageManager,
            testingFrameworks, ciCdTools, hasDocker, fileStats, keyDirectories);
    }

    /// <summary>
    /// Recursively collects all file paths, skipping common non-source directories.
    /// </summary>
    private static List<string> CollectAllFiles(string root)
    {
        var results = new List<string>();
        CollectFilesRecursive(root, results);
        return results;
    }

    private static void CollectFilesRecursive(string dir, List<string> results)
    {
        try
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                results.Add(file);
            }

            foreach (var subDir in Directory.GetDirectories(dir))
            {
                var dirName = System.IO.Path.GetFileName(subDir);
                if (dirName is not null && !SkipDirs.Contains(dirName) && !dirName.StartsWith('.'))
                {
                    CollectFilesRecursive(subDir, results);
                }
            }
        }
        catch
        {
            // Skip inaccessible directories
        }
    }

    private static List<string> SafeReadDir(string path)
    {
        try
        {
            return Directory.GetFileSystemEntries(path)
                .Select(System.IO.Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static List<string> DetectLanguages(string repoPath, List<string> rootFiles, List<string> allFiles)
    {
        var languages = new List<string>();

        if (rootFiles.Contains("package.json")) languages.Add("JavaScript");
        if (rootFiles.Contains("tsconfig.json") || allFiles.Any(f => f.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)))
            if (!languages.Contains("TypeScript")) languages.Add("TypeScript");
        if (rootFiles.Contains("pyproject.toml") || rootFiles.Contains("requirements.txt") || rootFiles.Contains("setup.py") || rootFiles.Contains("Pipfile"))
            languages.Add("Python");
        if (rootFiles.Contains("go.mod")) languages.Add("Go");
        if (rootFiles.Contains("Cargo.toml")) languages.Add("Rust");
        if (allFiles.Any(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)))
            languages.Add("C#");
        if (allFiles.Any(f => f.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)))
            languages.Add("F#");
        if (rootFiles.Contains("pom.xml") || rootFiles.Contains("build.gradle") || rootFiles.Contains("build.gradle.kts"))
            languages.Add("Java");
        if (rootFiles.Contains("Gemfile") || allFiles.Any(f => f.EndsWith(".rb", StringComparison.OrdinalIgnoreCase)))
            if (!languages.Contains("Ruby")) languages.Add("Ruby");
        if (rootFiles.Contains("composer.json"))
            languages.Add("PHP");
        if (rootFiles.Contains("Package.swift"))
            languages.Add("Swift");
        if (allFiles.Any(f => f.EndsWith(".kt", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".kts", StringComparison.OrdinalIgnoreCase))
            && !languages.Contains("Kotlin"))
            languages.Add("Kotlin");

        return languages;
    }

    private static List<string> DetectFrameworks(string repoPath, List<string> rootFiles, List<string> allFiles)
    {
        var frameworks = new List<string>();

        // JS/TS frameworks from package.json
        if (rootFiles.Contains("package.json"))
        {
            frameworks.AddRange(DetectJsFrameworks(repoPath));
        }

        // Python frameworks
        DetectPythonFrameworks(repoPath, rootFiles, frameworks);

        // .NET frameworks from csproj files
        DetectDotNetFrameworks(allFiles, frameworks);

        // Go frameworks from go.mod
        DetectGoFrameworks(repoPath, rootFiles, frameworks);

        // Java frameworks from pom.xml
        DetectJavaFrameworks(repoPath, rootFiles, frameworks);

        // Ruby frameworks
        DetectRubyFrameworks(repoPath, rootFiles, frameworks);

        return frameworks;
    }

    private static List<string> DetectJsFrameworks(string repoPath)
    {
        var frameworks = new List<string>();
        var packageJsonPath = System.IO.Path.Combine(repoPath, "package.json");

        try
        {
            var content = File.ReadAllText(packageJsonPath);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var allDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (root.TryGetProperty("dependencies", out var deps))
                foreach (var prop in deps.EnumerateObject()) allDeps.Add(prop.Name);
            if (root.TryGetProperty("devDependencies", out var devDeps))
                foreach (var prop in devDeps.EnumerateObject()) allDeps.Add(prop.Name);

            if (allDeps.Contains("next")) frameworks.Add("Next.js");
            else if (allDeps.Contains("react")) frameworks.Add("React");
            if (allDeps.Contains("vue")) frameworks.Add("Vue");
            if (allDeps.Contains("@angular/core")) frameworks.Add("Angular");
            if (allDeps.Contains("svelte")) frameworks.Add("Svelte");
            if (allDeps.Contains("express")) frameworks.Add("Express");
            if (allDeps.Contains("@nestjs/core")) frameworks.Add("NestJS");
            if (allDeps.Contains("fastify")) frameworks.Add("Fastify");
            if (allDeps.Contains("hono")) frameworks.Add("Hono");
            if (allDeps.Contains("astro")) frameworks.Add("Astro");
            if (allDeps.Contains("nuxt")) frameworks.Add("Nuxt");
            if (allDeps.Contains("remix") || allDeps.Contains("@remix-run/node")) frameworks.Add("Remix");
            if (allDeps.Contains("electron")) frameworks.Add("Electron");
            if (allDeps.Contains("vite")) frameworks.Add("Vite");
        }
        catch { }

        return frameworks;
    }

    private static void DetectPythonFrameworks(string repoPath, List<string> rootFiles, List<string> frameworks)
    {
        // Check requirements.txt
        var reqPath = System.IO.Path.Combine(repoPath, "requirements.txt");
        if (File.Exists(reqPath))
        {
            try
            {
                var content = File.ReadAllText(reqPath).ToLowerInvariant();
                if (content.Contains("django")) frameworks.Add("Django");
                if (content.Contains("flask")) frameworks.Add("Flask");
                if (content.Contains("fastapi")) frameworks.Add("FastAPI");
                if (content.Contains("celery")) frameworks.Add("Celery");
                if (content.Contains("sqlalchemy")) frameworks.Add("SQLAlchemy");
            }
            catch { }
        }

        // Check pyproject.toml
        var pyprojectPath = System.IO.Path.Combine(repoPath, "pyproject.toml");
        if (File.Exists(pyprojectPath))
        {
            try
            {
                var content = File.ReadAllText(pyprojectPath).ToLowerInvariant();
                if (content.Contains("django") && !frameworks.Contains("Django")) frameworks.Add("Django");
                if (content.Contains("flask") && !frameworks.Contains("Flask")) frameworks.Add("Flask");
                if (content.Contains("fastapi") && !frameworks.Contains("FastAPI")) frameworks.Add("FastAPI");
            }
            catch { }
        }

        // Check for manage.py (Django)
        if (rootFiles.Contains("manage.py") && !frameworks.Contains("Django")) frameworks.Add("Django");
    }

    private static void DetectDotNetFrameworks(List<string> allFiles, List<string> frameworks)
    {
        foreach (var csproj in allFiles.Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var content = File.ReadAllText(csproj);
                if (content.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
                    if (!frameworks.Contains("ASP.NET Core")) frameworks.Add("ASP.NET Core");
                if (content.Contains("Microsoft.NET.Sdk.BlazorWebAssembly", StringComparison.OrdinalIgnoreCase)
                    || content.Contains("Microsoft.AspNetCore.Components", StringComparison.OrdinalIgnoreCase))
                    if (!frameworks.Contains("Blazor")) frameworks.Add("Blazor");
                if (content.Contains("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase))
                    if (!frameworks.Contains(".NET Worker Service")) frameworks.Add(".NET Worker Service");
                if (content.Contains("Microsoft.Maui", StringComparison.OrdinalIgnoreCase))
                    if (!frameworks.Contains("MAUI")) frameworks.Add("MAUI");
                if (content.Contains("Microsoft.NET.Sdk.WindowsDesktop", StringComparison.OrdinalIgnoreCase)
                    || content.Contains("UseWPF", StringComparison.OrdinalIgnoreCase))
                    if (!frameworks.Contains("WPF")) frameworks.Add("WPF");
                if (content.Contains("Spectre.Console", StringComparison.OrdinalIgnoreCase))
                    if (!frameworks.Contains("Spectre.Console")) frameworks.Add("Spectre.Console");
                if (content.Contains("System.CommandLine", StringComparison.OrdinalIgnoreCase))
                    if (!frameworks.Contains("System.CommandLine")) frameworks.Add("System.CommandLine");
            }
            catch { }
        }
    }

    private static void DetectGoFrameworks(string repoPath, List<string> rootFiles, List<string> frameworks)
    {
        if (!rootFiles.Contains("go.mod")) return;

        try
        {
            var content = File.ReadAllText(System.IO.Path.Combine(repoPath, "go.mod"));
            if (content.Contains("gin-gonic/gin")) frameworks.Add("Gin");
            if (content.Contains("labstack/echo")) frameworks.Add("Echo");
            if (content.Contains("gofiber/fiber")) frameworks.Add("Fiber");
            if (content.Contains("gorilla/mux")) frameworks.Add("Gorilla Mux");
        }
        catch { }
    }

    private static void DetectJavaFrameworks(string repoPath, List<string> rootFiles, List<string> frameworks)
    {
        var pomPath = System.IO.Path.Combine(repoPath, "pom.xml");
        if (File.Exists(pomPath))
        {
            try
            {
                var content = File.ReadAllText(pomPath);
                if (content.Contains("spring-boot", StringComparison.OrdinalIgnoreCase)) frameworks.Add("Spring Boot");
                if (content.Contains("quarkus", StringComparison.OrdinalIgnoreCase)) frameworks.Add("Quarkus");
                if (content.Contains("micronaut", StringComparison.OrdinalIgnoreCase)) frameworks.Add("Micronaut");
            }
            catch { }
        }

        var gradlePath = System.IO.Path.Combine(repoPath, "build.gradle");
        if (!File.Exists(gradlePath)) gradlePath = System.IO.Path.Combine(repoPath, "build.gradle.kts");
        if (File.Exists(gradlePath))
        {
            try
            {
                var content = File.ReadAllText(gradlePath);
                if (content.Contains("spring-boot", StringComparison.OrdinalIgnoreCase) && !frameworks.Contains("Spring Boot"))
                    frameworks.Add("Spring Boot");
                if (content.Contains("android", StringComparison.OrdinalIgnoreCase)) frameworks.Add("Android");
            }
            catch { }
        }
    }

    private static void DetectRubyFrameworks(string repoPath, List<string> rootFiles, List<string> frameworks)
    {
        if (rootFiles.Contains("Gemfile"))
        {
            try
            {
                var content = File.ReadAllText(System.IO.Path.Combine(repoPath, "Gemfile"));
                if (content.Contains("rails", StringComparison.OrdinalIgnoreCase)) frameworks.Add("Ruby on Rails");
                if (content.Contains("sinatra", StringComparison.OrdinalIgnoreCase)) frameworks.Add("Sinatra");
            }
            catch { }
        }
    }

    private static string? DetectPackageManager(string repoPath, List<string> rootFiles, List<string> allFiles)
    {
        foreach (var (file, name) in PackageManagers)
        {
            if (rootFiles.Contains(file))
            {
                return name;
            }
        }

        // .NET — check both root and recursive
        if (rootFiles.Any(f => f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            || allFiles.Any(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
            return "NuGet";
        if (rootFiles.Contains("go.mod")) return "Go Modules";
        if (rootFiles.Contains("Cargo.toml")) return "Cargo";
        if (rootFiles.Contains("pom.xml")) return "Maven";
        if (rootFiles.Contains("build.gradle") || rootFiles.Contains("build.gradle.kts")) return "Gradle";
        if (rootFiles.Contains("Gemfile")) return "Bundler";
        if (rootFiles.Contains("composer.json")) return "Composer";

        return null;
    }

    private static List<string> DetectTestingFrameworks(string repoPath, List<string> rootFiles, List<string> allFiles)
    {
        var testing = new List<string>();

        // JS/TS testing
        if (rootFiles.Contains("package.json"))
        {
            try
            {
                var content = File.ReadAllText(System.IO.Path.Combine(repoPath, "package.json"));
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                var allDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("dependencies", out var deps))
                    foreach (var prop in deps.EnumerateObject()) allDeps.Add(prop.Name);
                if (root.TryGetProperty("devDependencies", out var devDeps))
                    foreach (var prop in devDeps.EnumerateObject()) allDeps.Add(prop.Name);

                if (allDeps.Contains("jest")) testing.Add("Jest");
                if (allDeps.Contains("mocha")) testing.Add("Mocha");
                if (allDeps.Contains("vitest")) testing.Add("Vitest");
                if (allDeps.Contains("@playwright/test") || allDeps.Contains("playwright")) testing.Add("Playwright");
                if (allDeps.Contains("cypress")) testing.Add("Cypress");
                if (allDeps.Contains("@testing-library/react") || allDeps.Contains("@testing-library/jest-dom")) testing.Add("Testing Library");
                if (allDeps.Contains("ava")) testing.Add("Ava");
            }
            catch { }
        }

        // .NET testing
        foreach (var csproj in allFiles.Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var content = File.ReadAllText(csproj);
                if (content.Contains("xunit", StringComparison.OrdinalIgnoreCase) && !testing.Contains("xUnit")) testing.Add("xUnit");
                if (content.Contains("nunit", StringComparison.OrdinalIgnoreCase) && !testing.Contains("NUnit")) testing.Add("NUnit");
                if (content.Contains("MSTest", StringComparison.OrdinalIgnoreCase) && !testing.Contains("MSTest")) testing.Add("MSTest");
                if (content.Contains("FluentAssertions", StringComparison.OrdinalIgnoreCase) && !testing.Contains("FluentAssertions")) testing.Add("FluentAssertions");
                if (content.Contains("Moq", StringComparison.OrdinalIgnoreCase) && !testing.Contains("Moq")) testing.Add("Moq");
                if (content.Contains("NSubstitute", StringComparison.OrdinalIgnoreCase) && !testing.Contains("NSubstitute")) testing.Add("NSubstitute");
            }
            catch { }
        }

        // Python testing
        if (rootFiles.Contains("pytest.ini") || rootFiles.Contains("conftest.py") || rootFiles.Contains("setup.cfg") || rootFiles.Contains("tox.ini"))
        {
            if (!testing.Contains("pytest")) testing.Add("pytest");
        }
        if (allFiles.Any(f => System.IO.Path.GetFileName(f).StartsWith("test_", StringComparison.OrdinalIgnoreCase)))
        {
            if (!testing.Contains("pytest")) testing.Add("pytest");
        }

        // Go testing (convention: _test.go files)
        if (allFiles.Any(f => f.EndsWith("_test.go", StringComparison.OrdinalIgnoreCase)))
        {
            testing.Add("Go testing");
        }

        // Ruby testing
        if (rootFiles.Contains(".rspec") || allFiles.Any(f => f.Contains("spec", StringComparison.OrdinalIgnoreCase) && f.EndsWith("_spec.rb", StringComparison.OrdinalIgnoreCase)))
        {
            testing.Add("RSpec");
        }

        // Java testing
        foreach (var file in new[] { "pom.xml", "build.gradle", "build.gradle.kts" })
        {
            var path = System.IO.Path.Combine(repoPath, file);
            if (File.Exists(path))
            {
                try
                {
                    var content = File.ReadAllText(path);
                    if (content.Contains("junit", StringComparison.OrdinalIgnoreCase) && !testing.Contains("JUnit")) testing.Add("JUnit");
                    if (content.Contains("mockito", StringComparison.OrdinalIgnoreCase) && !testing.Contains("Mockito")) testing.Add("Mockito");
                }
                catch { }
            }
        }

        return testing;
    }

    private static List<string> DetectCiCd(string repoPath, List<string> rootFiles)
    {
        var cicd = new List<string>();

        if (Directory.Exists(System.IO.Path.Combine(repoPath, ".github", "workflows")))
            cicd.Add("GitHub Actions");
        if (rootFiles.Contains("azure-pipelines.yml") || rootFiles.Contains("azure-pipelines.yaml"))
            cicd.Add("Azure Pipelines");
        if (rootFiles.Contains("Jenkinsfile"))
            cicd.Add("Jenkins");
        if (rootFiles.Contains(".travis.yml"))
            cicd.Add("Travis CI");
        if (rootFiles.Contains(".circleci") || Directory.Exists(System.IO.Path.Combine(repoPath, ".circleci")))
            cicd.Add("CircleCI");
        if (rootFiles.Contains(".gitlab-ci.yml"))
            cicd.Add("GitLab CI");
        if (rootFiles.Contains("bitbucket-pipelines.yml"))
            cicd.Add("Bitbucket Pipelines");

        return cicd;
    }

    private static FileStats ComputeFileStats(List<string> allFiles)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var total = 0;

        foreach (var file in allFiles)
        {
            var ext = System.IO.Path.GetExtension(file);
            if (string.IsNullOrEmpty(ext)) continue;

            if (ExtensionToCategory.TryGetValue(ext, out var category))
            {
                counts[category] = counts.GetValueOrDefault(category) + 1;
                total++;
            }
        }

        return new FileStats(counts, total);
    }

    private static List<KeyDirectory> DetectKeyDirectories(string repoPath)
    {
        var dirs = new List<KeyDirectory>();

        var knownDirs = new (string Name, string Description)[]
        {
            ("src", "Source code"),
            ("lib", "Library code"),
            ("app", "Application code"),
            ("pages", "Page components/routes"),
            ("components", "UI components"),
            ("api", "API endpoints"),
            ("server", "Server-side code"),
            ("client", "Client-side code"),
            ("services", "Service layer"),
            ("models", "Data models"),
            ("controllers", "Controllers"),
            ("middleware", "Middleware"),
            ("utils", "Utility functions"),
            ("helpers", "Helper functions"),
            ("config", "Configuration"),
            ("scripts", "Scripts and tooling"),
            ("tests", "Tests"),
            ("test", "Tests"),
            ("__tests__", "Tests"),
            ("spec", "Test specifications"),
            ("docs", "Documentation"),
            ("public", "Static/public assets"),
            ("static", "Static assets"),
            ("assets", "Assets (images, fonts, etc.)"),
            ("migrations", "Database migrations"),
            ("templates", "Templates"),
            ("views", "Views/templates"),
            (".github", "GitHub configuration"),
            (".vscode", "VS Code configuration"),
            ("docker", "Docker configuration"),
            ("deploy", "Deployment configuration"),
            ("infra", "Infrastructure as code"),
            ("terraform", "Terraform configuration"),
        };

        foreach (var (name, description) in knownDirs)
        {
            var dirPath = System.IO.Path.Combine(repoPath, name);
            if (Directory.Exists(dirPath))
            {
                dirs.Add(new KeyDirectory($"{name}/", description));
            }
        }

        return dirs;
    }
}

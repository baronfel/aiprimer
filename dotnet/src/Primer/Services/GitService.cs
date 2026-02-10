using System.Diagnostics;
using System.Text;
using LibGit2Sharp;

namespace Primer.Services;

/// <summary>
/// Provides Git operations using LibGit2Sharp for local operations and Git CLI for remote operations.
/// </summary>
public static class GitService
{
    /// <summary>
    /// Checks if the specified path is a Git repository.
    /// </summary>
    /// <param name="repoPath">Path to check for Git repository.</param>
    /// <returns>True if the path contains a .git directory, false otherwise.</returns>
    public static bool IsGitRepo(string repoPath)
    {
        ArgumentNullException.ThrowIfNull(repoPath);

        var gitDir = Path.Combine(repoPath, ".git");
        return Directory.Exists(gitDir);
    }

    /// <summary>
    /// Checks if the specified path is a Git repository.
    /// </summary>
    /// <param name="repoPath">Path to check for Git repository.</param>
    /// <returns>True if the path contains a .git directory, false otherwise.</returns>
    public static Task<bool> IsGitRepoAsync(string repoPath)
    {
        ArgumentNullException.ThrowIfNull(repoPath);

        var gitDir = Path.Combine(repoPath, ".git");
        var isRepo = Directory.Exists(gitDir);
        return Task.FromResult(isRepo);
    }

    /// <summary>
    /// Gets the root directory of the Git repository.
    /// </summary>
    /// <param name="repoPath">Path within the repository.</param>
    /// <returns>The root path of the repository.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the path is not within a Git repository.</exception>
    public static Task<string> GetRepoRootAsync(string repoPath)
    {
        ArgumentNullException.ThrowIfNull(repoPath);

        var discoveredPath = Repository.Discover(repoPath);
        if (string.IsNullOrWhiteSpace(discoveredPath))
        {
            throw new InvalidOperationException($"Not a git repository (or any of the parent directories): {repoPath}");
        }

        // Repository.Discover returns path to .git directory, we need the parent
        var gitDir = new DirectoryInfo(discoveredPath);
        var repoRoot = gitDir.Parent?.FullName ?? gitDir.FullName;
        return Task.FromResult(repoRoot);
    }

    /// <summary>
    /// Clones a Git repository to the specified destination.
    /// </summary>
    /// <param name="repoUrl">URL of the repository to clone.</param>
    /// <param name="destination">Local destination path.</param>
    /// <param name="onProgress">Optional progress callback receiving message and percentage.</param>
    /// <param name="depth">Clone depth (1 for shallow clone, null for full clone).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when clone operation fails.</exception>
    public static async Task CloneRepoAsync(
        string repoUrl,
        string destination,
        Action<string, int>? onProgress = null,
        int? depth = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repoUrl);
        ArgumentNullException.ThrowIfNull(destination);

        var args = new StringBuilder();
        args.Append("clone");
        
        if (depth.HasValue)
        {
            args.Append($" --depth {depth.Value}");
        }
        
        args.Append($" \"{repoUrl}\" \"{destination}\"");

        await RunGitCommandAsync(
            args.ToString(),
            workingDirectory: Directory.GetCurrentDirectory(),
            onProgress: onProgress,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Checks out a branch in the repository, creating it if it doesn't exist.
    /// </summary>
    /// <param name="repoPath">Path to the repository.</param>
    /// <param name="branchName">Name of the branch to checkout.</param>
    /// <exception cref="InvalidOperationException">Thrown when checkout operation fails.</exception>
    public static Task CheckoutBranchAsync(string repoPath, string branchName)
    {
        ArgumentNullException.ThrowIfNull(repoPath);
        ArgumentNullException.ThrowIfNull(branchName);

        using var repo = new Repository(repoPath);
        
        // Check if branch already exists
        var branch = repo.Branches[branchName];
        
        if (branch == null)
        {
            // Create new branch from current HEAD
            var head = repo.Head;
            branch = repo.CreateBranch(branchName, head.Tip);
        }

        LibGit2Sharp.Commands.Checkout(repo, branch);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stages all changes and commits them with the specified message.
    /// </summary>
    /// <param name="repoPath">Path to the repository.</param>
    /// <param name="message">Commit message.</param>
    /// <exception cref="InvalidOperationException">Thrown when commit operation fails.</exception>
    public static Task CommitAllAsync(string repoPath, string message)
    {
        ArgumentNullException.ThrowIfNull(repoPath);
        ArgumentNullException.ThrowIfNull(message);

        using var repo = new Repository(repoPath);
        
        // Stage all changes (equivalent to git add -A)
        LibGit2Sharp.Commands.Stage(repo, "*");

        // Get signature for commit
        var signature = GetSignature(repo);

        // Commit staged changes
        repo.Commit(message, signature, signature);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Pushes a branch to the remote repository.
    /// </summary>
    /// <param name="repoPath">Path to the repository.</param>
    /// <param name="branchName">Name of the branch to push.</param>
    /// <param name="token">Optional authentication token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when push operation fails.</exception>
    public static async Task PushBranchAsync(
        string repoPath,
        string branchName,
        string? token = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repoPath);
        ArgumentNullException.ThrowIfNull(branchName);

        // Use git CLI for push to handle authentication tokens properly
        var args = new StringBuilder();
        args.Append($"push origin {branchName}");

        var env = new Dictionary<string, string>();
        
        // If token is provided, set up authentication
        if (!string.IsNullOrWhiteSpace(token))
        {
            // Configure Git to use the token as password with empty username
            env["GIT_ASKPASS"] = "echo";
            env["GIT_USERNAME"] = "x-access-token";
            env["GIT_PASSWORD"] = token;
        }

        await RunGitCommandAsync(
            args.ToString(),
            workingDirectory: repoPath,
            environmentVariables: env,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Gets a signature for commits, using Git config or defaults.
    /// </summary>
    private static Signature GetSignature(Repository repo)
    {
        var config = repo.Config;
        var name = config.GetValueOrDefault<string>("user.name", "Primer");
        var email = config.GetValueOrDefault<string>("user.email", "primer@example.com");
        
        return new Signature(name, email, DateTimeOffset.Now);
    }

    /// <summary>
    /// Runs a Git command using the Git CLI.
    /// </summary>
    private static async Task RunGitCommandAsync(
        string arguments,
        string workingDirectory,
        Action<string, int>? onProgress = null,
        Dictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Add environment variables if provided
        if (environmentVariables != null)
        {
            foreach (var (key, value) in environmentVariables)
            {
                startInfo.EnvironmentVariables[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                onProgress?.Invoke(e.Data, -1); // Progress percentage not available from git CLI
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
                // Git often writes progress to stderr, so also call onProgress
                onProgress?.Invoke(e.Data, -1);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = errorBuilder.ToString();
            var output = outputBuilder.ToString();
            var message = !string.IsNullOrWhiteSpace(error) ? error : output;
            throw new InvalidOperationException($"Git command failed: {message}");
        }
    }
}

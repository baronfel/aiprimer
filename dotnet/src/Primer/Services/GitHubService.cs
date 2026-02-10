using System.Diagnostics;
using System.Text;
using Octokit;

namespace Primer.Services;

/// <summary>
/// GitHub repository information
/// </summary>
public record GitHubRepo(
    string Name,
    string Owner,
    string FullName,
    string CloneUrl,
    bool IsPrivate,
    string DefaultBranch,
    bool? HasInstructions = null);

/// <summary>
/// GitHub organization information
/// </summary>
public record GitHubOrg(string Login, string? Name);

/// <summary>
/// Service for interacting with GitHub API using Octokit
/// </summary>
public static class GitHubService
{
    /// <summary>
    /// Gets GitHub token from environment variables or gh CLI
    /// </summary>
    /// <returns>GitHub token or null if not found</returns>
    public static async Task<string?> GetGitHubTokenAsync()
    {
        // Check GITHUB_TOKEN environment variable
        var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(githubToken))
        {
            return githubToken;
        }

        // Check GH_TOKEN environment variable
        var ghToken = Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(ghToken))
        {
            return ghToken;
        }

        // Try gh auth token command
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = "auth token",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                return output.Trim();
            }
        }
        catch
        {
            // gh CLI not available or failed
        }

        return null;
    }

    /// <summary>
    /// Creates a configured GitHubClient with the provided token
    /// </summary>
    /// <param name="token">GitHub personal access token</param>
    /// <returns>Configured GitHubClient instance</returns>
    public static GitHubClient CreateGitHubClient(string token)
    {
        var client = new GitHubClient(new ProductHeaderValue("Primer"))
        {
            Credentials = new Credentials(token)
        };
        return client;
    }

    /// <summary>
    /// Lists accessible repositories for the authenticated user
    /// </summary>
    /// <param name="token">GitHub personal access token</param>
    /// <param name="limit">Maximum number of repositories to return (default: 100)</param>
    /// <returns>Array of GitHubRepo records</returns>
    public static async Task<GitHubRepo[]> ListAccessibleReposAsync(string token, int limit = 100)
    {
        var client = CreateGitHubClient(token);

        var request = new RepositoryRequest
        {
            Sort = RepositorySort.Pushed,
            Direction = SortDirection.Descending
        };

        var options = new ApiOptions
        {
            PageSize = Math.Min(limit, 100),
            PageCount = 1
        };

        var repos = await client.Repository.GetAllForCurrent(request, options);

        return repos
            .Take(limit)
            .Select(r => new GitHubRepo(
                Name: r.Name,
                Owner: r.Owner.Login,
                FullName: r.FullName,
                CloneUrl: r.CloneUrl,
                IsPrivate: r.Private,
                DefaultBranch: r.DefaultBranch ?? "main"))
            .ToArray();
    }

    /// <summary>
    /// Gets information about a specific repository
    /// </summary>
    /// <param name="token">GitHub personal access token</param>
    /// <param name="owner">Repository owner</param>
    /// <param name="repo">Repository name</param>
    /// <returns>GitHubRepo record</returns>
    public static async Task<GitHubRepo> GetRepoAsync(string token, string owner, string repo)
    {
        var client = CreateGitHubClient(token);
        var repository = await client.Repository.Get(owner, repo);

        return new GitHubRepo(
            Name: repository.Name,
            Owner: repository.Owner.Login,
            FullName: repository.FullName,
            CloneUrl: repository.CloneUrl,
            IsPrivate: repository.Private,
            DefaultBranch: repository.DefaultBranch ?? "main");
    }

    /// <summary>
    /// Creates a pull request in the specified repository
    /// </summary>
    /// <param name="token">GitHub personal access token</param>
    /// <param name="owner">Repository owner</param>
    /// <param name="repo">Repository name</param>
    /// <param name="title">Pull request title</param>
    /// <param name="body">Pull request body</param>
    /// <param name="head">The name of the branch where changes are implemented</param>
    /// <param name="baseRef">The name of the branch to merge into</param>
    /// <returns>URL of the created pull request</returns>
    public static async Task<string> CreatePullRequestAsync(
        string token,
        string owner,
        string repo,
        string title,
        string body,
        string head,
        string baseRef)
    {
        var client = CreateGitHubClient(token);

        var newPullRequest = new NewPullRequest(title, head, baseRef)
        {
            Body = body
        };

        var pullRequest = await client.PullRequest.Create(owner, repo, newPullRequest);
        return pullRequest.HtmlUrl;
    }

    /// <summary>
    /// Lists organizations the authenticated user belongs to
    /// </summary>
    /// <param name="token">GitHub personal access token</param>
    /// <returns>Array of GitHubOrg records</returns>
    public static async Task<GitHubOrg[]> ListUserOrgsAsync(string token)
    {
        var client = CreateGitHubClient(token);
        var orgs = await client.Organization.GetAllForCurrent();

        return orgs
            .Select(o => new GitHubOrg(Login: o.Login, Name: o.Name))
            .ToArray();
    }

    /// <summary>
    /// Lists repositories for a specific organization
    /// </summary>
    /// <param name="token">GitHub personal access token</param>
    /// <param name="org">Organization name</param>
    /// <param name="limit">Maximum number of repositories to return (default: 100)</param>
    /// <returns>Array of GitHubRepo records</returns>
    public static async Task<GitHubOrg[]> ListOrgReposAsync(string token, string org, int limit = 100)
    {
        var client = CreateGitHubClient(token);

        var options = new ApiOptions
        {
            PageSize = Math.Min(limit, 100),
            PageCount = 1
        };

        var repos = await client.Repository.GetAllForOrg(org, options);

        return repos
            .Take(limit)
            .Select(r => new GitHubOrg(Login: r.Owner.Login, Name: r.Name))
            .ToArray();
    }

    /// <summary>
    /// Checks if a repository has Copilot instructions file
    /// </summary>
    /// <param name="token">GitHub personal access token</param>
    /// <param name="owner">Repository owner</param>
    /// <param name="repo">Repository name</param>
    /// <returns>True if .github/copilot-instructions.md exists, false otherwise</returns>
    public static async Task<bool> CheckRepoHasInstructionsAsync(string token, string owner, string repo)
    {
        var client = CreateGitHubClient(token);

        try
        {
            await client.Repository.Content.GetAllContents(owner, repo, ".github/copilot-instructions.md");
            return true;
        }
        catch (NotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Checks multiple repositories for Copilot instructions with concurrency limit
    /// </summary>
    /// <param name="token">GitHub personal access token</param>
    /// <param name="repos">Array of repositories to check</param>
    /// <param name="onProgress">Optional progress callback</param>
    /// <returns>Array of repositories with HasInstructions property set</returns>
    public static async Task<GitHubRepo[]> CheckReposForInstructionsAsync(
        string token,
        GitHubRepo[] repos,
        Action<int, int>? onProgress = null)
    {
        const int concurrencyLimit = 10;
        var semaphore = new SemaphoreSlim(concurrencyLimit);
        var completed = 0;
        var total = repos.Length;

        var tasks = repos.Select(async repo =>
        {
            await semaphore.WaitAsync();
            try
            {
                var hasInstructions = await CheckRepoHasInstructionsAsync(token, repo.Owner, repo.Name);
                
                Interlocked.Increment(ref completed);
                onProgress?.Invoke(completed, total);

                return repo with { HasInstructions = hasInstructions };
            }
            finally
            {
                semaphore.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }
}

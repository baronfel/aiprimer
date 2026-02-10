using System.Text.Json;
using Primer.Utils;

namespace Primer.Services;

/// <summary>
/// Options for generating configuration files.
/// </summary>
public record GenerateOptions(
    string RepoPath,
    RepoAnalysis Analysis,
    List<string> Selections,
    bool Force
);

/// <summary>
/// Service for generating configuration files for a repository.
/// </summary>
public static class GeneratorService
{
    /// <summary>
    /// Generates configuration files based on the provided options.
    /// </summary>
    /// <param name="options">The generation options.</param>
    /// <returns>A summary of the actions performed.</returns>
    public static async Task<string> GenerateConfigsAsync(GenerateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var actions = new List<string>();

        if (options.Selections.Contains("mcp"))
        {
            var filePath = Path.Combine(options.RepoPath, ".vscode", "mcp.json");
            FileUtils.EnsureDir(Path.GetDirectoryName(filePath)!);
            var content = RenderMcp();
            var result = await FileUtils.SafeWriteFileAsync(filePath, content, options.Force);
            actions.Add(result);
        }

        if (options.Selections.Contains("vscode"))
        {
            var filePath = Path.Combine(options.RepoPath, ".vscode", "settings.json");
            FileUtils.EnsureDir(Path.GetDirectoryName(filePath)!);
            var content = RenderVscodeSettings(options.Analysis);
            var result = await FileUtils.SafeWriteFileAsync(filePath, content, options.Force);
            actions.Add(result);
        }

        var summary = actions.Count > 0
            ? $"\n{string.Join("\n", actions)}"
            : "No changes made.";

        return summary;
    }

    /// <summary>
    /// Renders the MCP server configuration JSON.
    /// </summary>
    private static string RenderMcp()
    {
        var config = new
        {
            mcpServers = new
            {
                github = new
                {
                    command = "npx",
                    args = new[] { "-y", "@modelcontextprotocol/server-github" }
                },
                filesystem = new
                {
                    command = "npx",
                    args = new[] { "-y", "@modelcontextprotocol/server-filesystem", "." }
                }
            }
        };

        return JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    /// <summary>
    /// Renders the VS Code settings JSON for Copilot based on repository analysis.
    /// </summary>
    private static string RenderVscodeSettings(RepoAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var settings = new Dictionary<string, object>
        {
            ["github.copilot.enable"] = new Dictionary<string, bool>
            {
                ["*"] = true
            }
        };

        // Add language-specific settings based on analysis
        if (analysis.Languages.Count > 0)
        {
            settings["github.copilot.advanced"] = new Dictionary<string, object>
            {
                ["debug.useNodeDebugger"] = false
            };
        }

        return JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}

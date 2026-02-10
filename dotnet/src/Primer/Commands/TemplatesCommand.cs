using System.CommandLine;

namespace Primer.Commands;

/// <summary>
/// Command for listing available templates.
/// </summary>
internal static class TemplatesCommand
{
    /// <summary>
    /// Builds the templates command.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build()
    {
        var command = new Command("templates", "List available templates");

        command.SetAction(_ =>
        {
            Console.WriteLine("Available templates: mcp, vscode");
            return 0;
        });

        return command;
    }
}

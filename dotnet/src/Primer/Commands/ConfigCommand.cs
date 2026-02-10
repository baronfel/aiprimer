using System.CommandLine;

namespace Primer.Commands;

/// <summary>
/// Command for managing Primer configuration.
/// </summary>
internal static class ConfigCommand
{
    /// <summary>
    /// Builds the config command.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build()
    {
        var command = new Command("config", "Manage Primer configuration");

        command.SetAction(_ =>
        {
            Console.WriteLine("Config is not implemented yet.");
            return 0;
        });

        return command;
    }
}

using System.CommandLine;

namespace Primer.Commands;

/// <summary>
/// Command for updating Primer.
/// </summary>
internal static class UpdateCommand
{
    /// <summary>
    /// Builds the update command.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build()
    {
        var command = new Command("update", "Update Primer to the latest version");

        command.SetAction(_ =>
        {
            Console.WriteLine("Update is not implemented yet.");
            return 0;
        });

        return command;
    }
}

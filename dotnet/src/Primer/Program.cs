using System.CommandLine;
using Primer.Commands;

var rootCommand = new RootCommand("Prime repositories for AI-assisted development");

rootCommand.Subcommands.Add(InitCommand.Build());
rootCommand.Subcommands.Add(AnalyzeCommand.Build());
rootCommand.Subcommands.Add(GenerateCommand.Build());
rootCommand.Subcommands.Add(PrCommand.Build());
rootCommand.Subcommands.Add(EvalCommand.Build());
rootCommand.Subcommands.Add(TuiCommand.Build());
rootCommand.Subcommands.Add(InstructionsCommand.Build());
rootCommand.Subcommands.Add(BatchCommand.Build());
rootCommand.Subcommands.Add(TemplatesCommand.Build());
rootCommand.Subcommands.Add(UpdateCommand.Build());
rootCommand.Subcommands.Add(ConfigCommand.Build());

// If no args provided, default to TUI
if (args.Length == 0)
{
    args = ["tui"];
}

return await rootCommand.Parse(args).InvokeAsync();

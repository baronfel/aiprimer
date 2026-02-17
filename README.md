# Primer

> Prime your repositories for AI-assisted development.

Primer is a .NET CLI tool that analyzes your codebase and generates `.github/copilot-instructions.md` files to help AI coding assistants understand your project better. It supports single repos, batch processing across organizations, and includes an evaluation framework to measure instruction effectiveness.

![Primer](primer.png)

## Features

- **Repository Analysis** - Detects languages, frameworks, package managers, testing frameworks, CI/CD pipelines, Docker, file statistics, and key directories
- **AI-Powered Generation** - Uses the Copilot CLI to analyze your codebase and generate context-aware instructions
- **Batch Processing** - Process multiple repos across organizations with a single command
- **Evaluation Framework** - Test and measure how well your instructions improve AI responses
- **GitHub Integration** - Clone repos, create branches, and open PRs automatically
- **Interactive TUI** - Terminal interface built with Spectre.Console
- **Config Generation** - Generate MCP and VS Code configurations

## Prerequisites

1. **.NET 10 SDK**
2. **GitHub Copilot CLI** - Installed via VS Code's Copilot Chat extension
3. **Copilot CLI Authentication** - Run `copilot` then `/login` to authenticate
4. **PowerShell 7+** (`pwsh`) - Required for the eval command
5. **GitHub CLI (optional)** - For batch processing and PR creation: `gh auth login`

## Installation

```bash
# Clone the repository
git clone https://github.com/webreidi/aiprimer.git
cd primer

# Restore dependencies
dotnet restore dotnet/Primer.slnx
```

## Usage

All commands are run from the repository root using `dotnet run`:

```bash
dotnet run --project dotnet/src/Primer -- <command> [options]
```

### Quick Start (Init)

The easiest way to get started is with the `init` command:

```bash
# Interactive setup for current directory
dotnet run --project dotnet/src/Primer -- init

# Accept defaults and generate instructions automatically
dotnet run --project dotnet/src/Primer -- init --yes

# Work with a GitHub repository
dotnet run --project dotnet/src/Primer -- init --github
```

To run an eval on another repo:

```bash
# Initialize the repo to create the tests
dotnet run --project dotnet/src/Primer -- eval --init --repo path_to_repo

# Run the evaluation
dotnet run --project dotnet/src/Primer -- eval --repo  path_to_repo
```

### Interactive Mode (TUI)

```bash
# Run TUI in current directory
dotnet run --project dotnet/src/Primer -- tui

# Run on a specific repo
dotnet run --project dotnet/src/Primer -- tui --repo /path/to/repo

# Skip the animated intro
dotnet run --project dotnet/src/Primer -- tui --no-animation
```

**Keys:**
- `[A]` Analyze - Detect languages, frameworks, and package manager
- `[G]` Generate - Generate copilot-instructions.md using Copilot CLI
- `[S]` Save - Save generated instructions (in preview mode)
- `[D]` Discard - Discard generated instructions (in preview mode)
- `[Q]` Quit

### Generate Instructions

```bash
# Generate instructions for current directory
dotnet run --project dotnet/src/Primer -- instructions

# Generate for specific repo with custom output
dotnet run --project dotnet/src/Primer -- instructions --repo /path/to/repo --output ./instructions.md

# Use a specific model
dotnet run --project dotnet/src/Primer -- instructions --model gpt-5
```

### Batch Processing

Process multiple repositories across organizations:

```bash
# Launch batch TUI
dotnet run --project dotnet/src/Primer -- batch

# Save results to file
dotnet run --project dotnet/src/Primer -- batch --output results.json
```

### Analyze Repository

```bash
# Analyze current directory
dotnet run --project dotnet/src/Primer -- analyze

# Analyze a different repo by passing its path
dotnet run --project dotnet/src/Primer -- analyze /path/to/other/repo

# JSON output (useful for scripting)
dotnet run --project dotnet/src/Primer -- analyze /path/to/other/repo --json
```

The analyzer detects languages, frameworks, package managers, testing frameworks, CI/CD tools, Docker configuration, file statistics by category, and key directories.

### Generate Configs

Generate configuration files for your repo:

```bash
# Generate MCP config
dotnet run --project dotnet/src/Primer -- generate mcp

# Generate VS Code settings
dotnet run --project dotnet/src/Primer -- generate vscode --force
```

### Manage Templates

View available instruction templates:

```bash
dotnet run --project dotnet/src/Primer -- templates
```

### Configuration

View and manage Primer configuration:

```bash
dotnet run --project dotnet/src/Primer -- config
```

### Update

Check for and apply updates:

```bash
dotnet run --project dotnet/src/Primer -- update
```

### Create Pull Requests

Automatically create a PR to add Primer configs to a repository:

```bash
# Create PR for a GitHub repo
dotnet run --project dotnet/src/Primer -- pr owner/repo-name

# Use custom branch name
dotnet run --project dotnet/src/Primer -- pr owner/repo-name --branch primer/custom-branch
```

### Evaluation Framework

Test how well your instructions improve AI responses:

```bash
# Create a starter eval config (10 categorized test cases)
dotnet run --project dotnet/src/Primer -- eval --init

# Run evaluation against current directory
dotnet run --project dotnet/src/Primer -- eval

# Run evaluation against a different repo
dotnet run --project dotnet/src/Primer -- eval --repo /path/to/other/repo

# Use a custom config file
dotnet run --project dotnet/src/Primer -- eval primer.eval.json --repo /path/to/repo

# Save results and use specific models
dotnet run --project dotnet/src/Primer -- eval --output results.json --model gpt-5 --judge-model gpt-5
```

Example `primer.eval.json`:
```json
{
  "instructionFile": ".github/copilot-instructions.md",
  "cases": [
    {
      "id": "project-overview",
      "category": "understanding",
      "prompt": "Summarize what this project does and list the main entry points.",
      "expectation": "Should mention the primary purpose and key files/directories."
    }
  ]
}
```

The evaluator runs each prompt through the Copilot CLI twice (with and without your instructions), then uses a judge model to score which response was better. Requires the Copilot CLI and PowerShell 7+ (`pwsh`).

## How It Works

1. **Analysis** - Recursively scans the repository for:
   - Language files (`.cs`, `.ts`, `.js`, `.py`, `.go`, `.java`, `.rb`, `.php`, `.rs`, `.swift`, etc.)
   - Framework indicators (`package.json`, `tsconfig.json`, `*.csproj`, `go.mod`, `pom.xml`, etc.)
   - Package manager lock files
   - Testing frameworks (Jest, Vitest, xUnit, pytest, JUnit, etc.)
   - CI/CD pipelines (GitHub Actions, Azure Pipelines, Jenkins, etc.)
   - Docker configuration
   - Key directories and file statistics

2. **Generation** - Uses the Copilot CLI to:
   - Start a Copilot CLI session
   - Let the AI agent explore your codebase using tools
   - Generate concise, project-specific instructions
   - Falls back to basic analysis when CLI is unavailable

3. **Batch Processing** - For multiple repos:
   - Select organizations and repositories via interactive prompts
   - Clone, branch, generate, commit, push, and create PRs
   - Track success/failure for each repository

4. **Evaluation** - Measure instruction quality:
   - Run prompts with and without instructions
   - Use a judge model to score responses
   - Generate comparison reports

## Project Structure

```
primer/
├── dotnet/
│   ├── Primer.slnx              # Solution file
│   └── src/Primer/
│       ├── Program.cs            # Entry point
│       ├── Primer.csproj         # Project file
│       ├── Commands/             # CLI commands
│       │   ├── AnalyzeCommand.cs
│       │   ├── BatchCommand.cs
│       │   ├── ConfigCommand.cs
│       │   ├── EvalCommand.cs
│       │   ├── GenerateCommand.cs
│       │   ├── InitCommand.cs
│       │   ├── InstructionsCommand.cs
│       │   ├── PrCommand.cs
│       │   ├── TemplatesCommand.cs
│       │   ├── TuiCommand.cs
│       │   └── UpdateCommand.cs
│       ├── Services/             # Core business logic
│       │   ├── AnalyzerService.cs
│       │   ├── EvaluatorService.cs
│       │   ├── GeneratorService.cs
│       │   ├── GitService.cs
│       │   ├── GitHubService.cs
│       │   └── InstructionsService.cs
│       └── Utils/                # Helpers
│           ├── FileUtils.cs
│           └── Logger.cs
├── primer.eval.json              # Example eval config
└── README.md
```

## Development

```bash
# Build
dotnet build dotnet/Primer.slnx

# Run
dotnet run --project dotnet/src/Primer

# Run with arguments
dotnet run --project dotnet/src/Primer -- analyze --json
```

## Tech Stack

- **.NET 10** - Target framework
- **System.CommandLine 2.0.2** - CLI argument parsing
- **Spectre.Console** - Rich terminal output and interactive prompts
- **LibGit2Sharp** - Git operations
- **Octokit** - GitHub API client

## Troubleshooting

### "Copilot CLI not found"
Install the GitHub Copilot Chat extension in VS Code. The CLI is bundled with it.

### "Copilot CLI not logged in"
Run `copilot` in your terminal, then type `/login` to authenticate.

### "GitHub authentication required" (batch/PR commands)
Install GitHub CLI and authenticate: `gh auth login`

Or set a token: `export GITHUB_TOKEN=<your-token>` (Linux/macOS) or `$env:GITHUB_TOKEN="<your-token>"` (PowerShell)

### Generation hangs or times out
- Ensure you're authenticated with the Copilot CLI
- Check your network connection
- Try a smaller repository first

## License

MIT

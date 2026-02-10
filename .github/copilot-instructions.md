# Project Guidelines

## Overview

> Prime your repositories for AI-assisted development.

Primer is a .NET CLI tool that analyzes your codebase and generates `.github/copilot-instructions.md` files to help AI coding assistants understand your project better. It supports single repos, batch processing across organizations, and includes an evaluation framework to measure instruction effectiveness.

## Tech Stack

- **C#**
- Spectre.Console
- System.CommandLine
- NuGet (package manager)

## Project Structure

- `.github/` — GitHub configuration

## Coding Conventions

- Nullable reference types are enabled — avoid null where possible
- Implicit usings are enabled
- Follow standard .NET naming conventions (PascalCase for public members)
- Use async/await for I/O operations

## Development

### Build

```bash
dotnet build
```

### Test

```bash
dotnet test
```


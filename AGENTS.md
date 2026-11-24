# Repository Guidelines

## Project Structure & Module Organization

This repository provides a .NET 8 WPF AI assistant component using AduSkin and MVVM. `AIComponent/AIComponent.slnx` contains two projects:

- `AIComponent/AIComponent/`: reusable library. `ChatControl.xaml` defines the UI; `ViewModels/` manages presentation state; `Behaviors/` handles input behavior. `Assistant.cs`, `ChatWorkspace.cs`, and `StreamingResponse.cs` implement conversations, providers, tools, and streaming; `ProfileStorage.cs` handles encrypted configuration.
- `AIComponent/AIComponent.Demo/`: runnable host with an offline player simulation. XAML resources live alongside their controls and application files.

`README.md` and `INTEGRATION.md` document usage. There is currently no dedicated test project.

## Build, Test, and Development Commands

Run from the repository root on Windows with a .NET SDK supporting `net8.0-windows` and WPF:

```powershell
dotnet restore ./AIComponent/AIComponent.Demo/AIComponent.Demo.csproj
dotnet build ./AIComponent/AIComponent.Demo/AIComponent.Demo.csproj
dotnet run --project ./AIComponent/AIComponent.Demo/AIComponent.Demo.csproj
```

Restore downloads dependencies; build compiles both projects; run launches the demo. The library currently references the AduSkin 2.0.4 NuGet package. Prefer the project files over README instructions describing an older local AduSkin checkout.

## Coding Style & Naming Conventions

Follow adjacent code: indentation currently mixes four spaces and tabs, so preserve each file's existing style. Use file-scoped C# namespaces, PascalCase for types and public members, camelCase for private fields and parameters, `I` prefixes for interfaces, and `Async` suffixes for asynchronous methods. Nullable reference types and implicit usings are enabled. No repository formatter or lint configuration is present.

Keep presentation logic in ViewModels, use AduSkin commands and property notifications, and propagate cancellation through provider and tool operations.

## Testing Guidelines

No automated test framework or coverage threshold is configured. Build and exercise the offline demo after relevant changes: verify sending, stopping, clearing, profile switching, settings, and encrypted configuration round trips. For streaming changes, check incremental text, newlines, cancellation, and ordinary JSON responses. Record verification steps and distinguish mock checks from live-provider validation.

## Commit & Pull Request Guidelines

Recent history includes a short Chinese subject (`流式输出`) and placeholder subjects; no consistent commit convention exists. Write concise, descriptive subjects identifying the affected behavior. PRs should describe the change, link relevant issues, list verification performed, and include screenshots for visible WPF changes.

## Security & Configuration

Never commit API keys or saved profiles. Configuration uses current-user Windows DPAPI; demo profiles reside under `%LOCALAPPDATA%/AIComponent.Demo/`. Host tools must validate arguments and enforce permissions before performing business operations.

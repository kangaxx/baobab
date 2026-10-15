# Baobab

Baobab is a starter framework for managing AI Agents, MCP servers, and Hooks.

## Projects

- `src/Baobab.Core` is a dependency-free .NET 10 library and can be built on
  supported platforms.
- `src/Baobab.Desktop` is the WPF desktop interface targeting `net10.0-windows`.
  Windows is required to run the interface; `EnableWindowsTargeting` permits
  building it from non-Windows hosts when the Windows targeting pack is available.

## Build

```sh
dotnet build src/Baobab.Core/Baobab.Core.csproj
dotnet build Baobab.sln
```

## Database connections

The core library provides `DatabaseConnectionFactory.Create`, which creates a
`DbConnection` from a registered ADO.NET provider invariant name and connection
string. Register and configure the provider in the application host; no database
vendor or credentials are bundled with this starter.

## Local AI Agent discovery

The desktop app scans automatically when opened. Select **AI Agents** to see the
results or **重新扫描** to refresh them. The overview counts detected products
(not individual installations); multiple installation paths remain visible.

Supported candidates:

- CLI tools: GitHub Copilot CLI, Claude Code, OpenAI Codex CLI, Gemini CLI,
  OpenCode, Aider, Goose, and Qwen Code.
- Desktop apps: GitHub Copilot Desktop, Cursor, Windsurf, and DSH (DeepSeek Desktop).
- VS Code extensions: GitHub Copilot (including Copilot Chat), Cline, and Roo Code.

Discovery checks PATH, common npm/Python/pipx/uv and native tool locations,
GitHub CLI extension directories (`gh-copilot`), Windows uninstall records,
Start Menu shortcut targets, and VS Code stable, Insiders, and common portable
extension directories. Desktop entries must have an existing executable;
extension identities are checked against their `package.json`, excluding entries
marked obsolete. Config directories alone do not count as installations.
Custom locations outside these sources can be missed.

GitHub Copilot Desktop is detected separately from its CLI and VS Code extension.
Windows install records locate its `github.exe` even on a custom drive, without
hard-coding an installation path. Its bundled CLI is also detected under
`%LOCALAPPDATA%\github-copilot-sdk\cli`, including version subdirectories; no
particular SDK version or PATH registration is required. GitHub Desktop (the Git
client) is not treated as GitHub Copilot Desktop.

DSH discovery recognizes `DSH Desktop.exe`, including installation records named
`DSH Desktop` with a version suffix. If `InstallLocation` is empty, the scanner
uses the executable in `DisplayIcon` or the Start Menu shortcut target; custom
installation drives are supported without hard-coded paths.

Scanning never starts an agent, performs a network request, installs software, or
reads credentials, and it does not require administrator privileges. **Installed**
does not imply authenticated or usable.
Versions come from executable metadata, Windows install records, or extension
manifests; script-based CLI versions remain unknown rather than running commands.
Inaccessible or malformed sources appear under **扫描提示** rather than silently
being treated as a complete scan. Results are refreshed from disk each time.

Run the dependency-free discovery and WPF navigation regression checks on Windows:

```powershell
dotnet run --project tests\Baobab.AgentDiscovery.Checks
```

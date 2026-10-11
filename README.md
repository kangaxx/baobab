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

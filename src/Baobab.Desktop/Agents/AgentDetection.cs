namespace Baobab.Desktop.Agents;

public sealed record AgentDetection(
    string Name, string Category, string Status, string Paths, string Sources, string Version,
    bool IsInstalled);

public sealed record AgentScanResult(
    IReadOnlyList<AgentDetection> Agents, IReadOnlyList<string> Warnings);

internal sealed record AgentDefinition(
    string Name, string Category, string[] Commands, string[] ApplicationNames,
    string[] Executables, string[]? ExtensionIds = null);

internal sealed record AgentScanLocations(
    string[] CommandDirectories, string[] ApplicationDirectories,
    string[] ExtensionDirectories, string[] GitHubCliExtensionDirectories,
    string[] ShortcutDirectories, bool ReadRegistry,
    string[]? CopilotSdkDirectories = null);

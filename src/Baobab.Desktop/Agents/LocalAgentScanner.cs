using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace Baobab.Desktop.Agents;

/// <summary>
/// Finds local installations without launching agents or reading credentials.
/// Requested by the user on 2026-10-15; implemented by GitHub Copilot (Auto).
/// </summary>
public sealed class LocalAgentScanner
{
    private static readonly AgentDefinition[] Definitions =
    [
        new("GitHub Copilot CLI", "命令行工具", ["copilot", "gh-copilot"], [], []),
        new("Claude Code", "命令行工具", ["claude"], [], []),
        new("OpenAI Codex CLI", "命令行工具", ["codex"], [], []),
        new("Gemini CLI", "命令行工具", ["gemini"], [], []),
        new("OpenCode", "命令行工具", ["opencode"], [], []),
        new("Aider", "命令行工具", ["aider"], [], []),
        new("Goose", "命令行工具", ["goose"], [], []),
        new("Qwen Code", "命令行工具", ["qwen"], [], []),
        new("GitHub Copilot Desktop", "桌面应用", [], ["GitHub Copilot"], ["github.exe"]),
        new("Cursor", "桌面应用", [], ["Cursor"], ["Cursor.exe"]),
        new("Windsurf", "桌面应用", [], ["Windsurf"], ["Windsurf.exe"]),
        new("DSH (DeepSeek Desktop)", "桌面应用", [],
            ["DSH Desktop", "DeepSeek Desktop", "DeepSeekDesktop", "DeepSeek-Desktop", "DeepSeek", "DSH"],
            ["DSH Desktop.exe", "DeepSeek Desktop.exe", "DeepSeekDesktop.exe", "DeepSeek.exe", "dsh.exe"]),
        new("GitHub Copilot", "编辑器插件", [], [], [], ["github.copilot", "github.copilot-chat"]),
        new("Cline", "编辑器插件", [], [], [], ["saoudrizwan.claude-dev"]),
        new("Roo Code", "编辑器插件", [], [], [], ["rooveterinaryinc.roo-cline"])
    ];

    private readonly AgentScanLocations? _locations;

    public LocalAgentScanner() { }

    internal LocalAgentScanner(AgentScanLocations locations) => _locations = locations;

    /// <summary>Scans known locations on a worker thread; reports inaccessible locations separately.</summary>
    public Task<AgentScanResult> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(cancellationToken), cancellationToken);

    private AgentScanResult Scan(CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var locations = _locations ?? GetDefaultLocations(warnings);
        var found = Definitions.ToDictionary(
            definition => definition.Name, _ => new List<(string Path, string Source, string Version)>());

        void Add(AgentDefinition definition, string path, string source, string? version = null)
        {
            if (!File.Exists(path))
                return;
            var normalized = Path.GetFullPath(path);
            var installations = found[definition.Name];
            var existingIndex = installations.FindIndex(item =>
                string.Equals(item.Path, normalized, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                var existing = installations[existingIndex];
                if (!existing.Source.Split("；").Contains(source))
                    installations[existingIndex] = (existing.Path, existing.Source + "；" + source, existing.Version);
                return;
            }
            var detectedVersion = string.IsNullOrWhiteSpace(version) ? ReadVersion(path, warnings) : version;
            installations.Add((normalized, source,
                string.IsNullOrWhiteSpace(detectedVersion) ? "未知" : detectedVersion));
        }

        foreach (var definition in Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var directory in locations.CommandDirectories)
                foreach (var command in definition.Commands)
                    foreach (var suffix in new[] { ".exe", ".cmd", ".bat", ".ps1" })
                        Add(definition, Path.Combine(directory, command + suffix), "PATH / 常见工具目录");

            foreach (var directory in locations.ApplicationDirectories)
                foreach (var executable in definition.Executables)
                {
                    Add(definition, Path.Combine(directory, executable), "常见应用目录");
                    foreach (var applicationName in definition.ApplicationNames)
                        Add(definition, Path.Combine(directory, applicationName, executable), "常见应用目录");
                }
        }

        ScanExtensions(locations.ExtensionDirectories, Add, warnings, cancellationToken);
        ScanGitHubCliExtensions(locations.GitHubCliExtensionDirectories, Add, warnings, cancellationToken);
        ScanCopilotSdk(locations.CopilotSdkDirectories ?? [], Add, warnings, cancellationToken);
        if (locations.ReadRegistry)
            ScanRegistry(Add, warnings, cancellationToken);
        ScanShortcuts(locations.ShortcutDirectories, Add, warnings, cancellationToken);

        var results = Definitions.Select(definition =>
        {
            var installations = found[definition.Name];
            return new AgentDetection(definition.Name, definition.Category,
                installations.Count > 0 ? "已安装 · 可用性未验证" : "未发现",
                string.Join(Environment.NewLine, installations.Select(item => item.Path)),
                string.Join("；", installations.Select(item => item.Source).Distinct()),
                string.Join("；", installations.Select(item => item.Version).Distinct()),
                installations.Count > 0);
        }).ToArray();
        return new AgentScanResult(results, warnings.Distinct().ToArray());
    }

    private static AgentScanLocations GetDefaultLocations(List<string> warnings)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var commands = new List<string>();
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            TryRead(directory, warnings, () =>
                commands.Add(Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"')))));
        }
        commands.AddRange([
            Path.Combine(roaming, "npm"), Path.Combine(home, ".local", "bin"),
            Path.Combine(home, ".cargo", "bin"), Path.Combine(home, ".bun", "bin"),
            Path.Combine(home, "scoop", "shims"), Path.Combine(local, "Microsoft", "WinGet", "Links"),
            Path.Combine(home, ".local", "share", "uv", "tools", "aider-chat", "Scripts"),
            Path.Combine(home, "pipx", "venvs", "aider-chat", "Scripts"),
            Path.Combine(home, ".local", "pipx", "venvs", "aider-chat", "Scripts"),
            Path.Combine(local, "Programs", "claude"), Path.Combine(local, "Programs", "opencode"),
            Path.Combine(local, "Programs", "goose")
        ]);
        foreach (var variable in new[] { "NPM_CONFIG_PREFIX", "PIPX_BIN_DIR", "UV_TOOL_BIN_DIR" })
        {
            var directory = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(directory))
                TryRead(variable, warnings, () =>
                    commands.Add(Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory))));
        }
        foreach (var pythonRoot in new[] { Path.Combine(roaming, "Python"), Path.Combine(local, "Programs", "Python") })
            foreach (var directory in GetDirectories(pythonRoot, warnings))
                commands.Add(Path.Combine(directory, "Scripts"));

        var apps = new[]
        {
            Path.Combine(local, "Programs"), local,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        var extensions = new List<string>
        {
            Path.Combine(home, ".vscode", "extensions"),
            Path.Combine(home, ".vscode-insiders", "extensions")
        };
        var githubCliExtensions = new[]
        {
            Path.Combine(home, ".config", "gh", "extensions"),
            Path.Combine(local, "gh", "extensions"),
            Path.Combine(roaming, "GitHub CLI", "extensions")
        };
        foreach (var root in apps)
            foreach (var editor in new[] { "Microsoft VS Code", "Microsoft VS Code Insiders" })
                extensions.Add(Path.Combine(root, editor, "data", "extensions"));
        return new AgentScanLocations(
            commands.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), apps, extensions.ToArray(),
            githubCliExtensions,
            [
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
            ], true, [Path.Combine(local, "github-copilot-sdk", "cli")]);
    }

    private static void ScanCopilotSdk(string[] roots,
        Action<AgentDefinition, string, string, string?> add, List<string> warnings, CancellationToken token)
    {
        var definition = Definitions.Single(item => item.Name == "GitHub Copilot CLI");
        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            add(definition, Path.Combine(root, "copilot.exe"), "Copilot Desktop 内置 CLI", null);
            foreach (var directory in GetDirectories(root, warnings))
            {
                token.ThrowIfCancellationRequested();
                add(definition, Path.Combine(directory, "copilot.exe"), "Copilot Desktop 内置 CLI", null);
            }
        }
    }

    private static void ScanExtensions(string[] roots,
        Action<AgentDefinition, string, string, string?> add, List<string> warnings, CancellationToken token)
    {
        foreach (var root in roots)
        {
            var obsolete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var obsoletePath = Path.Combine(root, ".obsolete");
            if (File.Exists(obsoletePath))
                TryRead(obsoletePath, warnings, () =>
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(obsoletePath));
                    foreach (var entry in document.RootElement.EnumerateObject())
                        if (entry.Value.ValueKind == JsonValueKind.True)
                            obsolete.Add(entry.Name);
                });
            foreach (var directory in GetDirectories(root, warnings))
            {
                token.ThrowIfCancellationRequested();
                var folderName = Path.GetFileName(directory);
                if (obsolete.Contains(folderName) || !Definitions.Any(definition =>
                        definition.ExtensionIds is not null && definition.ExtensionIds.Any(id =>
                            folderName.StartsWith(id + "-", StringComparison.OrdinalIgnoreCase))))
                    continue;
                var manifest = Path.Combine(directory, "package.json");
                if (!File.Exists(manifest))
                    continue;
                TryRead(manifest, warnings, () =>
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                    var json = document.RootElement;
                    if (!json.TryGetProperty("publisher", out var publisher) ||
                        !json.TryGetProperty("name", out var name))
                        return;
                    var id = $"{publisher.GetString()}.{name.GetString()}";
                    var definition = Definitions.FirstOrDefault(item =>
                        item.ExtensionIds?.Contains(id, StringComparer.OrdinalIgnoreCase) == true);
                    if (definition is not null)
                        add(definition, manifest, "VS Code 插件清单",
                            json.TryGetProperty("version", out var version) ? version.GetString() : "未知");
                });
            }
        }
    }

    private static void ScanGitHubCliExtensions(string[] roots,
        Action<AgentDefinition, string, string, string?> add, List<string> warnings, CancellationToken token)
    {
        var definition = Definitions.Single(item => item.Name == "GitHub Copilot CLI");
        foreach (var root in roots)
        {
            foreach (var directory in GetDirectories(root, warnings))
            {
                token.ThrowIfCancellationRequested();
                var name = Path.GetFileName(directory);
                if (!name.Contains("copilot", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var suffix in new[] { ".exe", ".cmd", ".bat", ".ps1" })
                {
                    AddIfExists(Path.Combine(directory, "gh-copilot" + suffix));
                    AddIfExists(Path.Combine(directory, "bin", "gh-copilot" + suffix));
                }
            }
        }

        void AddIfExists(string path)
        {
            if (File.Exists(path))
                add(definition, path, "GitHub CLI 扩展目录", null);
        }
    }

    private static void ScanRegistry(Action<AgentDefinition, string, string, string?> add,
        List<string> warnings, CancellationToken token)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                TryRead($"{hive} ({view})", warnings, () =>
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null)
                        return;
                    foreach (var keyName in uninstall.GetSubKeyNames())
                    {
                        token.ThrowIfCancellationRequested();
                        TryRead($"{hive}\\Uninstall\\{keyName}", warnings, () =>
                        {
                            using var key = uninstall.OpenSubKey(keyName);
                            var name = key?.GetValue("DisplayName") as string;
                            var definition = Definitions.FirstOrDefault(item => MatchesApplication(item, name));
                            if (definition is null)
                                return;
                            var version = key?.GetValue("DisplayVersion") as string;
                            var directory = key?.GetValue("InstallLocation") as string;
                            if (!string.IsNullOrWhiteSpace(directory))
                                foreach (var executable in definition.Executables)
                                    add(definition, Path.Combine(directory.Trim('"'), executable),
                                        "Windows 安装记录", version);
                            var icon = key?.GetValue("DisplayIcon") as string;
                            var iconPath = GetIconPath(icon);
                            if (iconPath is not null &&
                                definition.Executables.Contains(Path.GetFileName(iconPath), StringComparer.OrdinalIgnoreCase))
                                add(definition, iconPath, "Windows 安装记录", version);
                        });
                    }
                });
    }

    private static bool MatchesApplication(AgentDefinition definition, string? name) =>
        name is not null && definition.ApplicationNames.Any(candidate =>
            name.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(candidate + " ", StringComparison.OrdinalIgnoreCase));

    private static string? GetIconPath(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
            return null;
        var path = icon.Trim();
        if (path.StartsWith('"'))
        {
            var closingQuote = path.IndexOf('"', 1);
            return closingQuote > 0 ? path[1..closingQuote] : null;
        }
        var comma = path.LastIndexOf(',');
        return comma > 0 && int.TryParse(path[(comma + 1)..], out _) ? path[..comma] : path;
    }

    private static void ScanShortcuts(string[] roots,
        Action<AgentDefinition, string, string, string?> add, List<string> warnings, CancellationToken token)
    {
        foreach (var root in roots.Where(path => !string.IsNullOrWhiteSpace(path)))
            ScanShortcutDirectory(root, add, warnings, token);
    }

    private static void ScanShortcutDirectory(string root,
        Action<AgentDefinition, string, string, string?> add, List<string> warnings, CancellationToken token)
    {
        TryRead(root, warnings, () =>
        {
            foreach (var path in Directory.GetFiles(root, "*.lnk"))
            {
                token.ThrowIfCancellationRequested();
                var definition = Definitions.FirstOrDefault(item => MatchesApplication(item, Path.GetFileNameWithoutExtension(path)));
                if (definition is not null)
                    TryRead(path, warnings, () => ReadShortcut(path, definition, add));
            }
        });
        foreach (var directory in GetDirectories(root, warnings))
        {
            token.ThrowIfCancellationRequested();
            TryRead(directory, warnings, () =>
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                    ScanShortcutDirectory(directory, add, warnings, token);
            });
        }
    }

    private static void ReadShortcut(string path, AgentDefinition definition,
        Action<AgentDefinition, string, string, string?> add)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!;
            shell = Activator.CreateInstance(shellType)!;
            shortcut = ((dynamic)shell).CreateShortcut(path);
            string target = ((dynamic)shortcut).TargetPath;
            if (definition.Executables.Contains(Path.GetFileName(target), StringComparer.OrdinalIgnoreCase))
                add(definition, target, "开始菜单快捷方式", null);
        }
        finally
        {
            if (shortcut is not null)
                Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null)
                Marshal.FinalReleaseComObject(shell);
        }
    }

    private static string ReadVersion(string path, List<string> warnings)
    {
        var version = "未知（未执行程序）";
        if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            TryRead(path, warnings, () =>
            {
                var metadata = FileVersionInfo.GetVersionInfo(path);
                version = metadata.ProductVersion ?? metadata.FileVersion ?? version;
            });
        return version;
    }

    private static string[] GetDirectories(string root, List<string> warnings)
    {
        var directories = Array.Empty<string>();
        TryRead(root, warnings, () => directories = Directory.GetDirectories(root));
        return directories;
    }

    private static void TryRead(string source, List<string> warnings, Action read)
    {
        try
        {
            read();
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // A missing installation directory is an expected negative result.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or JsonException or ArgumentException
            or COMException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            warnings.Add($"{source}: {exception.Message}");
        }
    }
}

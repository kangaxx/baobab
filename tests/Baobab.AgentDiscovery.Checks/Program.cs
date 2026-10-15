using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Baobab.Desktop;
using Baobab.Desktop.Agents;

// Local discovery regression checks requested on 2026-10-15; GitHub Copilot (Auto).
if (args.Contains("--local"))
{
    var localResult = await new LocalAgentScanner().ScanAsync();
    foreach (var agent in localResult.Agents)
        Console.WriteLine($"{agent.Name}: {agent.Status}{Environment.NewLine}{agent.Paths}{Environment.NewLine}{agent.Sources}");
    Console.WriteLine($"Warnings: {localResult.Warnings.Count}");
    foreach (var warning in localResult.Warnings)
        Console.WriteLine(warning);
    if (args.Contains("--verify-copilot"))
    {
        Check(localResult.Agents.Single(agent => agent.Name == "GitHub Copilot Desktop").IsInstalled,
            "The real local Copilot desktop installation is detected.");
        Check(localResult.Agents.Single(agent => agent.Name == "GitHub Copilot CLI").IsInstalled,
            "The real local Copilot CLI installation is detected.");
    }
    if (args.Contains("--verify-dsh"))
    {
        var dsh = localResult.Agents.Single(agent => agent.Name == "DSH (DeepSeek Desktop)");
        Check(dsh.IsInstalled && dsh.Paths.Split(Environment.NewLine)
                .Any(path => Path.GetFileName(path).Equals("DSH Desktop.exe", StringComparison.OrdinalIgnoreCase)),
            "The real DSH Desktop executable is detected.");
        Check(dsh.Sources.Contains("Windows 安装记录"),
            "DSH Desktop is located through its Windows install record even without InstallLocation.");
    }
    return;
}

var root = Path.Combine(Path.GetTempPath(), $"baobab-agent-checks-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
try
{
    var commands = Path.Combine(root, "commands");
    var apps = Path.Combine(root, "apps");
    var extensions = Path.Combine(root, "extensions");
    var ghExtensions = Path.Combine(root, "gh-extensions");
    Directory.CreateDirectory(commands);
    Directory.CreateDirectory(apps);
    Directory.CreateDirectory(extensions);
    Directory.CreateDirectory(ghExtensions);
    var scanner = new LocalAgentScanner(new AgentScanLocations(
        [commands, commands], [apps], [extensions], [ghExtensions], [], false));
    var empty = await scanner.ScanAsync();
    Check(empty.Agents.Count == 15, "All candidates including Copilot Desktop appear.");
    Check(empty.Agents.All(agent => !agent.IsInstalled), "Empty locations do not imply installations.");
    Check(empty.Warnings.Count == 0, "Missing tools do not generate errors.");

    var marker = Path.Combine(root, "must-not-run");
    foreach (var command in new[] { "copilot", "claude", "codex", "gemini", "opencode", "aider", "goose", "qwen" })
        File.WriteAllText(Path.Combine(commands, command + ".cmd"), $"@echo executed > \"{marker}\"");
    File.WriteAllText(Path.Combine(commands, "copilot.ps1"), $"Set-Content '{marker}' executed");
    foreach (var (directory, executable) in new[]
             { ("GitHub Copilot", "github.exe"), ("Cursor", "Cursor.exe"),
               ("Windsurf", "Windsurf.exe"), ("DSH Desktop", "DSH Desktop.exe") })
    {
        Directory.CreateDirectory(Path.Combine(apps, directory));
        File.WriteAllBytes(Path.Combine(apps, directory, executable), []);
    }
    WriteExtension("github.copilot-1.0.0", "github", "copilot", "1.0.0");
    WriteExtension("saoudrizwan.claude-dev-2.0.0", "saoudrizwan", "claude-dev", "2.0.0");
    WriteExtension("rooveterinaryinc.roo-cline-3.0.0", "rooveterinaryinc", "roo-cline", "3.0.0");

    var installed = await scanner.ScanAsync();
    Check(installed.Agents.All(agent => agent.IsInstalled), "All CLI, desktop and extension candidates are detected.");
    Check(!File.Exists(marker), "Neither command scripts nor executables are launched.");
    var copilot = installed.Agents.Single(agent => agent.Name == "GitHub Copilot CLI");
    Check(copilot.Paths.Split(Environment.NewLine).Length == 2, "Unique installations are retained without duplicate paths.");
    Check(installed.Agents.Single(agent => agent.Name == "Cline").Version == "2.0.0", "Extension version comes from its manifest.");
    var ghCopilotDir = Path.Combine(ghExtensions, "gh-copilot");
    Directory.CreateDirectory(ghCopilotDir);
    File.WriteAllText(Path.Combine(ghCopilotDir, "gh-copilot.cmd"), "@echo gh-copilot");
    var ghInstalled = await scanner.ScanAsync();
    Check(ghInstalled.Agents.Single(agent => agent.Name == "GitHub Copilot CLI").Sources.Contains("GitHub CLI 扩展目录"),
        "GitHub Copilot CLI is detected from gh extension directories.");

    File.WriteAllText(Path.Combine(extensions, ".obsolete"), """{"github.copilot-1.0.0":true}""");
    WriteExtension("saoudrizwan.claude-dev-2.0.0", "wrong", "identity", "2.0.0");
    File.WriteAllText(Path.Combine(extensions, "rooveterinaryinc.roo-cline-3.0.0", "package.json"), "{invalid");
    File.Delete(Path.Combine(apps, "DSH Desktop", "DSH Desktop.exe"));
    File.Delete(Path.Combine(commands, "qwen.cmd"));
    Directory.CreateDirectory(Path.Combine(commands, ".qwen"));
    var refreshed = await scanner.ScanAsync();
    foreach (var name in new[] { "GitHub Copilot", "Cline", "Roo Code", "DSH (DeepSeek Desktop)", "Qwen Code" })
        Check(!refreshed.Agents.Single(agent => agent.Name == name).IsInstalled,
            $"{name}: stale, mismatched, malformed or config-only sources do not imply installation.");
    Check(refreshed.Warnings.Count == 1, "Malformed manifests produce a visible warning without losing other results.");
    Check(refreshed.Agents.Single(agent => agent.Name == "Claude Code").IsInstalled, "One bad source does not stop discovery.");

    WriteExtension("github.copilot-chat-4.0.0", "github", "copilot-chat", "4.0.0");
    var chatResult = await scanner.ScanAsync();
    Check(chatResult.Agents.Single(agent => agent.Name == "GitHub Copilot").Version == "4.0.0",
        "The Copilot Chat extension is recognized under GitHub Copilot.");

    var shortcuts = Path.Combine(root, "shortcuts");
    Directory.CreateDirectory(shortcuts);
    var cursorTarget = Path.Combine(apps, "Cursor", "Cursor.exe");
    WriteShortcut(Path.Combine(shortcuts, "Cursor.lnk"), cursorTarget);
    WriteShortcut(Path.Combine(shortcuts, "DeepSeek Desktop.lnk"), Path.Combine(root, "DeepSeek.exe"));
    var shortcutScanner = new LocalAgentScanner(new AgentScanLocations([], [], [], [], [shortcuts], false));
    var shortcutResult = await shortcutScanner.ScanAsync();
    Check(shortcutResult.Agents.Single(agent => agent.Name == "Cursor").IsInstalled,
        "Start Menu shortcuts resolve to existing known executables.");
    Check(!shortcutResult.Agents.Single(agent => agent.Name == "DSH (DeepSeek Desktop)").IsInstalled,
        "A stale shortcut is not an installation.");

    var dshTarget = Path.Combine(apps, "DSH Desktop", "DSH Desktop.exe");
    File.WriteAllBytes(dshTarget, []);
    WriteShortcut(Path.Combine(shortcuts, "DSH Desktop.lnk"), dshTarget);
    var dshShortcutResult = await shortcutScanner.ScanAsync();
    Check(dshShortcutResult.Agents.Single(agent => agent.Name == "DSH (DeepSeek Desktop)").Paths == dshTarget,
        "The actual DSH Desktop executable name is recognized through a shortcut.");
    File.Delete(dshTarget);
    Check(!(await shortcutScanner.ScanAsync()).Agents
            .Single(agent => agent.Name == "DSH (DeepSeek Desktop)").IsInstalled,
        "A remaining DSH Desktop shortcut does not count after the executable is removed.");

    var desktopTarget = Path.Combine(apps, "GitHub Copilot", "github.exe");
    WriteShortcut(Path.Combine(shortcuts, "GitHub Copilot.lnk"), desktopTarget);
    WriteShortcut(Path.Combine(shortcuts, "GitHub Desktop.lnk"), cursorTarget);
    var desktopShortcutResult = await shortcutScanner.ScanAsync();
    Check(desktopShortcutResult.Agents.Single(agent => agent.Name == "GitHub Copilot Desktop").Paths == desktopTarget,
        "Copilot Desktop is recognized by its shortcut and is not confused with GitHub Desktop.");
    Check(!desktopShortcutResult.Agents.Single(agent => agent.Name == "GitHub Copilot").IsInstalled,
        "Copilot Desktop does not imply the VS Code extension is installed.");

    var sdkRoot = Path.Combine(root, "sdk", "cli");
    var sdkVersion = Path.Combine(sdkRoot, "1.0.95-3");
    var nextSdkVersion = Path.Combine(sdkRoot, "2.0.0");
    Directory.CreateDirectory(sdkVersion);
    Directory.CreateDirectory(nextSdkVersion);
    Directory.CreateDirectory(Path.Combine(sdkRoot, "stale-version"));
    File.WriteAllBytes(Path.Combine(sdkVersion, "copilot.exe"), []);
    File.WriteAllBytes(Path.Combine(nextSdkVersion, "copilot.exe"), []);
    var sdkScanner = new LocalAgentScanner(new AgentScanLocations(
        [], [], [], [], [], false, [sdkRoot]));
    var sdkResult = await sdkScanner.ScanAsync();
    var sdkCli = sdkResult.Agents.Single(agent => agent.Name == "GitHub Copilot CLI");
    Check(sdkCli.IsInstalled && sdkCli.Paths.Split(Environment.NewLine).Length == 2,
        "SDK version directories detect CLI installations without requiring PATH.");
    Check(sdkCli.Sources == "Copilot Desktop 内置 CLI",
        "The bundled CLI detection source is identified explicitly.");
    File.Delete(Path.Combine(sdkVersion, "copilot.exe"));
    File.Delete(Path.Combine(nextSdkVersion, "copilot.exe"));
    Check((await sdkScanner.ScanAsync()).Agents.All(agent => !agent.IsInstalled),
        "Empty SDK directories and removed executables do not imply installation.");

    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    try
    {
        await scanner.ScanAsync(cancellation.Token);
        throw new InvalidOperationException("Cancellation was ignored.");
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("PASS: Cancellation is honored.");
    }
    await CheckWindowAsync();
    Console.WriteLine("All discovery and navigation checks passed.");

    void WriteExtension(string directoryName, string publisher, string name, string version)
    {
        var directory = Path.Combine(extensions, directoryName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "package.json"),
            JsonSerializer.Serialize(new { publisher, name, version }));
    }
}
finally
{
    Directory.Delete(root, recursive: true);
}

static void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
    Console.WriteLine($"PASS: {description}");
}

static void WriteShortcut(string path, string target)
{
    object? shell = null;
    object? shortcut = null;
    try
    {
        shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!)!;
        shortcut = ((dynamic)shell).CreateShortcut(path);
        ((dynamic)shortcut).TargetPath = target;
        ((dynamic)shortcut).Save();
    }
    finally
    {
        if (shortcut is not null)
            Marshal.FinalReleaseComObject(shortcut);
        if (shell is not null)
            Marshal.FinalReleaseComObject(shell);
    }
}

static Task CheckWindowAsync()
{
    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var thread = new Thread(() =>
    {
        MainWindow? window = null;
        try
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            window = new MainWindow();
            var agentsPage = (Grid)window.FindName("AgentsPage");
            var overviewPage = (ScrollViewer)window.FindName("OverviewPage");
            var scanButton = (Button)window.FindName("ScanButton");
            var agentGrid = (DataGrid)window.FindName("AgentGrid");
            ((Button)window.FindName("AgentsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(agentsPage.Visibility == Visibility.Visible && overviewPage.Visibility == Visibility.Collapsed,
                "AI Agents navigation opens the discovery page.");
            ((Button)window.FindName("OverviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(overviewPage.Visibility == Visibility.Visible && agentsPage.Visibility == Visibility.Collapsed,
                "Overview navigation restores the overview.");
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Check(!scanButton.IsEnabled, "Automatic scan disables duplicate scan requests.");
            WaitForScan(scanButton);
            Check(agentGrid.Items.Count == 15, "Automatic scan populates all candidates.");
            var detected = agentGrid.Items.Cast<AgentDetection>().Count(agent => agent.IsInstalled);
            Check(((TextBlock)window.FindName("AgentCount")).Text == detected.ToString(),
                "Overview count matches the discovery results.");
            scanButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForScan(scanButton);
            Check(agentGrid.Items.Count == 15 && scanButton.Content.ToString() == "重新扫描",
                "Rescan refreshes rather than duplicating rows.");
            completion.SetResult();
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
        }
        finally
        {
            window?.Close();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    return completion.Task;
}

static void WaitForScan(Button scanButton)
{
    var frame = new DispatcherFrame();
    var deadline = DateTime.UtcNow.AddSeconds(15);
    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
    timer.Tick += (_, _) =>
    {
        if (scanButton.IsEnabled || DateTime.UtcNow >= deadline)
            frame.Continue = false;
    };
    timer.Start();
    try
    {
        Dispatcher.PushFrame(frame);
        Check(scanButton.IsEnabled, "Scan completes without blocking the dispatcher.");
    }
    finally
    {
        timer.Stop();
    }
}

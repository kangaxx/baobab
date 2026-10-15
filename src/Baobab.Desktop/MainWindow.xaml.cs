using System.Windows;
using System.Windows.Media;
using Baobab.Desktop.Agents;

namespace Baobab.Desktop;

/// <summary>
/// Hosts navigation and asynchronous local discovery requested on 2026-10-15.
/// Implemented by GitHub Copilot (Auto).
/// </summary>
public partial class MainWindow : Window
{
    private readonly LocalAgentScanner _agentScanner = new();
    private readonly CancellationTokenSource _scanCancellation = new();
    private bool _isScanning;
    private bool _isClosed;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await ScanAgentsAsync();

    private void Window_Closed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _scanCancellation.Cancel();
        if (!_isScanning)
            _scanCancellation.Dispose();
    }

    private void Overview_Click(object sender, RoutedEventArgs e) => ShowAgents(false);

    private void Agents_Click(object sender, RoutedEventArgs e) => ShowAgents(true);

    private void ShowAgents(bool showAgents)
    {
        OverviewPage.Visibility = showAgents ? Visibility.Collapsed : Visibility.Visible;
        AgentsPage.Visibility = showAgents ? Visibility.Visible : Visibility.Collapsed;
        var selectedBackground = new SolidColorBrush(Color.FromRgb(43, 56, 81));
        OverviewButton.Background = showAgents ? SystemColors.ControlBrush : selectedBackground;
        OverviewButton.Foreground = showAgents ? SystemColors.ControlTextBrush : Brushes.White;
        AgentsButton.Background = showAgents ? selectedBackground : SystemColors.ControlBrush;
        AgentsButton.Foreground = showAgents ? Brushes.White : SystemColors.ControlTextBrush;
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAgentsAsync();

    private async Task ScanAgentsAsync()
    {
        if (_isScanning || _isClosed)
            return;
        _isScanning = true;
        ScanButton.IsEnabled = false;
        ScanStatus.Text = "正在扫描本机…";
        AgentOverviewStatus.Text = "正在扫描本机…";
        try
        {
            var result = await _agentScanner.ScanAsync(_scanCancellation.Token);
            if (_isClosed)
                return;
            AgentGrid.ItemsSource = result.Agents;
            var count = result.Agents.Count(agent => agent.IsInstalled);
            AgentCount.Text = count.ToString();
            AgentOverviewStatus.Text = $"已发现 {count} 项工具 / 应用 / 插件";
            ScanStatus.Text = $"扫描完成：发现 {count} / {result.Agents.Count} 项 · {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                + (result.Warnings.Count > 0 ? $" · {result.Warnings.Count} 条扫描提示（部分位置无法检测）" : "");
            WarningDetails.Text = string.Join(Environment.NewLine, result.Warnings);
            ScanWarnings.Visibility = result.Warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ScanButton.Content = "重新扫描";
        }
        catch (OperationCanceledException) when (_scanCancellation.IsCancellationRequested)
        {
            // Closing the window cancels the background scan.
        }
        catch (Exception exception)
        {
            if (!_isClosed)
            {
                ScanStatus.Text = "扫描失败；已有结果未更新。";
                AgentOverviewStatus.Text = "扫描失败";
                MessageBox.Show(this, exception.Message, "AI Agents 扫描失败",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            _isScanning = false;
            if (_isClosed)
                _scanCancellation.Dispose();
            else
                ScanButton.IsEnabled = true;
        }
    }
}
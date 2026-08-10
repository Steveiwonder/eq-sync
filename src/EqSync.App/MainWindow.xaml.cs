using System.Windows;
using EqSync.Core;

namespace EqSync.App;

public partial class MainWindow : Window
{
    private readonly LocalAppSettings _settings;
    private readonly IManifestBuilder _manifestBuilder;
    private readonly IRunningProcessGuard _processGuard;
    private IReadOnlyList<EqInstall> _installs;

    public MainWindow(
        LocalAppSettings settings,
        IReadOnlyList<EqInstall> installs,
        IManifestBuilder manifestBuilder,
        IRunningProcessGuard processGuard)
    {
        _settings = settings;
        _installs = installs;
        _manifestBuilder = manifestBuilder;
        _processGuard = processGuard;
        InitializeComponent();
        LoadInstalls();
    }

    public void AddPeer(PeerInfo peer)
    {
        foreach (PeerInfo existing in PeersList.Items.OfType<PeerInfo>())
        {
            if (existing.MachineId == peer.MachineId)
            {
                return;
            }
        }

        PeersList.Items.Add(peer);
        StatusText.Text = $"Discovered {peer.MachineName}";
    }

    private void LoadInstalls()
    {
        InstallsList.Items.Clear();
        foreach (EqInstall install in _installs)
        {
            InstallsList.Items.Add(install);
        }

        StatusText.Text = _installs.Count == 0
            ? "No EverQuest installs detected."
            : $"Detected {_installs.Count} install(s). Machine id: {_settings.MachineId}";
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        EqInstallDiscovery discovery = new(_settings.ManualInstallPaths);
        _installs = discovery.Discover();
        LoadInstalls();
        PreviewGrid.ItemsSource = null;
    }

    private void OnPreviewClicked(object sender, RoutedEventArgs e)
    {
        if (InstallsList.SelectedItem is not EqInstall install)
        {
            StatusText.Text = "Select an install first.";
            return;
        }

        IReadOnlyList<string> blockers = _processGuard.GetBlockingProcesses();
        if (blockers.Count > 0)
        {
            StatusText.Text = $"Sync blocked while running: {string.Join(", ", blockers)}";
            return;
        }

        SyncManifest manifest = _manifestBuilder.Build(install, _settings.MachineId, Environment.MachineName);
        PreviewGrid.ItemsSource = manifest.Files
            .Select(file => new PreviewRow("Tracked", file.RelativePath, $"{file.Size:N0} bytes"))
            .ToArray();
        StatusText.Text = $"Previewed {manifest.Files.Count} tracked files for {install.DisplayName}.";
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private sealed record PreviewRow(string Action, string RelativePath, string Reason);
}

using System.Windows;
using EqSync.Core;

namespace EqSync.App;

public partial class MainWindow : Window
{
    private readonly LocalAppSettings _settings;
    private readonly IManifestBuilder _manifestBuilder;
    private readonly IRunningProcessGuard _processGuard;
    private readonly PeerSyncService _peerSyncService;
    private IReadOnlyList<EqInstall> _installs;
    private SyncPlan? _currentPlan;
    private EqInstall? _currentInstall;
    private PeerInfo? _currentPeer;

    public MainWindow(
        LocalAppSettings settings,
        IReadOnlyList<EqInstall> installs,
        IManifestBuilder manifestBuilder,
        IRunningProcessGuard processGuard,
        PeerSyncService peerSyncService)
    {
        _settings = settings;
        _installs = installs;
        _manifestBuilder = manifestBuilder;
        _processGuard = processGuard;
        _peerSyncService = peerSyncService;
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
        ApplyButton.IsEnabled = false;
        _currentPlan = null;
    }

    private async void OnPreviewClicked(object sender, RoutedEventArgs e)
    {
        if (InstallsList.SelectedItem is not EqInstall install)
        {
            StatusText.Text = "Select an install first.";
            return;
        }

        if (PeersList.SelectedItem is not PeerInfo peer)
        {
            StatusText.Text = "Select a LAN peer first.";
            return;
        }

        try
        {
            PreviewGrid.ItemsSource = null;
            ApplyButton.IsEnabled = false;
            SetBusy(true, $"Previewing {install.DisplayName} with {peer.MachineName}. Hashing files and comparing manifests...");
            SyncPlan plan = await Task.Run(() => _peerSyncService.PreviewAsync(install, peer, CancellationToken.None));
            _currentPlan = plan;
            _currentInstall = install;
            _currentPeer = peer;
            PreviewGrid.ItemsSource = plan.Items
                .Where(item => item.Action is not SyncActionKind.NoOp)
                .Select(item => new PreviewRow(item.Action.ToString(), item.RelativePath, item.Reason))
                .ToArray();
            ApplyButton.IsEnabled = plan.ChangeCount > 0 && plan.ConflictCount == 0;
            StatusText.Text = plan.ConflictCount > 0
                ? $"Preview found {plan.ConflictCount} conflict(s). Apply is disabled."
                : $"Preview found {plan.ChangeCount} change(s).";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            ApplyButton.IsEnabled = false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        if (_currentPlan is null || _currentInstall is null || _currentPeer is null)
        {
            StatusText.Text = "Preview a sync plan first.";
            return;
        }

        MessageBoxResult confirm = System.Windows.MessageBox.Show(
            $"Apply {_currentPlan.ChangeCount} change(s) between this PC and {_currentPeer.MachineName}?",
            "EQ Sync",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            SetBusy(true, "Applying sync. Copying files and creating backups...");
            PeerSyncApplyResult result = await Task.Run(() => _peerSyncService.ApplyAsync(_currentInstall, _currentPeer, _currentPlan, CancellationToken.None));
            ApplyButton.IsEnabled = false;
            StatusText.Text = $"Synced. Downloaded {result.DownloadedFiles}, uploaded {result.UploadedFiles}. Local backups: {result.LocalBackup.FileCount}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy, string? status = null)
    {
        RefreshButton.IsEnabled = !isBusy;
        PreviewButton.IsEnabled = !isBusy;
        ApplyButton.IsEnabled = !isBusy && _currentPlan is not null && _currentPlan.ChangeCount > 0 && _currentPlan.ConflictCount == 0;
        BusyProgress.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        if (status is not null)
        {
            StatusText.Text = status;
        }
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private sealed record PreviewRow(string Action, string RelativePath, string Reason);
}

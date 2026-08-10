using System.Windows;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;
using EqSync.Core;

namespace EqSync.App;

public partial class MainWindow : Window
{
    private readonly LocalAppSettings _settings;
    private readonly IManifestBuilder _manifestBuilder;
    private readonly IRunningProcessGuard _processGuard;
    private readonly PeerSyncService _peerSyncService;
    private readonly SelfUpdateService _selfUpdateService = new();
    private readonly IEqSyncLogger _logger;
    private readonly string _backupRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EqSync",
        "backups");
    private IReadOnlyList<EqInstall> _installs;
    private SyncPlan? _currentPlan;
    private EqInstall? _currentInstall;
    private PeerInfo? _currentPeer;
    private ObservableCollection<PreviewRow> _previewRows = [];

    public MainWindow(
        LocalAppSettings settings,
        IReadOnlyList<EqInstall> installs,
        IManifestBuilder manifestBuilder,
        IRunningProcessGuard processGuard,
        PeerSyncService peerSyncService,
        IEqSyncLogger logger)
    {
        _settings = settings;
        _installs = installs;
        _manifestBuilder = manifestBuilder;
        _processGuard = processGuard;
        _peerSyncService = peerSyncService;
        _logger = logger;
        InitializeComponent();
        LoadInstalls();
        StatusText.Text = $"{StatusText.Text}. Log: {_logger.LogPath}";
    }

    public void AddPeer(PeerInfo peer)
    {
        _logger.Info($"Peer discovered by UI. Machine={peer.MachineName}; Id={peer.MachineId}; Version={peer.AppVersion}; Endpoint={peer.Endpoint}; Profiles={string.Join(", ", peer.Profiles)}");
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
            _logger.Info($"UI loaded install. Profile={install.ProfileType}; Name={install.DisplayName}; Path={install.Path}; Source={install.DetectionSource}");
            InstallsList.Items.Add(install);
        }

        StatusText.Text = _installs.Count == 0
            ? "No EverQuest installs detected."
            : $"Detected {_installs.Count} install(s). Machine id: {_settings.MachineId}";
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        _logger.Info("Refresh clicked.");
        EqInstallDiscovery discovery = new(_settings.ManualInstallPaths);
        _installs = discovery.Discover();
        _logger.Info($"Refresh discovery complete. Count={_installs.Count}");
        LoadInstalls();
        PreviewGrid.ItemsSource = null;
        ApplyButton.IsEnabled = false;
        _currentPlan = null;
    }

    private async void OnPreviewClicked(object sender, RoutedEventArgs e)
    {
        if (InstallsList.SelectedItem is not EqInstall install)
        {
            _logger.Info("Preview clicked without selected install.");
            StatusText.Text = "Select an install first.";
            return;
        }

        if (PeersList.SelectedItem is not PeerInfo peer)
        {
            _logger.Info($"Preview clicked without selected peer. Install={install.DisplayName}; Profile={install.ProfileType}");
            StatusText.Text = "Select a LAN peer first.";
            return;
        }

        try
        {
            _logger.Info($"Preview clicked. Install={install.DisplayName}; Profile={install.ProfileType}; Path={install.Path}; Peer={peer.MachineName}; Endpoint={peer.Endpoint}; Version={peer.AppVersion}");
            PreviewGrid.ItemsSource = null;
            ApplyButton.IsEnabled = false;
            SetBusy(true, $"Previewing {install.DisplayName} with {peer.MachineName}. Hashing files and comparing manifests...");
            SyncPlan plan = await Task.Run(() => _peerSyncService.PreviewAsync(install, peer, CancellationToken.None));
            _logger.Info($"Preview returned. Items={plan.Items.Count}; Changes={plan.ChangeCount}; Conflicts={plan.ConflictCount}");
            _currentPlan = plan;
            _currentInstall = install;
            _currentPeer = peer;
            _previewRows = new ObservableCollection<PreviewRow>(plan.Items
                .Select(item => new PreviewRow(DisplayAction(item.Action), item.RelativePath, item.Reason))
                .ToArray());
            PreviewGrid.ItemsSource = _previewRows;
            ApplyButton.IsEnabled = plan.ChangeCount > 0 && plan.ConflictCount == 0;
            StatusText.Text = plan.ConflictCount > 0
                ? $"Preview compared {plan.Items.Count} file(s), found {plan.ConflictCount} conflict(s). Apply is disabled."
                : $"Preview compared {plan.Items.Count} file(s), found {plan.ChangeCount} change(s).";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Preview failed.");
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
            _logger.Info("Apply clicked without current plan.");
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
            _logger.Info($"Apply clicked. Install={_currentInstall.DisplayName}; Profile={_currentInstall.ProfileType}; Peer={_currentPeer.MachineName}; Items={_currentPlan.Items.Count}; Changes={_currentPlan.ChangeCount}; Conflicts={_currentPlan.ConflictCount}");
            SetBusy(true, "Applying sync. Copying files and creating backups...");
            PrepareProgressRows();
            BusyProgress.IsIndeterminate = false;
            BusyProgress.Value = 0;
            Progress<SyncProgressUpdate> progress = new(UpdateApplyProgress);
            PeerSyncApplyResult result = await Task.Run(() => _peerSyncService.ApplyAsync(_currentInstall, _currentPeer, _currentPlan, CancellationToken.None, progress));
            _currentPlan = null;
            ApplyButton.IsEnabled = false;
            StatusText.Text = $"Synced. Downloaded {result.DownloadedFiles}, uploaded {result.UploadedFiles}. Local backups: {result.LocalBackup.FileCount}.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Apply failed.");
            StatusText.Text = ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnCheckUpdatesClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _logger.Info("Update check clicked.");
            SetBusy(true, "Checking GitHub Releases for updates...");
            UpdateCheckResult update = await _selfUpdateService.CheckForUpdateAsync(CancellationToken.None);
            if (!update.IsUpdateAvailable)
            {
                StatusText.Text = $"No update available. Current version: {update.CurrentVersion}.";
                return;
            }

            MessageBoxResult confirm = System.Windows.MessageBox.Show(
                $"EQ Sync {update.LatestVersion} is available. Download, install, and restart now?",
                "EQ Sync Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                StatusText.Text = $"Update available: {update.LatestVersion}.";
                return;
            }

            StatusText.Text = $"Downloading EQ Sync {update.LatestVersion}...";
            await _selfUpdateService.DownloadAndLaunchUpdaterAsync(update, CancellationToken.None);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Update check failed.");
            StatusText.Text = $"Update check failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnOpenLogClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _logger.Info("Open log clicked.");
            string? directory = System.IO.Path.GetDirectoryName(_logger.LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(_logger.LogPath))
            {
                File.WriteAllText(_logger.LogPath, string.Empty);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _logger.LogPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open log: {ex.Message}";
        }
    }

    private void OnOpenBackupsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _logger.Info($"Open backups clicked. Path={_backupRoot}");
            Directory.CreateDirectory(_backupRoot);
            Process.Start(new ProcessStartInfo
            {
                FileName = _backupRoot,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open backups: {ex.Message}";
        }
    }

    private void SetBusy(bool isBusy, string? status = null)
    {
        RefreshButton.IsEnabled = !isBusy;
        OpenLogButton.IsEnabled = !isBusy;
        OpenBackupsButton.IsEnabled = !isBusy;
        UpdateButton.IsEnabled = !isBusy;
        PreviewButton.IsEnabled = !isBusy;
        ApplyButton.IsEnabled = !isBusy && _currentPlan is not null && _currentPlan.ChangeCount > 0 && _currentPlan.ConflictCount == 0;
        BusyProgress.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        if (isBusy)
        {
            BusyProgress.IsIndeterminate = true;
        }

        if (status is not null)
        {
            StatusText.Text = status;
        }
    }

    private void PrepareProgressRows()
    {
        foreach (PreviewRow row in _previewRows)
        {
            row.Status = row.Action is "CopyRemoteToLocal" or "CopyLocalToRemote" ? "Pending" : "-";
        }
    }

    private void UpdateApplyProgress(SyncProgressUpdate update)
    {
        if (update.TotalItems > 0)
        {
            BusyProgress.Value = Math.Clamp(update.CompletedItems * 100.0 / update.TotalItems, 0, 100);
        }

        StatusText.Text = $"{update.Message} ({update.CompletedItems}/{update.TotalItems})";
        if (update.RelativePath is null)
        {
            return;
        }

        PreviewRow? row = _previewRows.FirstOrDefault(candidate => string.Equals(candidate.RelativePath, update.RelativePath, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return;
        }

        row.Status = update.Phase switch
        {
            SyncProgressPhase.Downloading or SyncProgressPhase.Uploading => "Syncing",
            SyncProgressPhase.Completed => "Done",
            _ => row.Status
        };
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private static string DisplayAction(SyncActionKind action)
    {
        return action == SyncActionKind.NoOp ? "Same" : action.ToString();
    }

    private sealed class PreviewRow : INotifyPropertyChanged
    {
        private string _status;

        public PreviewRow(string action, string relativePath, string reason)
        {
            Action = action;
            RelativePath = relativePath;
            Reason = reason;
            _status = action == "Same" ? "-" : "Ready";
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status == value)
                {
                    return;
                }

                _status = value;
                OnPropertyChanged();
            }
        }

        public string Action { get; }

        public string RelativePath { get; }

        public string Reason { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

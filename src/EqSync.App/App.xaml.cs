using System.Windows;
using EqSync.Core;
using Forms = System.Windows.Forms;

namespace EqSync.App;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private PeerHttpService? _peerHttpService;
    private CancellationTokenSource? _discoveryCancellation;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        LocalAppSettingsStore settingsStore = new();
        LocalAppSettings settings = settingsStore.Load();
        settingsStore.Save(settings);

        SyncContentRules rules = new();
        EqInstallDiscovery discovery = new(settings.ManualInstallPaths);
        ManifestBuilder manifestBuilder = new(rules);
        BackupService backupService = new();
        RunningProcessGuard processGuard = new();
        IReadOnlyList<EqInstall> installs = discovery.Discover();

        _peerHttpService = new PeerHttpService(
            () => discovery.Discover(),
            manifestBuilder,
            rules,
            backupService,
            processGuard,
            settings.MachineId,
            Environment.MachineName);

        try
        {
            _peerHttpService.Start();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not start local peer HTTP service: {ex.Message}", "EQ Sync");
        }

        PeerSyncService peerSyncService = new(
            manifestBuilder,
            new SyncPlanner(),
            new PeerHttpTransport(),
            backupService,
            processGuard,
            settings.MachineId,
            Environment.MachineName);

        _mainWindow = new MainWindow(settings, installs, manifestBuilder, processGuard, peerSyncService);
        ConfigureTrayIcon();
        _mainWindow.Show();

        _discoveryCancellation = new CancellationTokenSource();
        _ = DiscoverPeersAsync(settings, discovery, _discoveryCancellation.Token);
        await Task.CompletedTask;
    }

    private void ConfigureTrayIcon()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "EQ Sync",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };

        _notifyIcon.ContextMenuStrip.Items.Add("Open", null, (_, _) => ShowMainWindow());
        _notifyIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Shutdown());
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        _mainWindow?.Show();
        _mainWindow?.Activate();
    }

    private async Task DiscoverPeersAsync(LocalAppSettings settings, EqInstallDiscovery installDiscovery, CancellationToken cancellationToken)
    {
        PeerInfo LocalPeer() => new(
            settings.MachineId,
            Environment.MachineName,
            AppVersionProvider.Current,
            _peerHttpService?.Endpoint ?? new Uri("http://localhost:47642/"),
            IsPaired: true,
            installDiscovery.Discover().Select(install => install.ProfileType).Distinct().ToArray());

        await using LanPeerDiscovery peerDiscovery = new(LocalPeer);
        await foreach (PeerInfo peer in peerDiscovery.DiscoverAsync(cancellationToken))
        {
            Dispatcher.Invoke(() => _mainWindow?.AddPeer(peer));
        }
    }

    private async void OnExit(object sender, ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        if (_discoveryCancellation is not null)
        {
            await _discoveryCancellation.CancelAsync();
            _discoveryCancellation.Dispose();
        }

        if (_peerHttpService is not null)
        {
            await _peerHttpService.DisposeAsync();
        }
    }
}

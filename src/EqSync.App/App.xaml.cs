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
    private LocalAppSettingsStore? _settingsStore;
    private LocalAppSettings? _settings;
    private FileEqSyncLogger? _logger;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        _settingsStore = new LocalAppSettingsStore();
        _settings = _settingsStore.Load();
        _settingsStore.Save(_settings);

        _logger = new FileEqSyncLogger();
        _logger.Info($"EQ Sync starting. Version={AppVersionProvider.Current}; Machine={Environment.MachineName}; SettingsPath={_settingsStore.SettingsPath}; LogPath={_logger.LogPath}");
        SyncContentRules rules = new();
        EqInstallDiscovery discovery = new(_settings.ManualInstallPaths);
        ManifestBuilder manifestBuilder = new(rules, _logger);
        BackupService backupService = new();
        RunningProcessGuard processGuard = new();
        IReadOnlyList<EqInstall> installs = discovery.Discover();
        _logger.Info($"Install discovery complete. Count={installs.Count}; Installs={string.Join(" | ", installs.Select(install => $"{install.ProfileType}:{install.DisplayName}:{install.Path}"))}");

        _peerHttpService = new PeerHttpService(
            () => discovery.Discover(),
            manifestBuilder,
            rules,
            backupService,
            processGuard,
            _settings.MachineId,
            Environment.MachineName,
            logger: _logger);

        try
        {
            _peerHttpService.Start();
            PromptForFirewallRulesIfNeeded();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not start local peer HTTP service.");
            System.Windows.MessageBox.Show($"Could not start local peer HTTP service: {ex.Message}", "EQ Sync");
        }

        PeerSyncService peerSyncService = new(
            manifestBuilder,
            new SyncPlanner(),
            new PeerHttpTransport(logger: _logger),
            backupService,
            processGuard,
            _settings.MachineId,
            Environment.MachineName,
            logger: _logger);

        _mainWindow = new MainWindow(_settings, installs, manifestBuilder, processGuard, peerSyncService, _logger);
        ConfigureTrayIcon();
        _mainWindow.Show();

        _discoveryCancellation = new CancellationTokenSource();
        _ = DiscoverPeersAsync(_settings, discovery, _discoveryCancellation.Token);
        await Task.CompletedTask;
    }

    private void PromptForFirewallRulesIfNeeded()
    {
        if (_settings is null || _settingsStore is null || _logger is null || _settings.FirewallPromptDismissed)
        {
            return;
        }

        FirewallRuleService firewallRules = new();
        if (firewallRules.AreRulesPresent())
        {
            _logger.Info("Firewall rules already present.");
            return;
        }

        MessageBoxResult result = System.Windows.MessageBox.Show(
            "EQ Sync listens on TCP 47642 for sync and UDP 47641 for discovery. Allow EQ Sync through Windows Firewall for local subnet devices?",
            "EQ Sync Firewall",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _logger.Info("User accepted firewall rule prompt. Launching elevated rule installer.");
            firewallRules.LaunchElevatedRuleInstaller();
            return;
        }

        _logger.Info("User declined firewall rule prompt. Prompt dismissed.");
        _settings = _settings with { FirewallPromptDismissed = true };
        _settingsStore.Save(_settings);
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

using MagicTrackpad.Configuration;
using MagicTrackpad.Gestures;
using MagicTrackpad.Hid;
using MagicTrackpad.Input;
using MagicTrackpad.Keyboard;

namespace MagicTrackpad.Runtime;

internal sealed class BridgeApplicationContext : ApplicationContext
{
    private readonly string configPath;
    private AppConfig config;
    private readonly IInputInjector injector;
    private readonly GestureEngine engine;
    private readonly KeyboardRemapper keyboard;
    private readonly System.Windows.Forms.Timer reenableTimer = new();
    private readonly System.Windows.Forms.Timer reloadTimer = new();
    private readonly EventWaitHandle reloadSignal;
    private readonly RegisteredWaitHandle reloadRegistration;
    private readonly System.Windows.Forms.Timer? stopTimer;
    private readonly NotifyIcon notifyIcon;
    private readonly object reportLock = new();
    private readonly HashSet<string> announced = [];
    private readonly HashSet<string> enabled = [];
    private readonly Dictionary<string, RecentReport> recentReports = new(StringComparer.OrdinalIgnoreCase);
    private readonly RawInputWindow window;
    private readonly DirectHidReportReader directReader;
    private DateTime lastConfigWrite;
    private volatile bool reloadRequested;

    public BridgeApplicationContext(string configPath, bool dryRun, double? seconds)
    {
        this.configPath = configPath;
        config = ConfigStore.Load(configPath);
        lastConfigWrite = ConfigLastWrite();
        injector = dryRun ? new DryRunInputInjector() : new Win32InputInjector();
        engine = new GestureEngine(injector, config.Gestures);
        keyboard = new KeyboardRemapper(injector, config.Keyboard);
        keyboard.SetAppleKeyboardPresent(DeviceCatalog.FindAppleKeyboards(DeviceActions.EnumerateRawInputDevices()).Count > 0);
        reloadSignal = ConfigReloadSignal.Create(configPath);
        reloadRegistration = ThreadPool.RegisterWaitForSingleObject(
            reloadSignal,
            (_, _) => reloadRequested = true,
            null,
            -1,
            executeOnlyOnce: false);

        notifyIcon = new NotifyIcon
        {
            Text = "Apple Peripherals",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };

        directReader = new DirectHidReportReader(OnReport);
        window = new RawInputWindow(OnReport, OnDevicesChanged);
        reenableTimer.Interval = Math.Max(3000, (int)(config.ReenableIntervalSeconds * 1000));
        reenableTimer.Tick += (_, _) => ReenableTrackpads();
        reenableTimer.Start();

        reloadTimer.Interval = 500;
        reloadTimer.Tick += (_, _) => ReloadConfigIfChanged();
        reloadTimer.Start();

        if (seconds != null)
        {
            stopTimer = new System.Windows.Forms.Timer { Interval = Math.Max(1, (int)(seconds.Value * 1000)) };
            stopTimer.Tick += (_, _) => ExitThread();
            stopTimer.Start();
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        menu.Items.Add("Refresh Devices", null, (_, _) =>
        {
            window.RefreshDevices();
            ReenableTrackpads();
        });
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        return menu;
    }

    private void ShowSettings()
    {
        var form = new Ui.SettingsForm(configPath);
        form.FormClosed += (_, _) => ReloadConfig(force: true);
        form.Show();
    }

    private void ReloadConfigIfChanged()
    {
        var writeTime = ConfigLastWrite();
        if (reloadRequested)
        {
            reloadRequested = false;
            ReloadConfig(force: true);
            return;
        }

        if (writeTime != lastConfigWrite)
        {
            ReloadConfig(force: true);
        }
    }

    private void ReloadConfig(bool force = false)
    {
        var writeTime = ConfigLastWrite();
        if (!force && writeTime <= lastConfigWrite)
        {
            return;
        }

        config = ConfigStore.Load(configPath);
        engine.UpdateConfig(config.Gestures);
        keyboard.UpdateConfig(config.Keyboard);
        reenableTimer.Interval = Math.Max(3000, (int)(config.ReenableIntervalSeconds * 1000));
        lastConfigWrite = writeTime;
    }

    private DateTime ConfigLastWrite() =>
        File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : DateTime.MinValue;

    private void OnDevicesChanged(IReadOnlyList<HidDeviceInfo> devices)
    {
        keyboard.SetAppleKeyboardPresent(DeviceCatalog.FindAppleKeyboards(devices).Count > 0);

        var groups = DeviceCatalog.FindMagicTrackpads(devices).Where(ShouldHandleDevice).GroupBy(device => DeviceActions.PhysicalKey(device.Name));
        foreach (var group in groups)
        {
            if (announced.Add(group.Key) && config.EnableMultitouchOnStart)
            {
                if (DeviceActions.EnableAnyCollection(group))
                {
                    enabled.Add(group.Key);
                }
            }
        }

        directReader.UpdateDevices(devices.Where(ShouldHandleDevice));
    }

    private void ReenableTrackpads()
    {
        if (!config.EnableMultitouchOnStart)
        {
            return;
        }

        foreach (var group in DeviceCatalog.FindMagicTrackpads(DeviceActions.EnumerateRawInputDevices()).Where(ShouldHandleDevice).GroupBy(device => DeviceActions.PhysicalKey(device.Name)))
        {
            if (DeviceActions.EnableAnyCollection(group))
            {
                enabled.Add(group.Key);
            }
        }

        directReader.UpdateDevices(DeviceActions.EnumerateRawInputDevices().Where(ShouldHandleDevice));
    }

    private bool ShouldHandleDevice(HidDeviceInfo device) =>
        !(config.UseWindowsPrecisionTouchpad && device.IsAppleMagicTrackpad && !device.IsBluetooth) &&
        (!device.IsAppleKeyboard || config.Keyboard.Enabled);

    private void OnReport(HidDeviceInfo device, byte[] report)
    {
        lock (reportLock)
        {
            if (!ShouldHandleDevice(device) || IsDuplicateReport(device, report))
            {
                return;
            }

            DeviceBattery.TryCacheReport(device, report);
            if (device.IsAppleKeyboard)
            {
                keyboard.ProcessKeyboardReport(device, report);
                // Never write keyboard input to the raw trackpad log or gesture parser.
                return;
            }

            if (config.LogRawReports)
            {
                LogRawReport(device, report);
            }

            foreach (var frame in HidReportParser.ParseReports(report))
            {
                engine.ProcessFrame(frame);
            }
        }
    }

    private bool IsDuplicateReport(HidDeviceInfo device, byte[] report)
    {
        if (report.Length == 0)
        {
            return true;
        }

        var key = $"{DeviceActions.PhysicalKey(device.Name)}:{Convert.ToHexString(report)}";
        var now = DateTimeOffset.UtcNow;
        if (recentReports.TryGetValue(key, out var recent) && now - recent.SeenAt < TimeSpan.FromMilliseconds(8))
        {
            return true;
        }

        recentReports[key] = new RecentReport(now);
        foreach (var stale in recentReports.Where(item => now - item.Value.SeenAt > TimeSpan.FromSeconds(1)).Select(item => item.Key).ToList())
        {
            recentReports.Remove(stale);
        }

        return false;
    }

    private void LogRawReport(HidDeviceInfo device, byte[] report)
    {
        var path = Path.IsPathRooted(config.RawLogPath)
            ? config.RawLogPath
            : Path.Combine(AppContext.BaseDirectory, config.RawLogPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
        File.AppendAllText(path, $"{device.ProductName} {device.Name} {Convert.ToHexString(report)}{Environment.NewLine}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            stopTimer?.Dispose();
            reenableTimer.Dispose();
            reloadTimer.Dispose();
            reloadRegistration.Unregister(null);
            reloadSignal.Dispose();
            keyboard.Dispose();
            window.Dispose();
            directReader.Dispose();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record RecentReport(DateTimeOffset SeenAt);
}

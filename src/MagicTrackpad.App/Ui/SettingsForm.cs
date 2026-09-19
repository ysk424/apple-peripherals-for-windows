using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using MagicTrackpad.Configuration;
using MagicTrackpad.Hid;
using MagicTrackpad.Input;
using MagicTrackpad.Keyboard;

namespace MagicTrackpad.Ui;

public sealed class SettingsForm : Form
{
    private const int LeftColumnWidth = 340;
    private const int MainColumnWidth = 380;
    private const int KeyboardLeftWidth = 900;
    private const int KeyboardRightWidth = 500;
    private const int TrackpadPageGutter = 36;
    private const int KeyboardPageGutter = 36;
    private const int DevicePageGutter = 20;
    private const int TrackpadStageWidth = 430;
    private const int TrackpadInspectorWidth = 600;
    private const int KeyboardStageWidth = 900;
    private const int KeyboardInspectorWidth = 520;
    private static readonly Color Shell = ThemePalette.Shell;
    private static readonly Color Surface = ThemePalette.Surface;
    private static readonly Color SurfaceAlt = ThemePalette.SurfaceAlt;
    private static readonly Color Stroke = ThemePalette.Stroke;
    private static readonly Color TextMain = ThemePalette.TextMain;
    private static readonly Color TextMuted = ThemePalette.TextMuted;
    private static readonly Color Accent = ThemePalette.Accent;

    private readonly string configPath;
    private readonly Dictionary<string, Control> controlsByName = [];
    private readonly List<Action> layoutSyncs = [];
    private readonly System.Windows.Forms.Timer autoSaveTimer = new();
    private AppConfig config;
    private bool loadingValues;
    private bool autoSaveErrorShown;
    private Label? globeKeyTestLabel;
    private Button? globeKeyTestButton;

    public SettingsForm(string configPath)
    {
        this.configPath = configPath;
        config = ConfigStore.Load(configPath);
        autoSaveTimer.Interval = 650;
        autoSaveTimer.Tick += (_, _) =>
        {
            autoSaveTimer.Stop();
            SaveNow();
        };

        Text = "Apple Peripherals for Windows";
        Width = 1520;
        Height = 920;
        MinimumSize = new Size(1140, 720);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        Font = new Font("Segoe UI", 10F);
        BackColor = Shell;
        ForeColor = TextMain;

        Build();
        ApplyPeripheralTheme(this);
        LoadValues();
        WireAutoSave();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            autoSaveTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryUseLightTitleBar();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        SyncLayouts();
        BeginInvoke(new Action(SyncLayouts));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        SyncLayouts();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        autoSaveTimer.Stop();
        SaveNow();
        base.OnFormClosing(e);
    }

    private void TryUseLightTitleBar()
    {
        try
        {
            var enabled = 0;
            _ = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
            _ = DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
        }
        catch
        {
            // Older Windows builds ignore this; the app body still owns the light theme.
        }
    }

    private void SyncLayouts()
    {
        foreach (var sync in layoutSyncs)
        {
            sync();
        }
    }

    private void Build()
    {
        var devices = DeviceTabs().ToArray();
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(0),
            BackColor = Shell,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(new AppHeader(devices)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        }, 0, 0);

        var tabHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 6, 10, 10),
            BackColor = Shell,
        };
        root.Controls.Add(tabHost, 0, 1);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(295, 38),
            Padding = new Point(16, 5),
            BackColor = Shell,
        };
        tabs.DrawItem += DrawDeviceTab;
        tabs.Resize += (_, _) => SyncLayouts();
        tabHost.Controls.Add(tabs);

        foreach (var device in devices)
        {
            var page = new TabPage(device.Title)
            {
                BackColor = Shell,
                ForeColor = TextMain,
                Padding = new Padding(16, 14, 16, 16),
                AutoScroll = true,
                Tag = device,
            };
            if (device.Kind == DeviceKind.Trackpad)
            {
                BuildTrackpadPage(page, device);
            }
            else
            {
                BuildKeyboardPage(page, device);
            }
            tabs.TabPages.Add(page);
        }
    }

    private void BuildTrackpadPage(TabPage page, DeviceTabInfo device)
    {
        var grid = new TableLayoutPanel
        {
            Width = TrackpadStageWidth + TrackpadInspectorWidth + DevicePageGutter,
            Height = 1380,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Shell,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TrackpadStageWidth));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TrackpadInspectorWidth));
        page.Controls.Add(grid);

        var stage = Column();
        var inspector = Column();
        grid.Controls.Add(stage, 0, 0);
        grid.Controls.Add(inspector, 1, 0);

        BuildTrackpadInfo(stage, device, TrackpadStageWidth);
        BuildProfileGroup(stage, device, TrackpadStageWidth);
        BuildStatusGroup(stage, "Connection", device, TrackpadStageWidth);

        AddModeStrip(inspector, "Customize", "Magic Trackpad", ["Gestures", "Pointer", "Clicks"], TrackpadInspectorWidth);

        var pointer = Group(inspector, "Pointer", TrackpadInspectorWidth);
        AddCheck(pointer, "PointerEnabled", "Move pointer");
        AddSlider(pointer, "PointerSensitivity", "Sensitivity:", 5, 100, "Slow", "Fast");
        AddCheck(pointer, "InvertPointerX", "Reverse horizontal pointer direction");
        AddCheck(pointer, "InvertPointerY", "Reverse vertical pointer direction");
        AddCheck(pointer, "SwapLeftRightButtons", "Swap left/right clicks");

        var clicks = Group(inspector, "Click Assignment", TrackpadInspectorWidth);
        AddCheck(clicks, "OneFingerTap", "1 finger tap");
        AddChoice(clicks, "OneFingerTapButton", "1 finger action:", ButtonChoices(), 210);
        AddCheck(clicks, "TwoFingerTap", "2 finger tap");
        AddChoice(clicks, "TwoFingerTapButton", "2 finger action:", ButtonChoices(), 210);
        AddCheck(clicks, "ThreeFingerTap", "3 finger tap");
        AddChoice(clicks, "ThreeFingerTapButton", "3 finger action:", ButtonChoices(), 210);
        AddCheck(clicks, "IgnorePhysicalClick", "Ignore physical click");
        AddChoice(clicks, "PhysicalClickButton", "Physical click:", ButtonChoices(), 210);
        AddChoice(clicks, "MultiFingerPhysicalClickButton", "Multi-finger click:", ButtonChoices(), 210);
        AddSlider(clicks, "TapMaxSeconds", "Tap time:", 5, 50, "Quick", "Delayed");
        AddSlider(clicks, "TapMaxDistance", "Tap distance:", 10, 250, "Tight", "Loose");

        var scroll = Group(inspector, "Scroll, Zoom, Rotate", TrackpadInspectorWidth);
        AddCheck(scroll, "ScrollEnabled", "Scrolling");
        AddCheck(scroll, "NoHorizontalScroll", "No horizontal scrolling", 28);
        AddCheck(scroll, "NaturalScroll", "Natural scroll direction", 28);
        AddSlider(scroll, "ScrollSensitivity", "Scroll speed:", 5, 200, "Slow", "Fast");
        AddCheck(scroll, "PinchZoomEnabled", "Pinch to zoom");
        AddSlider(scroll, "PinchSensitivity", "Pinch sensitivity:", 10, 200, "Light", "Strong");
        AddCheck(scroll, "SmartZoomEnabled", "Smart zoom double tap");
        AddActionChoice(scroll, "SmartZoomIn", "Smart zoom:", SmartZoomInActions(), 260);
        AddActionChoice(scroll, "SmartZoomOut", "Zoom again:", SmartZoomOutActions(), 260);
        AddCheck(scroll, "RotateEnabled", "Rotate with two fingers");
        AddActionChoice(scroll, "RotateClockwise", "Rotate clockwise:", RotateClockwiseActions(), 260);
        AddActionChoice(scroll, "RotateCounterClockwise", "Rotate counter:", RotateCounterClockwiseActions(), 260);

        var gestures = Group(inspector, "Gesture Assignment", TrackpadInspectorWidth);
        AddCheck(gestures, "TwoFingerSwipePagesEnabled", "2 finger swipe between pages");
        AddActionChoice(gestures, "TwoFingerSwipeLeft", "2 finger left:", TwoFingerLeftActions(), 260);
        AddActionChoice(gestures, "TwoFingerSwipeRight", "2 finger right:", TwoFingerRightActions(), 260);
        AddCheck(gestures, "ThreeFingerSwipesEnabled", "3 and 4 finger gestures");
        AddActionChoice(gestures, "ThreeFingerTapHotkey", "3 finger tap:", TapActions(), 260);
        AddActionChoice(gestures, "ThreeFingerSwipeLeft", "3 finger left:", DesktopLeftActions(), 260);
        AddActionChoice(gestures, "ThreeFingerSwipeRight", "3 finger right:", DesktopRightActions(), 260);
        AddActionChoice(gestures, "ThreeFingerSwipeUp", "3 finger up:", SwipeUpActions(), 260);
        AddActionChoice(gestures, "ThreeFingerSwipeDown", "3 finger down:", SwipeDownActions(), 260);
        AddActionChoice(gestures, "FourFingerSwipeLeft", "4 finger left:", DesktopLeftActions(), 260);
        AddActionChoice(gestures, "FourFingerSwipeRight", "4 finger right:", DesktopRightActions(), 260);
        AddActionChoice(gestures, "FourFingerSwipeUp", "4 finger up:", SwipeUpActions(), 260);
        AddActionChoice(gestures, "FourFingerSwipeDown", "4 finger down:", SwipeDownActions(), 260);
        AddCheck(gestures, "FourFingerPinchEnabled", "4 finger pinch and spread");
        AddActionChoice(gestures, "FourFingerPinchIn", "Pinch in:", PinchInActions(), 260);
        AddActionChoice(gestures, "FourFingerSpread", "Spread out:", SpreadActions(), 260);
        AddCheck(gestures, "FourFingerTapEnabled", "4 finger tap");
        AddActionChoice(gestures, "FourFingerTap", "Tap:", FourFingerTapActions(), 260);
        AddSlider(gestures, "SwipeThreshold", "Left/right sense:", 100, 1400, "Short", "Long");
        AddSlider(gestures, "SwipeVerticalThreshold", "Up/down sense:", 100, 1400, "Short", "Long");

        void SyncLayout() => FitDeviceLayout(page, grid, stage, inspector, TrackpadStageWidth, TrackpadInspectorWidth, 0.38, 680, 1260);
        layoutSyncs.Add(SyncLayout);
        page.HandleCreated += (_, _) => SyncLayout();
        page.Resize += (_, _) => SyncLayout();
    }

    private void BuildKeyboardPage(TabPage page, DeviceTabInfo device)
    {
        var grid = new TableLayoutPanel
        {
            Width = KeyboardStageWidth + KeyboardInspectorWidth + DevicePageGutter,
            Height = 1240,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Shell,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, KeyboardStageWidth));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, KeyboardInspectorWidth));
        page.Controls.Add(grid);

        var stage = Column();
        var inspector = Column();
        grid.Controls.Add(stage, 0, 0);
        grid.Controls.Add(inspector, 1, 0);

        BuildKeyboardInfo(stage, device, KeyboardStageWidth);
        BuildProfileGroup(stage, device, KeyboardStageWidth);
        BuildStatusGroup(stage, "Connection", device, KeyboardStageWidth);

        AddModeStrip(inspector, "Customize", "Magic Keyboard", ["Keymap", "Function Row", "Modifiers"], KeyboardInspectorWidth);

        var keymap = Group(inspector, "Apple Keymap", KeyboardInspectorWidth);
        AddCheck(keymap, "KeyboardEnabled", "Enable Apple keyboard support");
        AddCheck(keymap, "KeyboardOnlyWhenAppleKeyboardPresent", "Only apply while an Apple keyboard is connected");
        AddCheck(keymap, "KeyboardSwapExchangedKeys", "Swap exchanged modifier keys");
        AddChoice(keymap, "KeyboardFnGlobe", "Globe / Fn key:", KeyActionChoices(), 220);
        AddKeyboardFilterStatus(keymap);

        var modifiers = Group(inspector, "Modifier Assignment", KeyboardInspectorWidth);
        var actions = KeyActionChoices();
        AddChoice(modifiers, "KeyboardCapsLock", "Caps Lock:", actions, 160);
        AddChoice(modifiers, "KeyboardLeftControl", "Left Control:", actions, 160);
        AddChoice(modifiers, "KeyboardLeftOption", "Left Option:", actions, 160);
        AddChoice(modifiers, "KeyboardLeftCommand", "Left Command:", actions, 160);
        AddChoice(modifiers, "KeyboardRightCommand", "Right Command:", actions, 160);
        AddChoice(modifiers, "KeyboardRightOption", "Right Option:", actions, 160);
        AddChoice(modifiers, "KeyboardRightControl", "Right Control:", actions, 160);

        var fkeys = Group(inspector, "Function Row", KeyboardInspectorWidth);
        AddChoice(fkeys, "FKeyMode", "F1 .. F12 behavior:", ["standard", "custom"], 360);
        AddButtonRow(fkeys, "Edit all F-key mappings", ShowAllKeyMappings);

        var extra = Group(inspector, "Extended Keybinds", KeyboardInspectorWidth);
        AddHotkey(extra, "KeyboardF13", "F13");
        AddHotkey(extra, "KeyboardF14", "F14");
        AddHotkey(extra, "KeyboardF15", "F15");
        AddHotkey(extra, "KeyboardF16", "F16");
        AddHotkey(extra, "KeyboardF17", "F17");
        AddHotkey(extra, "KeyboardF18", "F18");
        AddHotkey(extra, "KeyboardF19", "F19");

        void SyncLayout() => FitDeviceLayout(page, grid, stage, inspector, KeyboardStageWidth, KeyboardInspectorWidth, 0.6, 1140, 980);
        layoutSyncs.Add(SyncLayout);
        page.HandleCreated += (_, _) => SyncLayout();
        page.Resize += (_, _) => SyncLayout();
    }

    private void BuildTrackpadInfo(FlowLayoutPanel column, DeviceTabInfo device, int width)
    {
        var group = Group(column, "Device", width);
        AddDeviceHeader(group, device);
        group.Controls.Add(new TrackpadPreview
        {
            Width = width - 48,
            Height = 330,
            Margin = new Padding(8, 18, 8, 12),
        });
        var row = Row(width: ContentWidth(group) - 8);
        row.Controls.Add(new Label { Text = "Raw touch log", Width = 150, TextAlign = ContentAlignment.MiddleLeft });
        var log = new CheckBox { Width = 24 };
        controlsByName["LogRawReports"] = log;
        row.Controls.Add(log);
        row.Controls.Add(new Label { Text = "Touch report capture", Width = 210, TextAlign = ContentAlignment.MiddleLeft, ForeColor = TextMuted });
        group.Controls.Add(row);
        AddProgress(group, device.Battery.Percent, BatteryText(device));
    }

    private void BuildKeyboardInfo(FlowLayoutPanel column, DeviceTabInfo device, int width)
    {
        var group = Group(column, "Device", width);
        AddDeviceHeader(group, device);
        group.Controls.Add(new KeyboardPreview
        {
            Width = width - 48,
            Height = 520,
            Margin = new Padding(8, 16, 8, 16),
        });
        AddProgress(group, device.Battery.Percent, BatteryText(device));
    }

    private void BuildProfileGroup(FlowLayoutPanel column, DeviceTabInfo device, int width)
    {
        var group = Group(column, "Profile", width);
        var row = Row(width: ContentWidth(group) - 8);
        row.Controls.Add(new Label
        {
            Text = "Default",
            Width = Math.Max(160, ContentWidth(group) / 2 - 16),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = TextMain,
        });
        row.Controls.Add(new Label
        {
            Text = "Auto applies",
            Width = Math.Max(150, ContentWidth(group) / 2 - 16),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Accent,
        });
        group.Controls.Add(row);

        var linked = Row(width: ContentWidth(group) - 8);
        linked.Controls.Add(new Label
        {
            Text = "Linked app",
            Width = 112,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextMuted,
        });
        linked.Controls.Add(new Label
        {
            Text = device.Connected ? "Global" : "Waiting for device",
            Width = Math.Max(220, ContentWidth(group) - 132),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextMain,
        });
        group.Controls.Add(linked);
    }

    private static void AddModeStrip(FlowLayoutPanel column, string selected, string device, string[] modes, int width)
    {
        column.Controls.Add(new ModeStrip(selected, device, modes)
        {
            Width = width - 18,
            Height = 74,
            Margin = new Padding(4, 4, 8, 12),
        });
    }

    private void BuildStatusGroup(FlowLayoutPanel column, string title, DeviceTabInfo device, int width)
    {
        var group = Group(column, title, width);
        group.Controls.Add(new Label
        {
            Text = device.Connected ? "Detected" : "Not detected",
            Width = ContentWidth(group) - 16,
            Height = 28,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = device.Connected ? Accent : TextMuted,
        });
        group.Controls.Add(new Label
        {
            Text = device.Device?.Name ?? "Open the bridge or reconnect the device to refresh this page.",
            Width = ContentWidth(group) - 16,
            AutoSize = false,
            Height = 54,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = TextMuted,
        });
    }

    private void AddDeviceHeader(FlowLayoutPanel group, DeviceTabInfo device)
    {
        var row = Row(width: ContentWidth(group) - 8);
        row.Controls.Add(new Label
        {
            Text = "Model",
            Width = 70,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextMuted,
        });
        row.Controls.Add(new Label
        {
            Text = device.Device?.ProductName ?? device.Title,
            Width = 225,
            TextAlign = ContentAlignment.MiddleLeft,
        });
        row.Controls.Add(new Label
        {
            Text = device.Device?.IsBluetooth == true ? "Bluetooth" : "USB / HID",
            Width = 105,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = TextMuted,
        });
        group.Controls.Add(row);
    }

    private static void AddProgress(FlowLayoutPanel group, int? value, string text)
    {
        var bar = new LevelMeter
        {
            Width = ContentWidth(group) - 8,
            Height = 28,
            Value = value is int percent ? Math.Clamp(percent, 0, 100) : null,
            Margin = new Padding(8, 18, 8, 6),
        };
        group.Controls.Add(bar);
        group.Controls.Add(new Label
        {
            Text = text,
            Width = ContentWidth(group) - 8,
            Height = 44,
            TextAlign = ContentAlignment.MiddleCenter,
        });
    }

    private static string BatteryText(DeviceTabInfo device)
    {
        if (!device.Connected)
        {
            return "Device not detected.";
        }

        if (device.Battery.Percent is not int percent)
        {
            return "Connected. Battery not reported yet.";
        }

        var state = device.Battery.Charging switch
        {
            true => "charging",
            false => "on battery",
            _ => "reported",
        };
        return $"Battery: {percent}% ({state}).";
    }

    private static void FitTrackpadLayout(TabPage page, TableLayoutPanel grid, FlowLayoutPanel left, FlowLayoutPanel middle, FlowLayoutPanel right)
    {
        var available = Math.Max(LeftColumnWidth + (MainColumnWidth * 2) + TrackpadPageGutter, AvailablePageWidth(page) - 20);
        var leftWidth = Math.Clamp((int)Math.Round(available * 0.27), LeftColumnWidth, 590);
        var mainWidth = Math.Clamp((available - leftWidth - TrackpadPageGutter) / 2, MainColumnWidth, 880);

        grid.Width = leftWidth + (mainWidth * 2) + TrackpadPageGutter;
        grid.ColumnStyles[0].Width = leftWidth;
        grid.ColumnStyles[1].Width = mainWidth;
        grid.ColumnStyles[2].Width = mainWidth;
        ResizeColumn(left, leftWidth);
        ResizeColumn(middle, mainWidth);
        ResizeColumn(right, mainWidth);
    }

    private static void FitKeyboardLayout(TabPage page, TableLayoutPanel grid, FlowLayoutPanel left, FlowLayoutPanel right)
    {
        var available = Math.Max(KeyboardLeftWidth + KeyboardRightWidth + KeyboardPageGutter, AvailablePageWidth(page) - 20);
        var leftWidth = Math.Clamp((int)Math.Round(available * 0.43), KeyboardLeftWidth, 920);
        var rightWidth = Math.Clamp(available - leftWidth - KeyboardPageGutter, KeyboardRightWidth, 1260);

        grid.Width = leftWidth + rightWidth + KeyboardPageGutter;
        grid.ColumnStyles[0].Width = leftWidth;
        grid.ColumnStyles[1].Width = rightWidth;
        ResizeColumn(left, leftWidth);
        ResizeColumn(right, rightWidth);
    }

    private static void FitDeviceLayout(
        TabPage page,
        TableLayoutPanel grid,
        FlowLayoutPanel stage,
        FlowLayoutPanel inspector,
        int minimumStageWidth,
        int minimumInspectorWidth,
        double stageRatio,
        int stageMaxWidth,
        int inspectorMaxWidth)
    {
        var minimum = minimumStageWidth + minimumInspectorWidth + DevicePageGutter;
        var available = Math.Max(minimum, AvailablePageWidth(page) - 20);
        var stageWidth = Math.Clamp((int)Math.Round(available * stageRatio), minimumStageWidth, stageMaxWidth);
        var inspectorWidth = Math.Clamp(available - stageWidth - DevicePageGutter, minimumInspectorWidth, inspectorMaxWidth);

        grid.Width = stageWidth + inspectorWidth + DevicePageGutter;
        grid.Left = Math.Max(0, (page.ClientSize.Width - grid.Width) / 2);
        grid.ColumnStyles[0].Width = stageWidth;
        grid.ColumnStyles[1].Width = inspectorWidth;
        ResizeColumn(stage, stageWidth);
        ResizeColumn(inspector, inspectorWidth);
    }

    private static int AvailablePageWidth(TabPage page)
    {
        var parentWidth = page.Parent?.ClientSize.Width - 8 ?? 0;
        var formWidth = page.FindForm()?.ClientSize.Width - 32 ?? 0;
        return Math.Max(page.ClientSize.Width, Math.Max(parentWidth, formWidth));
    }

    private static void ResizeColumn(FlowLayoutPanel column, int width)
    {
        column.Width = width;
        foreach (Control child in column.Controls)
        {
            if (child is ThemedGroupBox box)
            {
                ResizeGroup(box, width);
            }
            else if (child is ModeStrip strip)
            {
                strip.Width = Math.Max(260, width - 18);
            }
        }
    }

    private static void ResizeGroup(ThemedGroupBox box, int columnWidth)
    {
        var boxWidth = Math.Max(260, columnWidth - 18);
        var contentWidth = Math.Max(236, columnWidth - 44);
        box.Width = boxWidth;
        box.MinimumSize = new Size(boxWidth, 0);

        foreach (Control child in box.Controls)
        {
            if (child is FlowLayoutPanel panel)
            {
                panel.Width = contentWidth;
                panel.MinimumSize = new Size(contentWidth, 0);
                panel.Tag = contentWidth;
                ResizeGroupContent(panel, contentWidth);
            }
        }
    }

    private static void ResizeGroupContent(FlowLayoutPanel panel, int contentWidth)
    {
        foreach (Control child in panel.Controls)
        {
            switch (child)
            {
                case FlowLayoutPanel row:
                    ResizeRow(row, contentWidth);
                    break;
                case KeyboardPreview:
                    child.Width = Math.Max(180, contentWidth - 4);
                    child.Height = Math.Clamp((int)Math.Round(child.Width * 0.52), 440, 620);
                    break;
                case TrackpadPreview:
                    child.Width = Math.Max(180, contentWidth - 4);
                    break;
                case LevelMeter:
                    child.Width = Math.Max(180, contentWidth - 8);
                    break;
                case Label label:
                    label.Width = Math.Max(180, contentWidth - 8);
                    break;
            }
        }
    }

    private static void ResizeRow(FlowLayoutPanel row, int contentWidth)
    {
        row.Width = Math.Max(180, contentWidth - 8);
        var available = Math.Max(160, row.Width - row.Padding.Left - 4);
        var labels = row.Controls.OfType<Label>().ToArray();
        var button = row.Controls.OfType<Button>().FirstOrDefault();
        var textBox = row.Controls.OfType<TextBox>().FirstOrDefault();
        var combo = row.Controls.OfType<ComboBox>().FirstOrDefault();
        var sliderPanel = row.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        var check = row.Controls.OfType<CheckBox>().FirstOrDefault();

        if (textBox is not null && button is not null && labels.Length > 0)
        {
            labels[0].Width = Math.Clamp(labels[0].Width, 90, 125);
            button.Width = Math.Clamp(button.Width, 78, 94);
            textBox.Width = Math.Min(680, Math.Max(120, available - labels[0].Width - button.Width - 14));
        }
        else if (combo is not null && labels.Length > 0)
        {
            labels[0].Width = Math.Min(205, Math.Max(120, available - 155));
            combo.Width = Math.Min(420, Math.Max(120, available - labels[0].Width - 12));
        }
        else if (sliderPanel is not null && labels.Length > 0)
        {
            labels[0].Width = Math.Min(125, Math.Max(94, available / 3));
            sliderPanel.Width = Math.Max(180, available - labels[0].Width - 12);
            foreach (Control nested in sliderPanel.Controls)
            {
                if (nested is TrackBar track)
                {
                    track.Width = Math.Max(160, sliderPanel.Width - 10);
                }
            }
        }
        else if (button is not null && labels.Length > 0)
        {
            labels[0].Width = Math.Min(165, Math.Max(20, available - button.Width - 12));
            button.Width = Math.Max(160, Math.Min(260, available - labels[0].Width - 12));
        }
        else if (check is not null && labels.Length >= 2)
        {
            labels[0].Width = Math.Min(150, Math.Max(105, available / 3));
            check.Width = 24;
            labels[1].Width = Math.Max(95, available - labels[0].Width - check.Width - 16);
        }
        else if (check is not null)
        {
            check.Width = Math.Max(120, available - 4);
        }
    }

    private static FlowLayoutPanel Column() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = false,
        BackColor = Shell,
        Padding = new Padding(4),
    };

    private static FlowLayoutPanel Group(FlowLayoutPanel column, string title, int width)
    {
        var box = new ThemedGroupBox
        {
            Text = $"  {title}  ",
            Width = width - 18,
            MinimumSize = new Size(width - 18, 0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 50, 12, 14),
            Margin = new Padding(4, 4, 8, 12),
            BackColor = Surface,
            ForeColor = TextMain,
        };
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = width - 44,
            MinimumSize = new Size(width - 44, 0),
            Location = new Point(14, 48),
            Margin = new Padding(0),
            Padding = new Padding(0),
            Tag = width - 44,
            BackColor = Surface,
            ForeColor = TextMain,
        };
        box.Controls.Add(panel);
        column.Controls.Add(box);
        return panel;
    }

    private static FlowLayoutPanel Row(int height = 34, int width = 650) => new()
    {
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        Width = width,
        Height = height,
        Margin = new Padding(0, 2, 0, 2),
        BackColor = Color.Transparent,
        ForeColor = TextMain,
    };

    private static int ContentWidth(Control parent) =>
        parent.Tag is int width ? width : Math.Max(260, parent.Width);

    private static Label ThemedLabel(string text, int width) => new()
    {
        Text = text,
        Width = width,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = TextMain,
        BackColor = Color.Transparent,
    };

    private static Button ThemedButton(string text, int width, int height) => new()
    {
        Text = text,
        Width = width,
        Height = height,
        BackColor = SurfaceAlt,
        ForeColor = TextMain,
        FlatStyle = FlatStyle.Flat,
    };

    private static void ApplyPeripheralTheme(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is TabPage)
            {
                child.BackColor = Shell;
                child.ForeColor = TextMain;
            }
            else if (child is GroupBox)
            {
                child.BackColor = Surface;
                child.ForeColor = TextMain;
            }
            else if (child is FlowLayoutPanel or TableLayoutPanel or Panel)
            {
                child.BackColor = child.Parent is GroupBox or FlowLayoutPanel ? Surface : Shell;
                child.ForeColor = TextMain;
            }
            else if (child is Label label)
            {
                if (label.ForeColor == SystemColors.ControlText || label.ForeColor == Color.Black || label.ForeColor == Color.DimGray)
                {
                    label.ForeColor = label.ForeColor == Color.DimGray ? TextMuted : TextMain;
                }
                label.BackColor = Color.Transparent;
            }
            else if (child is CheckBox check)
            {
                check.ForeColor = TextMain;
                check.BackColor = Color.Transparent;
            }
            else if (child is Button button)
            {
                button.BackColor = SurfaceAlt;
                button.ForeColor = TextMain;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Stroke;
                button.FlatAppearance.MouseOverBackColor = ThemePalette.ControlHover;
                button.FlatAppearance.MouseDownBackColor = ThemePalette.ControlPressed;
            }
            else if (child is TextBox text)
            {
                text.BackColor = ThemePalette.Control;
                text.ForeColor = TextMain;
                text.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (child is ComboBox combo)
            {
                combo.BackColor = ThemePalette.Control;
                combo.ForeColor = TextMain;
                combo.FlatStyle = FlatStyle.Flat;
            }
            else if (child is TrackBar track)
            {
                track.BackColor = Surface;
                track.ForeColor = TextMain;
            }

            ApplyPeripheralTheme(child);
        }
    }

    private void AddCheck(FlowLayoutPanel parent, string name, string text, int indent = 0)
    {
        var contentWidth = ContentWidth(parent);
        var row = Row(width: contentWidth - 8);
        row.Padding = new Padding(indent, 0, 0, 0);
        var box = new CheckBox
        {
            Text = text,
            Width = contentWidth - indent - 12,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextMain,
            BackColor = Color.Transparent,
        };
        controlsByName[name] = box;
        row.Controls.Add(box);
        parent.Controls.Add(row);
    }

    private void AddChoice(FlowLayoutPanel parent, string name, string label, string[] choices, int comboWidth)
    {
        var contentWidth = ContentWidth(parent);
        var row = Row(width: contentWidth - 8);
        row.Controls.Add(ThemedLabel(label, 205));
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = Math.Min(comboWidth, Math.Max(120, contentWidth - 235)),
            BackColor = ThemePalette.Control,
            ForeColor = TextMain,
            FlatStyle = FlatStyle.Flat,
        };
        combo.Items.AddRange(choices.Cast<object>().ToArray());
        controlsByName[name] = combo;
        row.Controls.Add(combo);
        parent.Controls.Add(row);
    }

    private void AddActionChoice(FlowLayoutPanel parent, string name, string label, ActionOption[] choices, int comboWidth)
    {
        var contentWidth = ContentWidth(parent);
        var row = Row(width: contentWidth - 8);
        row.Controls.Add(ThemedLabel(label, 125));
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = Math.Min(comboWidth, Math.Max(160, contentWidth - 145)),
            BackColor = ThemePalette.Control,
            ForeColor = TextMain,
            FlatStyle = FlatStyle.Flat,
        };
        combo.Items.AddRange(choices.Cast<object>().ToArray());
        controlsByName[name] = combo;
        row.Controls.Add(combo);
        parent.Controls.Add(row);
    }

    private void AddHotkey(FlowLayoutPanel parent, string name, string label)
    {
        var contentWidth = ContentWidth(parent);
        var row = Row(width: contentWidth - 8);
        row.Controls.Add(ThemedLabel(label, 115));
        var textBox = new TextBox { Width = Math.Max(120, contentWidth - 195), BackColor = ThemePalette.Control, ForeColor = TextMain, BorderStyle = BorderStyle.FixedSingle };
        controlsByName[name] = textBox;
        row.Controls.Add(textBox);
        var record = ThemedButton("Record", 82, 28);
        record.Click += (_, _) => RecordHotkey(textBox);
        row.Controls.Add(record);
        parent.Controls.Add(row);
    }

    private void AddSlider(FlowLayoutPanel parent, string name, string label, int min, int max, string leftText, string rightText)
    {
        var contentWidth = ContentWidth(parent);
        var row = Row(58, contentWidth - 8);
        row.Controls.Add(ThemedLabel(label, 110));
        var sliderWidth = Math.Max(180, contentWidth - 130);
        var sliderPanel = new TableLayoutPanel
        {
            Width = sliderWidth,
            Height = 56,
            RowCount = 2,
            ColumnCount = 1,
            Margin = new Padding(0),
            BackColor = Surface,
        };
        sliderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        sliderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        var track = new TrackBar
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = Math.Max(1, (max - min) / 8),
            Width = sliderWidth - 10,
            Height = 32,
            Margin = new Padding(0),
            BackColor = Surface,
            ForeColor = TextMain,
        };
        controlsByName[name] = track;
        sliderPanel.Controls.Add(track, 0, 0);
        var legend = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0), BackColor = Surface };
        legend.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        legend.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        legend.Controls.Add(new Label { Text = leftText, Dock = DockStyle.Fill, ForeColor = TextMuted, BackColor = Surface }, 0, 0);
        legend.Controls.Add(new Label { Text = rightText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopRight, ForeColor = TextMuted, BackColor = Surface }, 1, 0);
        sliderPanel.Controls.Add(legend, 0, 1);
        row.Controls.Add(sliderPanel);
        parent.Controls.Add(row);
    }

    private void AddButtonRow(FlowLayoutPanel parent, string text, Action action)
    {
        var contentWidth = ContentWidth(parent);
        var row = Row(42, contentWidth - 8);
        var button = ThemedButton(text, Math.Min(280, Math.Max(190, contentWidth - 16)), 32);
        button.Click += (_, _) => action();
        row.Controls.Add(button);
        parent.Controls.Add(row);
    }

    private void AddKeyboardFilterStatus(FlowLayoutPanel parent)
    {
        var status = KeyboardFilterDriverStatus.Query();
        var contentWidth = ContentWidth(parent);

        var statusRow = Row(width: contentWidth - 8);
        statusRow.Controls.Add(ThemedLabel("Driver status:", 205));
        statusRow.Controls.Add(new Label
        {
            Text = status.Ready ? "Active" : "Not active",
            Width = Math.Max(120, contentWidth - 225),
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = status.Ready ? Accent : Color.FromArgb(174, 89, 72),
            BackColor = Color.Transparent,
        });
        parent.Controls.Add(statusRow);

        var detail = Row(50, contentWidth - 8);
        detail.Controls.Add(new Label
        {
            Text = status.Diagnosis,
            Width = contentWidth - 16,
            Height = 46,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextMuted,
            BackColor = Color.Transparent,
        });
        parent.Controls.Add(detail);

        var driverRow = Row(width: contentWidth - 8);
        driverRow.Controls.Add(ThemedLabel("Windows driver:", 205));
        driverRow.Controls.Add(new Label
        {
            Text = status.TargetDriverInfPath ?? "not bound",
            Width = Math.Max(120, contentWidth - 225),
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextMuted,
            BackColor = Color.Transparent,
        });
        parent.Controls.Add(driverRow);

        var testRow = Row(42, contentWidth - 8);
        testRow.Controls.Add(ThemedLabel("Live Globe/Fn:", 205));
        globeKeyTestLabel = new Label
        {
            Text = "Not tested",
            Width = Math.Max(120, contentWidth - 365),
            Height = 32,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextMuted,
            BackColor = Color.Transparent,
        };
        globeKeyTestButton = ThemedButton("Test key", 130, 32);
        globeKeyTestButton.Click += (_, _) => RunGlobeKeyProbe();
        testRow.Controls.Add(globeKeyTestLabel);
        testRow.Controls.Add(globeKeyTestButton);
        parent.Controls.Add(testRow);

        if (!status.Ready)
        {
            var installState = InstalledBundleState();
            var packageRow = Row(width: contentWidth - 8);
            packageRow.Controls.Add(ThemedLabel("Keyboard package:", 205));
            packageRow.Controls.Add(new Label
            {
                Text = installState.HasBundledKeyboardDriver switch
                {
                    true => "Bundled",
                    false => "Not bundled",
                    null => "Unknown",
                },
                Width = Math.Max(120, contentWidth - 225),
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = installState.HasBundledKeyboardDriver == true ? Accent : TextMuted,
                BackColor = Color.Transparent,
            });
            parent.Controls.Add(packageRow);

            var actionRow = Row(42, contentWidth - 8);
            actionRow.Controls.Add(ThemedLabel("", 205));
            var actionWidth = Math.Min(190, Math.Max(150, contentWidth - 225));
            if (installState.HasBundledKeyboardDriver == true)
            {
                var repair = ThemedButton("Repair keyboard driver", actionWidth, 32);
                repair.Click += (_, _) => LaunchDriverRepair();
                actionRow.Controls.Add(repair);
            }
            else
            {
                var docs = ThemedButton("Driver guide", actionWidth, 32);
                docs.Click += (_, _) => OpenKeyboardDriverGuide();
                actionRow.Controls.Add(docs);
            }

            parent.Controls.Add(actionRow);
        }
    }

    private async void RunGlobeKeyProbe()
    {
        if (globeKeyTestLabel == null || globeKeyTestButton == null)
        {
            return;
        }

        globeKeyTestButton.Enabled = false;
        globeKeyTestLabel.Text = "Press Globe/Fn now...";
        globeKeyTestLabel.ForeColor = TextMain;

        try
        {
            var result = await Task.Run(() => GlobeKeyProbe.Run(TimeSpan.FromSeconds(8)));
            if (result.Observed)
            {
                globeKeyTestLabel.Text = "Detected";
                globeKeyTestLabel.ForeColor = Accent;
                return;
            }

            globeKeyTestLabel.Text = "Not detected";
            globeKeyTestLabel.ForeColor = Color.FromArgb(174, 89, 72);
            MessageBox.Show(
                this,
                result.Diagnosis,
                "Globe/Fn Test",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            globeKeyTestLabel.Text = "Test failed";
            globeKeyTestLabel.ForeColor = Color.FromArgb(174, 89, 72);
            MessageBox.Show(this, ex.Message, "Globe/Fn Test", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            globeKeyTestButton.Enabled = true;
        }
    }

    private void LaunchDriverRepair()
    {
        var setupPath = InstalledSetupPath();
        if (!File.Exists(setupPath))
        {
            MessageBox.Show(
                this,
                "ApplePeripheralsSetup.exe was not found. Run the latest installer again from GitHub Releases.",
                "Apple Peripherals",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = "/install /driver",
                WorkingDirectory = Path.GetDirectoryName(setupPath) ?? Environment.CurrentDirectory,
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            MessageBox.Show(
                this,
                "Administrator approval was canceled, so Windows did not install or repair the drivers.",
                "Apple Peripherals",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Apple Peripherals", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string InstalledSetupPath()
    {
        var appDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var appParent = Directory.GetParent(appDirectory);
        if (appParent != null)
        {
            var siblingSetup = Path.Combine(appParent.FullName, "ApplePeripheralsSetup.exe");
            if (File.Exists(siblingSetup))
            {
                return siblingSetup;
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ApplePeripheralsForWindows",
            "ApplePeripheralsSetup.exe");
    }

    private static InstallBundleState InstalledBundleState()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ApplePeripheralsForWindows",
            "install-state.json");
        if (!File.Exists(path))
        {
            return new InstallBundleState(null);
        }

        try
        {
            var state = JsonSerializer.Deserialize<InstallBundleState>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return state ?? new InstallBundleState(null);
        }
        catch
        {
            return new InstallBundleState(null);
        }
    }

    private void OpenKeyboardDriverGuide()
    {
        MessageBox.Show(this,
            "USB keyboard modifier remapping works through the local bridge. " +
            "Globe/Fn support depends on the keyboard model and an optional signed filter driver. " +
            "This offline build does not download or install that optional driver. " +
            "See docs/keyboard-filter-driver.md in the source checkout.",
            "Keyboard driver guide", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private sealed record InstallBundleState(bool? HasBundledKeyboardDriver);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private void WireAutoSave()
    {
        foreach (var control in controlsByName.Values)
        {
            switch (control)
            {
                case CheckBox box:
                    box.CheckedChanged += (_, _) => ScheduleAutoSave();
                    break;
                case ComboBox combo:
                    combo.SelectedIndexChanged += (_, _) => ScheduleAutoSave();
                    break;
                case TrackBar slider:
                    slider.ValueChanged += (_, _) => ScheduleAutoSave();
                    break;
                case TextBox text:
                    text.TextChanged += (_, _) => ScheduleAutoSave();
                    break;
            }
        }
    }

    private void ScheduleAutoSave()
    {
        if (loadingValues)
        {
            return;
        }

        autoSaveTimer.Stop();
        autoSaveTimer.Start();
    }

    private void LoadValues()
    {
        loadingValues = true;
        try
        {
            Set("PointerEnabled", config.Gestures.PointerEnabled);
            Set("PointerSensitivity", Scale(config.Gestures.PointerSensitivity, 100));
            Set("InvertPointerX", config.Gestures.InvertPointerX);
            Set("InvertPointerY", config.Gestures.InvertPointerY);
            Set("ScrollEnabled", config.Gestures.ScrollEnabled);
            Set("NaturalScroll", config.Gestures.NaturalScroll);
            Set("ScrollSensitivity", Scale(config.Gestures.ScrollSensitivity, 100));
            Set("NoHorizontalScroll", !config.Gestures.HorizontalScrollEnabled);
            Set("OneFingerTap", config.Gestures.TapToClick && config.Gestures.OneFingerTapButton != "none");
            Set("TwoFingerTap", config.Gestures.TapToClick && config.Gestures.TwoFingerTapButton != "none");
            Set("ThreeFingerTap", config.Gestures.ThreeFingerMiddleClick && config.Gestures.ThreeFingerTapButton != "none");
            Set("IgnorePhysicalClick", config.Gestures.PhysicalClickButton == "none");
            Set("PhysicalClickButton", config.Gestures.PhysicalClickButton);
            Set("MultiFingerPhysicalClickButton", config.Gestures.MultiFingerPhysicalClickButton);
            Set("OneFingerTapButton", config.Gestures.OneFingerTapButton);
            Set("TwoFingerTapButton", config.Gestures.TwoFingerTapButton);
            Set("ThreeFingerTapButton", config.Gestures.ThreeFingerTapButton);
            Set("TapMaxSeconds", Scale(config.Gestures.TapMaxSeconds, 100));
            Set("TapMaxDistance", (int)Math.Round(config.Gestures.TapMaxDistance));
            Set("PinchZoomEnabled", config.Gestures.PinchZoomEnabled);
            Set("PinchSensitivity", Scale(config.Gestures.PinchSensitivity, 100));
            Set("SmartZoomEnabled", config.Gestures.SmartZoomEnabled);
            Set("RotateEnabled", config.Gestures.RotateEnabled);
            Set("TwoFingerSwipePagesEnabled", config.Gestures.TwoFingerSwipePagesEnabled);
            Set("FourFingerPinchEnabled", config.Gestures.FourFingerPinchEnabled);
            Set("FourFingerTapEnabled", config.Gestures.FourFingerTapEnabled);
            Set("ThreeFingerSwipesEnabled", config.Gestures.ThreeFingerSwipesEnabled);
            Set("SwipeThreshold", (int)Math.Round(config.Gestures.SwipeThreshold));
            Set("SwipeVerticalThreshold", (int)Math.Round(config.Gestures.SwipeVerticalThreshold));
            Set("SwapLeftRightButtons", config.Gestures.SwapLeftRightButtons);
            Set("TwoFingerSwipeLeft", config.Gestures.Hotkeys.TwoFingerSwipeLeft);
            Set("TwoFingerSwipeRight", config.Gestures.Hotkeys.TwoFingerSwipeRight);
            Set("SmartZoomIn", config.Gestures.Hotkeys.SmartZoomIn);
            Set("SmartZoomOut", config.Gestures.Hotkeys.SmartZoomOut);
            Set("RotateClockwise", config.Gestures.Hotkeys.RotateClockwise);
            Set("RotateCounterClockwise", config.Gestures.Hotkeys.RotateCounterClockwise);
            Set("FourFingerPinchIn", config.Gestures.Hotkeys.FourFingerPinchIn);
            Set("FourFingerSpread", config.Gestures.Hotkeys.FourFingerSpread);
            Set("FourFingerTap", config.Gestures.Hotkeys.FourFingerTap);
            Set("ThreeFingerTapHotkey", config.Gestures.Hotkeys.ThreeFingerTap);
            Set("ThreeFingerSwipeLeft", config.Gestures.Hotkeys.ThreeFingerSwipeLeft);
            Set("ThreeFingerSwipeRight", config.Gestures.Hotkeys.ThreeFingerSwipeRight);
            Set("ThreeFingerSwipeUp", config.Gestures.Hotkeys.ThreeFingerSwipeUp);
            Set("ThreeFingerSwipeDown", config.Gestures.Hotkeys.ThreeFingerSwipeDown);
            Set("FourFingerSwipeLeft", config.Gestures.Hotkeys.FourFingerSwipeLeft);
            Set("FourFingerSwipeRight", config.Gestures.Hotkeys.FourFingerSwipeRight);
            Set("FourFingerSwipeUp", config.Gestures.Hotkeys.FourFingerSwipeUp);
            Set("FourFingerSwipeDown", config.Gestures.Hotkeys.FourFingerSwipeDown);
            Set("LogRawReports", config.LogRawReports);

            Set("KeyboardEnabled", config.Keyboard.Enabled);
            Set("KeyboardOnlyWhenAppleKeyboardPresent", config.Keyboard.OnlyWhenAppleKeyboardPresent);
            Set("KeyboardSwapExchangedKeys", config.Keyboard.SwapExchangedKeys);
            Set("FKeyMode", config.Keyboard.FKeyMode);
            Set("KeyboardLeftCommand", config.Keyboard.LeftCommand);
            Set("KeyboardRightCommand", config.Keyboard.RightCommand);
            Set("KeyboardLeftControl", config.Keyboard.LeftControl);
            Set("KeyboardRightControl", config.Keyboard.RightControl);
            Set("KeyboardLeftOption", config.Keyboard.LeftOption);
            Set("KeyboardRightOption", config.Keyboard.RightOption);
            Set("KeyboardCapsLock", config.Keyboard.CapsLock);
            Set("KeyboardFnGlobe", config.Keyboard.FnGlobe);
            Set("KeyboardF13", config.Keyboard.F13);
            Set("KeyboardF14", config.Keyboard.F14);
            Set("KeyboardF15", config.Keyboard.F15);
            Set("KeyboardF16", config.Keyboard.F16);
            Set("KeyboardF17", config.Keyboard.F17);
            Set("KeyboardF18", config.Keyboard.F18);
            Set("KeyboardF19", config.Keyboard.F19);
        }
        finally
        {
            loadingValues = false;
        }
    }

    private bool SaveNow()
    {
        try
        {
            ReadValues();
            ValidateHotkeys();
            ConfigStore.Save(configPath, config);
            ConfigReloadSignal.Notify(configPath);
            autoSaveErrorShown = false;
            return true;
        }
        catch (Exception ex)
        {
            if (!autoSaveErrorShown)
            {
                MessageBox.Show(this, ex.Message, "Apple Peripherals", MessageBoxButtons.OK, MessageBoxIcon.Error);
                autoSaveErrorShown = true;
            }

            return false;
        }
    }

    private void ReadValues()
    {
        config.Gestures.PointerEnabled = GetBool("PointerEnabled");
        config.Gestures.PointerSensitivity = GetInt("PointerSensitivity") / 100.0;
        config.Gestures.InvertPointerX = GetBool("InvertPointerX");
        config.Gestures.InvertPointerY = GetBool("InvertPointerY");
        config.Gestures.ScrollEnabled = GetBool("ScrollEnabled");
        config.Gestures.NaturalScroll = GetBool("NaturalScroll");
        config.Gestures.ScrollSensitivity = GetInt("ScrollSensitivity") / 100.0;
        config.Gestures.HorizontalScrollEnabled = !GetBool("NoHorizontalScroll");
        config.Gestures.OneFingerTapButton = GetBool("OneFingerTap") ? GetText("OneFingerTapButton") : "none";
        config.Gestures.TwoFingerTapButton = GetBool("TwoFingerTap") ? GetText("TwoFingerTapButton") : "none";
        config.Gestures.ThreeFingerTapButton = GetBool("ThreeFingerTap") ? GetText("ThreeFingerTapButton") : "none";
        config.Gestures.TapToClick = config.Gestures.OneFingerTapButton != "none" ||
            config.Gestures.TwoFingerTapButton != "none" ||
            config.Gestures.ThreeFingerTapButton != "none";
        config.Gestures.ThreeFingerMiddleClick = GetBool("ThreeFingerTap");
        config.Gestures.PhysicalClickButton = GetBool("IgnorePhysicalClick") ? "none" : GetText("PhysicalClickButton");
        config.Gestures.MultiFingerPhysicalClickButton = GetText("MultiFingerPhysicalClickButton");
        config.Gestures.TapMaxSeconds = GetInt("TapMaxSeconds") / 100.0;
        config.Gestures.TapMaxDistance = GetInt("TapMaxDistance");
        config.Gestures.PinchZoomEnabled = GetBool("PinchZoomEnabled");
        config.Gestures.PinchSensitivity = GetInt("PinchSensitivity") / 100.0;
        config.Gestures.SmartZoomEnabled = GetBool("SmartZoomEnabled");
        config.Gestures.RotateEnabled = GetBool("RotateEnabled");
        config.Gestures.TwoFingerSwipePagesEnabled = GetBool("TwoFingerSwipePagesEnabled");
        config.Gestures.FourFingerPinchEnabled = GetBool("FourFingerPinchEnabled");
        config.Gestures.FourFingerTapEnabled = GetBool("FourFingerTapEnabled");
        config.Gestures.ThreeFingerSwipesEnabled = GetBool("ThreeFingerSwipesEnabled");
        config.Gestures.SwipeThreshold = GetInt("SwipeThreshold");
        config.Gestures.SwipeVerticalThreshold = GetInt("SwipeVerticalThreshold");
        config.Gestures.SwapLeftRightButtons = GetBool("SwapLeftRightButtons");
        config.Gestures.Hotkeys.TwoFingerSwipeLeft = Hotkeys.Normalize(GetText("TwoFingerSwipeLeft"));
        config.Gestures.Hotkeys.TwoFingerSwipeRight = Hotkeys.Normalize(GetText("TwoFingerSwipeRight"));
        config.Gestures.Hotkeys.SmartZoomIn = Hotkeys.Normalize(GetText("SmartZoomIn"));
        config.Gestures.Hotkeys.SmartZoomOut = Hotkeys.Normalize(GetText("SmartZoomOut"));
        config.Gestures.Hotkeys.RotateClockwise = Hotkeys.Normalize(GetText("RotateClockwise"));
        config.Gestures.Hotkeys.RotateCounterClockwise = Hotkeys.Normalize(GetText("RotateCounterClockwise"));
        config.Gestures.Hotkeys.FourFingerPinchIn = Hotkeys.Normalize(GetText("FourFingerPinchIn"));
        config.Gestures.Hotkeys.FourFingerSpread = Hotkeys.Normalize(GetText("FourFingerSpread"));
        config.Gestures.Hotkeys.FourFingerTap = Hotkeys.Normalize(GetText("FourFingerTap"));
        config.Gestures.Hotkeys.ThreeFingerTap = Hotkeys.Normalize(GetText("ThreeFingerTapHotkey"));
        config.Gestures.Hotkeys.ThreeFingerSwipeLeft = Hotkeys.Normalize(GetText("ThreeFingerSwipeLeft"));
        config.Gestures.Hotkeys.ThreeFingerSwipeRight = Hotkeys.Normalize(GetText("ThreeFingerSwipeRight"));
        config.Gestures.Hotkeys.ThreeFingerSwipeUp = Hotkeys.Normalize(GetText("ThreeFingerSwipeUp"));
        config.Gestures.Hotkeys.ThreeFingerSwipeDown = Hotkeys.Normalize(GetText("ThreeFingerSwipeDown"));
        config.Gestures.Hotkeys.FourFingerSwipeLeft = Hotkeys.Normalize(GetText("FourFingerSwipeLeft"));
        config.Gestures.Hotkeys.FourFingerSwipeRight = Hotkeys.Normalize(GetText("FourFingerSwipeRight"));
        config.Gestures.Hotkeys.FourFingerSwipeUp = Hotkeys.Normalize(GetText("FourFingerSwipeUp"));
        config.Gestures.Hotkeys.FourFingerSwipeDown = Hotkeys.Normalize(GetText("FourFingerSwipeDown"));
        config.LogRawReports = GetBool("LogRawReports");

        config.Keyboard.Enabled = GetBool("KeyboardEnabled");
        config.Keyboard.OnlyWhenAppleKeyboardPresent = GetBool("KeyboardOnlyWhenAppleKeyboardPresent");
        config.Keyboard.SwapExchangedKeys = GetBool("KeyboardSwapExchangedKeys");
        config.Keyboard.FKeyMode = GetText("FKeyMode");
        config.Keyboard.LeftCommand = GetText("KeyboardLeftCommand");
        config.Keyboard.RightCommand = GetText("KeyboardRightCommand");
        config.Keyboard.LeftControl = GetText("KeyboardLeftControl");
        config.Keyboard.RightControl = GetText("KeyboardRightControl");
        config.Keyboard.LeftOption = GetText("KeyboardLeftOption");
        config.Keyboard.RightOption = GetText("KeyboardRightOption");
        config.Keyboard.CapsLock = GetText("KeyboardCapsLock");
        config.Keyboard.FnGlobe = Hotkeys.Normalize(GetText("KeyboardFnGlobe"));
        config.Keyboard.F13 = Hotkeys.Normalize(GetText("KeyboardF13"));
        config.Keyboard.F14 = Hotkeys.Normalize(GetText("KeyboardF14"));
        config.Keyboard.F15 = Hotkeys.Normalize(GetText("KeyboardF15"));
        config.Keyboard.F16 = Hotkeys.Normalize(GetText("KeyboardF16"));
        config.Keyboard.F17 = Hotkeys.Normalize(GetText("KeyboardF17"));
        config.Keyboard.F18 = Hotkeys.Normalize(GetText("KeyboardF18"));
        config.Keyboard.F19 = Hotkeys.Normalize(GetText("KeyboardF19"));
    }

    private void ValidateHotkeys()
    {
        foreach (var value in AllHotkeys())
        {
            Hotkeys.Parse(value);
        }
    }

    private IEnumerable<string> AllHotkeys()
    {
        yield return config.Gestures.PinchZoomModifier;
        yield return config.Gestures.Hotkeys.TwoFingerSwipeLeft;
        yield return config.Gestures.Hotkeys.TwoFingerSwipeRight;
        yield return config.Gestures.Hotkeys.SmartZoomIn;
        yield return config.Gestures.Hotkeys.SmartZoomOut;
        yield return config.Gestures.Hotkeys.RotateClockwise;
        yield return config.Gestures.Hotkeys.RotateCounterClockwise;
        yield return config.Gestures.Hotkeys.FourFingerPinchIn;
        yield return config.Gestures.Hotkeys.FourFingerSpread;
        yield return config.Gestures.Hotkeys.FourFingerTap;
        yield return config.Gestures.Hotkeys.ThreeFingerTap;
        yield return config.Gestures.Hotkeys.ThreeFingerSwipeLeft;
        yield return config.Gestures.Hotkeys.ThreeFingerSwipeRight;
        yield return config.Gestures.Hotkeys.ThreeFingerSwipeUp;
        yield return config.Gestures.Hotkeys.ThreeFingerSwipeDown;
        yield return config.Gestures.Hotkeys.FourFingerSwipeLeft;
        yield return config.Gestures.Hotkeys.FourFingerSwipeRight;
        yield return config.Gestures.Hotkeys.FourFingerSwipeUp;
        yield return config.Gestures.Hotkeys.FourFingerSwipeDown;
        yield return config.Keyboard.F1;
        yield return config.Keyboard.F2;
        yield return config.Keyboard.F3;
        yield return config.Keyboard.F4;
        yield return config.Keyboard.F5;
        yield return config.Keyboard.F6;
        yield return config.Keyboard.F7;
        yield return config.Keyboard.F8;
        yield return config.Keyboard.F9;
        yield return config.Keyboard.F10;
        yield return config.Keyboard.F11;
        yield return config.Keyboard.F12;
        yield return config.Keyboard.FnGlobe;
        yield return config.Keyboard.F13;
        yield return config.Keyboard.F14;
        yield return config.Keyboard.F15;
        yield return config.Keyboard.F16;
        yield return config.Keyboard.F17;
        yield return config.Keyboard.F18;
        yield return config.Keyboard.F19;
    }

    private void ShowAllKeyMappings()
    {
        ReadValues();
        using var dialog = new Form
        {
            Text = "F-key mappings",
            Width = 520,
            Height = 680,
            StartPosition = FormStartPosition.CenterParent,
            Font = Font,
        };
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(14),
        };
        dialog.Controls.Add(panel);

        var edits = new Dictionary<string, TextBox>();
        foreach (var key in Enumerable.Range(1, 19).Select(index => $"F{index}"))
        {
            var row = Row();
            row.Controls.Add(new Label { Text = key, Width = 70, TextAlign = ContentAlignment.MiddleLeft });
            var textBox = new TextBox { Width = 260, Text = KeyboardKeyValue(key) };
            row.Controls.Add(textBox);
            var record = new Button { Text = "Record", Width = 82, Height = 28 };
            record.Click += (_, _) => RecordHotkey(textBox);
            row.Controls.Add(record);
            edits[key] = textBox;
            panel.Controls.Add(row);
        }

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Width = 450, Height = 42 };
        var ok = new Button { Text = "Done", Width = 90, Height = 30 };
        ok.Click += (_, _) =>
        {
            foreach (var item in edits)
            {
                SetKeyboardKeyValue(item.Key, Hotkeys.Normalize(item.Value.Text));
            }
            Set("KeyboardF13", config.Keyboard.F13);
            Set("KeyboardF14", config.Keyboard.F14);
            Set("KeyboardF15", config.Keyboard.F15);
            Set("KeyboardF16", config.Keyboard.F16);
            Set("KeyboardF17", config.Keyboard.F17);
            Set("KeyboardF18", config.Keyboard.F18);
            Set("KeyboardF19", config.Keyboard.F19);
            ScheduleAutoSave();
            dialog.Close();
        };
        buttons.Controls.Add(ok);
        panel.Controls.Add(buttons);
        dialog.ShowDialog(this);
    }

    private string KeyboardKeyValue(string key) => key switch
    {
        "F1" => config.Keyboard.F1,
        "F2" => config.Keyboard.F2,
        "F3" => config.Keyboard.F3,
        "F4" => config.Keyboard.F4,
        "F5" => config.Keyboard.F5,
        "F6" => config.Keyboard.F6,
        "F7" => config.Keyboard.F7,
        "F8" => config.Keyboard.F8,
        "F9" => config.Keyboard.F9,
        "F10" => config.Keyboard.F10,
        "F11" => config.Keyboard.F11,
        "F12" => config.Keyboard.F12,
        "F13" => config.Keyboard.F13,
        "F14" => config.Keyboard.F14,
        "F15" => config.Keyboard.F15,
        "F16" => config.Keyboard.F16,
        "F17" => config.Keyboard.F17,
        "F18" => config.Keyboard.F18,
        "F19" => config.Keyboard.F19,
        _ => "unchanged",
    };

    private void SetKeyboardKeyValue(string key, string value)
    {
        switch (key)
        {
            case "F1": config.Keyboard.F1 = value; break;
            case "F2": config.Keyboard.F2 = value; break;
            case "F3": config.Keyboard.F3 = value; break;
            case "F4": config.Keyboard.F4 = value; break;
            case "F5": config.Keyboard.F5 = value; break;
            case "F6": config.Keyboard.F6 = value; break;
            case "F7": config.Keyboard.F7 = value; break;
            case "F8": config.Keyboard.F8 = value; break;
            case "F9": config.Keyboard.F9 = value; break;
            case "F10": config.Keyboard.F10 = value; break;
            case "F11": config.Keyboard.F11 = value; break;
            case "F12": config.Keyboard.F12 = value; break;
            case "F13": config.Keyboard.F13 = value; break;
            case "F14": config.Keyboard.F14 = value; break;
            case "F15": config.Keyboard.F15 = value; break;
            case "F16": config.Keyboard.F16 = value; break;
            case "F17": config.Keyboard.F17 = value; break;
            case "F18": config.Keyboard.F18 = value; break;
            case "F19": config.Keyboard.F19 = value; break;
        }
    }

    private void RecordHotkey(TextBox textBox)
    {
        using var dialog = new Form
        {
            Text = "Record keybind",
            Width = 360,
            Height = 130,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            KeyPreview = true,
        };
        dialog.Controls.Add(new Label { Text = "Press the key combination.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
        dialog.KeyDown += (_, e) =>
        {
            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Shift) parts.Add("Shift");
            if (e.Alt) parts.Add("Alt");
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu)
            {
                return;
            }
            parts.Add(KeyName(e.KeyCode));
            textBox.Text = Hotkeys.Normalize(string.Join("+", parts));
            dialog.Close();
        };
        dialog.ShowDialog(this);
    }

    private List<DeviceTabInfo> DeviceTabs()
    {
        IReadOnlyList<HidDeviceInfo> devices;
        try
        {
            devices = DeviceActions.EnumerateRawInputDevices();
        }
        catch
        {
            devices = [];
        }

        var batteries = DeviceBattery.QueryApplePeripheralBatteries(devices);
        var result = new List<DeviceTabInfo>();
        foreach (var device in DeviceCatalog.FindMagicTrackpads(devices).GroupBy(item => DeviceActions.PhysicalKey(item.Name)).Select(group => group.First()))
        {
            result.Add(new DeviceTabInfo(DeviceKind.Trackpad, DeviceTitle(device, "Magic Trackpad"), device, true, BatteryFor(device, batteries)));
        }

        foreach (var device in DeviceCatalog.FindAppleKeyboards(devices).GroupBy(item => DeviceActions.PhysicalKey(item.Name)).Select(group => group.First()))
        {
            result.Add(new DeviceTabInfo(DeviceKind.Keyboard, DeviceTitle(device, "Magic Keyboard"), device, true, BatteryFor(device, batteries)));
        }

        if (!result.Any(item => item.Kind == DeviceKind.Trackpad))
        {
            result.Add(new DeviceTabInfo(DeviceKind.Trackpad, "Magic Trackpad", null, false, DeviceBattery.Unknown()));
        }

        if (!result.Any(item => item.Kind == DeviceKind.Keyboard))
        {
            result.Add(new DeviceTabInfo(DeviceKind.Keyboard, "Magic Keyboard", null, false, DeviceBattery.Unknown()));
        }

        return result;
    }

    private static BatteryStatus BatteryFor(HidDeviceInfo device, IReadOnlyDictionary<string, BatteryStatus> batteries) =>
        batteries.TryGetValue(DeviceActions.PhysicalKey(device.Name), out var status)
            ? status
            : DeviceBattery.Unknown("Battery not reported");

    private static string DeviceTitle(HidDeviceInfo device, string fallback)
    {
        var product = device.ProductName == "Unknown HID device" ? fallback : device.ProductName;
        var transport = device.IsBluetooth ? "Bluetooth" : "USB";
        return $"{product} - {transport}";
    }

    private void DrawDeviceTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs)
        {
            return;
        }

        var page = tabs.TabPages[e.Index];
        var info = page.Tag as DeviceTabInfo;
        var selected = e.State.HasFlag(DrawItemState.Selected);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = Rectangle.Inflate(e.Bounds, -4, -5);
        using (var tabPath = Rounded(bounds, 7))
        using (var background = new SolidBrush(selected ? ThemePalette.Surface : ThemePalette.SurfaceAlt))
        using (var border = new Pen(selected ? ThemePalette.Stroke : ThemePalette.StrokeSoft))
        {
            e.Graphics.FillPath(background, tabPath);
            e.Graphics.DrawPath(border, tabPath);
        }

        if (selected)
        {
            using var accentFill = new SolidBrush(Accent);
            using var accentPath = Rounded(new Rectangle(bounds.Left + 9, bounds.Bottom - 5, bounds.Width - 18, 3), 2);
            e.Graphics.FillPath(accentFill, accentPath);
        }

        using var dot = new SolidBrush(info?.Connected == true ? Accent : Color.FromArgb(164, 170, 180));
        e.Graphics.FillEllipse(dot, bounds.X + 14, bounds.Y + 12, 9, 9);
        TextRenderer.DrawText(
            e.Graphics,
            page.Text,
            Font,
            new Rectangle(bounds.X + 32, bounds.Y + 3, bounds.Width - 38, bounds.Height - 6),
            selected ? TextMain : TextMuted,
            TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
    }

    private static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string KeyName(Keys key) => key switch
    {
        Keys.Left => "Left",
        Keys.Right => "Right",
        Keys.Up => "Up",
        Keys.Down => "Down",
        Keys.LWin or Keys.RWin => "Win",
        _ => key.ToString(),
    };

    private void Set(string name, bool value)
    {
        if (controlsByName.TryGetValue(name, out var control) && control is CheckBox box)
        {
            box.Checked = value;
        }
    }

    private void Set(string name, int value)
    {
        if (controlsByName.TryGetValue(name, out var control) && control is TrackBar slider)
        {
            slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
        }
    }

    private void Set(string name, string value)
    {
        if (!controlsByName.TryGetValue(name, out var control))
        {
            return;
        }

        if (control is ComboBox combo)
        {
            combo.SelectedItem = ComboItemForValue(combo, value) ?? combo.Items[0];
        }
        else if (control is TextBox text)
        {
            text.Text = value;
        }
    }

    private bool GetBool(string name) =>
        controlsByName.TryGetValue(name, out var control) && control is CheckBox box && box.Checked;

    private int GetInt(string name) =>
        controlsByName.TryGetValue(name, out var control) && control is TrackBar slider ? slider.Value : 0;

    private string GetText(string name)
    {
        if (!controlsByName.TryGetValue(name, out var control))
        {
            return "";
        }

        return control switch
        {
            ComboBox combo => combo.SelectedItem is ActionOption action ? action.Value : combo.SelectedItem?.ToString() ?? "",
            TextBox text => text.Text,
            _ => "",
        };
    }

    private static object? ComboItemForValue(ComboBox combo, string value)
    {
        foreach (var item in combo.Items)
        {
            if (item is ActionOption action && string.Equals(action.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }

            if (item is string text && string.Equals(text, value, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private static int Scale(double value, int factor) => (int)Math.Round(value * factor);

    private static string[] ButtonChoices() => ["left", "middle", "right", "none"];

    private static string[] KeyActionChoices() => ["unchanged", "Ctrl", "Alt", "Win", "Shift", "Esc", "CapsLock", "none"];

    private static ActionOption[] TwoFingerLeftActions() =>
    [
        Action("Previous page", "BrowserBack"),
        Action("Previous desktop", "Win+Ctrl+Left"),
        Action("Task View", "Win+Tab"),
        Action("Show desktop", "Win+D"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] TwoFingerRightActions() =>
    [
        Action("Next page", "BrowserForward"),
        Action("Next desktop", "Win+Ctrl+Right"),
        Action("Task View", "Win+Tab"),
        Action("Show desktop", "Win+D"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] SmartZoomInActions() =>
    [
        Action("Zoom in", "Ctrl+Plus"),
        Action("Reset zoom", "Ctrl+0"),
        Action("Search / Spotlight", "Win+S"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] SmartZoomOutActions() =>
    [
        Action("Reset zoom", "Ctrl+0"),
        Action("Zoom out", "Ctrl+Minus"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] RotateClockwiseActions() =>
    [
        Action("Rotate clockwise", "Ctrl+R"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] RotateCounterClockwiseActions() =>
    [
        Action("Rotate counterclockwise", "Ctrl+Shift+R"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] TapActions() =>
    [
        Action("Middle click", "none"),
        Action("Search / Spotlight", "Win+S"),
        Action("Task View", "Win+Tab"),
        Action("Notification Center", "Win+N"),
    ];

    private static ActionOption[] DesktopLeftActions() =>
    [
        Action("Previous desktop", "Win+Ctrl+Left"),
        Action("Previous page", "BrowserBack"),
        Action("Task View", "Win+Tab"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] DesktopRightActions() =>
    [
        Action("Next desktop", "Win+Ctrl+Right"),
        Action("Next page", "BrowserForward"),
        Action("Task View", "Win+Tab"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] SwipeUpActions() =>
    [
        Action("Task View", "Win+Tab"),
        Action("Search / Spotlight", "Win+S"),
        Action("Start / Launchpad", "Win"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] SwipeDownActions() =>
    [
        Action("Show desktop", "Win+D"),
        Action("App switcher", "Alt+Tab"),
        Action("Notification Center", "Win+N"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] PinchInActions() =>
    [
        Action("Start / Launchpad", "Win"),
        Action("Search / Spotlight", "Win+S"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] SpreadActions() =>
    [
        Action("Show desktop", "Win+D"),
        Action("Task View", "Win+Tab"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] FourFingerTapActions() =>
    [
        Action("Notification Center", "Win+N"),
        Action("Start / Launchpad", "Win"),
        Action("Search / Spotlight", "Win+S"),
        Action("Show desktop", "Win+D"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption Action(string label, string value) => new(label, value);
}

internal sealed record ActionOption(string Label, string Value)
{
    public override string ToString() => Label;
}

internal static class ThemePalette
{
    public static readonly Color Shell = Color.FromArgb(248, 249, 251);
    public static readonly Color Surface = Color.FromArgb(255, 255, 255);
    public static readonly Color SurfaceAlt = Color.FromArgb(243, 245, 248);
    public static readonly Color SurfaceRaised = Color.FromArgb(252, 252, 254);
    public static readonly Color Control = Color.FromArgb(252, 253, 255);
    public static readonly Color ControlHover = Color.FromArgb(232, 243, 255);
    public static readonly Color ControlPressed = Color.FromArgb(210, 231, 255);
    public static readonly Color Stroke = Color.FromArgb(204, 211, 221);
    public static readonly Color StrokeSoft = Color.FromArgb(226, 230, 236);
    public static readonly Color TextMain = Color.FromArgb(29, 29, 31);
    public static readonly Color TextMuted = Color.FromArgb(102, 109, 119);
    public static readonly Color Accent = Color.FromArgb(0, 112, 243);
    public static readonly Color AccentDim = Color.FromArgb(54, 148, 255);
    public static readonly Color AccentSurface = Color.FromArgb(234, 244, 255);
}

internal enum DeviceKind
{
    Trackpad,
    Keyboard,
}

internal sealed record DeviceTabInfo(DeviceKind Kind, string Title, HidDeviceInfo? Device, bool Connected, BatteryStatus Battery);

internal sealed class AppHeader : Control
{
    private readonly DeviceTabInfo[] devices;

    public AppHeader(DeviceTabInfo[] devices)
    {
        this.devices = devices;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(ThemePalette.Surface);

        using (var border = new Pen(ThemePalette.StrokeSoft))
        {
            e.Graphics.DrawLine(border, 0, Height - 1, Width, Height - 1);
        }

        using var eyebrowFont = new Font("Segoe UI Semibold", 7.8F);
        using var titleFont = new Font("Segoe UI Semibold", 15F);
        TextRenderer.DrawText(e.Graphics, "DEVICE STUDIO", eyebrowFont, new Rectangle(22, 10, 220, 18), ThemePalette.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(e.Graphics, "Magic Trackpad + Keyboard", titleFont, new Rectangle(22, 26, 480, 34), ThemePalette.TextMain, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoClipping);

        var x = Width - 24;
        foreach (var device in devices.GroupBy(item => item.Kind).Select(group => group.First()).Reverse())
        {
            var label = device.Kind == DeviceKind.Trackpad ? "Trackpad" : "Keyboard";
            var width = device.Connected ? 176 : 194;
            x -= width;
            DrawStatusChip(e.Graphics, new Rectangle(x, 18, width, 30), label, device.Connected);
            x -= 10;
        }

        x -= 156;
        DrawProfileChip(e.Graphics, new Rectangle(Math.Max(470, x), 18, 146, 30));
    }

    private static void DrawProfileChip(Graphics graphics, Rectangle rect)
    {
        using var path = Rounded(rect, 8);
        using var fill = new SolidBrush(ThemePalette.SurfaceAlt);
        using var border = new Pen(ThemePalette.StrokeSoft);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        using var labelFont = new Font("Segoe UI Semibold", 8.5F);
        TextRenderer.DrawText(graphics, "Profile: Default", labelFont, new Rectangle(rect.Left + 13, rect.Top + 2, rect.Width - 24, rect.Height - 4), ThemePalette.TextMain, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static void DrawStatusChip(Graphics graphics, Rectangle rect, string label, bool connected)
    {
        using var path = Rounded(rect, 8);
        using var fill = new SolidBrush(connected ? ThemePalette.AccentSurface : ThemePalette.SurfaceAlt);
        using var border = new Pen(connected ? Color.FromArgb(166, 204, 245) : ThemePalette.StrokeSoft);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        using var dot = new SolidBrush(connected ? ThemePalette.Accent : Color.FromArgb(160, 167, 178));
        graphics.FillEllipse(dot, rect.Left + 12, rect.Top + 10, 9, 9);

        var status = connected ? "Detected" : "Not detected";
        using var font = new Font("Segoe UI", 8.6F);
        TextRenderer.DrawText(graphics, $"{label} {status}", font, new Rectangle(rect.Left + 27, rect.Top + 2, rect.Width - 34, rect.Height - 4), ThemePalette.TextMain, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class ModeStrip : Control
{
    private readonly string selected;
    private readonly string device;
    private readonly string[] modes;

    public ModeStrip(string selected, string device, string[] modes)
    {
        this.selected = selected;
        this.device = device;
        this.modes = modes;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Rounded(rect, 8))
        using (var fill = new SolidBrush(ThemePalette.Surface))
        using (var border = new Pen(ThemePalette.StrokeSoft))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        using var eyebrowFont = new Font("Segoe UI Semibold", 7.8F);
        using var titleFont = new Font("Segoe UI Semibold", 13F);
        TextRenderer.DrawText(e.Graphics, selected.ToUpperInvariant(), eyebrowFont, new Rectangle(18, 10, 170, 18), ThemePalette.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(e.Graphics, device, titleFont, new Rectangle(18, 29, 260, 28), ThemePalette.TextMain, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var x = Width - 18;
        using var chipFont = new Font("Segoe UI", 8.6F);
        for (var index = modes.Length - 1; index >= 0; index--)
        {
            var text = modes[index];
            var size = TextRenderer.MeasureText(e.Graphics, text, chipFont, new Size(180, 24), TextFormatFlags.NoPadding);
            var chipWidth = Math.Clamp(size.Width + 28, 86, 142);
            x -= chipWidth;
            DrawModeChip(e.Graphics, new Rectangle(x, 22, chipWidth, 30), text, index == 0);
            x -= 8;
        }
    }

    private static void DrawModeChip(Graphics graphics, Rectangle rect, string text, bool active)
    {
        using var path = Rounded(rect, 8);
        using var fill = new SolidBrush(active ? ThemePalette.AccentSurface : ThemePalette.SurfaceAlt);
        using var border = new Pen(active ? Color.FromArgb(173, 210, 250) : ThemePalette.StrokeSoft);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        using var font = new Font("Segoe UI", 8.6F);
        TextRenderer.DrawText(graphics, text, font, rect, active ? ThemePalette.TextMain : ThemePalette.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class ThemedGroupBox : GroupBox
{
    public ThemedGroupBox()
    {
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? ThemePalette.Shell);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var title = Text.Trim();
        var panelRect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var panelPath = Rounded(panelRect, 8);
        using var fill = new SolidBrush(BackColor);
        using var border = new Pen(ThemePalette.StrokeSoft);
        e.Graphics.FillPath(fill, panelPath);
        e.Graphics.DrawPath(border, panelPath);

        using (var headerLine = new Pen(ThemePalette.StrokeSoft))
        {
            e.Graphics.DrawLine(headerLine, 16, 43, Width - 16, 43);
        }

        using var titleFont = new Font(Font, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, title, titleFont, new Rectangle(18, 8, Width - 36, 28), ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

}

internal sealed class LevelMeter : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int? Value { get; init; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var background = new SolidBrush(ThemePalette.SurfaceAlt);
        using var border = new Pen(ThemePalette.StrokeSoft);
        using var meterPath = Rounded(bounds, 8);
        e.Graphics.FillPath(background, meterPath);
        e.Graphics.DrawPath(border, meterPath);

        var fillWidth = Value is int value
            ? Math.Max(0, (int)Math.Round((Width - 2) * Math.Clamp(value, 0, 100) / 100.0))
            : 0;
        if (fillWidth > 0)
        {
            var fillRect = new Rectangle(1, 1, fillWidth, Height - 2);
            using var fillPath = Rounded(fillRect, Math.Max(1, Math.Min(7, fillRect.Width / 2)));
            using var fill = new LinearGradientBrush(fillRect, ThemePalette.Accent, ThemePalette.AccentDim, LinearGradientMode.Horizontal);
            e.Graphics.FillPath(fill, fillPath);
        }

        var textColor = Value is int percentValue && percentValue > 45 ? Color.White : ThemePalette.TextMain;
        TextRenderer.DrawText(e.Graphics, Value is int percent ? $"{percent}%" : "--", Font, bounds, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class TrackpadPreview : Control
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var stage = new Rectangle(4, 8, Width - 8, Height - 16);
        DrawStage(e.Graphics, stage);

        var width = Math.Min(Math.Max(230, stage.Width - 70), 560);
        var height = Math.Min(Math.Max(148, stage.Height - 58), (int)Math.Round(width * 0.72));
        var body = new Rectangle(
            stage.Left + (stage.Width - width) / 2,
            stage.Top + Math.Max(18, (stage.Height - height) / 2),
            width,
            height);
        var shadow = new Rectangle(body.Left + 18, body.Bottom - 6, body.Width - 36, 22);

        using (var shadowPath = Rounded(shadow, 24))
        using (var shadowBrush = new PathGradientBrush(shadowPath)
        {
            CenterColor = Color.FromArgb(44, 105, 112, 125),
            SurroundColors = [Color.FromArgb(0, 0, 0, 0)],
        })
        {
            e.Graphics.FillPath(shadowBrush, shadowPath);
        }

        using var bodyPath = Rounded(body, Math.Max(16, body.Height / 12));
        using var glassFill = new LinearGradientBrush(body, Color.FromArgb(255, 255, 255), Color.FromArgb(231, 234, 241), LinearGradientMode.Vertical);
        using var glassBorder = new Pen(Color.FromArgb(191, 196, 205), 1.1F);
        e.Graphics.FillPath(glassFill, bodyPath);
        e.Graphics.DrawPath(glassBorder, bodyPath);

        var topGlow = new Rectangle(body.Left + 12, body.Top + 10, body.Width - 24, body.Height / 2);
        using (var glowPath = Rounded(topGlow, Math.Max(14, body.Height / 15)))
        using (var glowFill = new LinearGradientBrush(topGlow, Color.FromArgb(150, 255, 255, 255), Color.FromArgb(8, 255, 255, 255), LinearGradientMode.Vertical))
        {
            e.Graphics.FillPath(glowFill, glowPath);
        }

        var inner = Rectangle.Inflate(body, -8, -8);
        using (var highlight = new Pen(Color.FromArgb(190, 255, 255, 255), 1.6F))
        {
            e.Graphics.DrawArc(highlight, inner.Left, inner.Top, inner.Width, inner.Height, 194, 146);
        }

        var bottomFade = new Rectangle(body.Left + 1, body.Bottom - Math.Max(28, body.Height / 5), body.Width - 2, Math.Max(28, body.Height / 5));
        using (var fadePath = BottomRounded(bottomFade, Math.Max(16, body.Height / 12)))
        using (var fadeFill = new LinearGradientBrush(bottomFade, Color.FromArgb(0, 212, 216, 225), Color.FromArgb(58, 190, 195, 204), LinearGradientMode.Vertical))
        {
            e.Graphics.FillPath(fadeFill, fadePath);
        }
    }

    private static void DrawStage(Graphics graphics, Rectangle rect)
    {
        using (var stagePath = Rounded(rect, 8))
        using (var fill = new SolidBrush(Color.White))
        using (var border = new Pen(ThemePalette.StrokeSoft))
        {
            graphics.FillPath(fill, stagePath);
            graphics.DrawPath(border, stagePath);
        }
    }

    private static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath BottomRounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddLine(rect.Left, rect.Top, rect.Right, rect.Top);
        path.AddLine(rect.Right, rect.Top, rect.Right, rect.Bottom - radius);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.AddLine(rect.Left, rect.Bottom - radius, rect.Left, rect.Top);
        path.CloseFigure();
        return path;
    }
}

internal sealed class KeyboardPreview : Control
{
    private enum KeyGlyph
    {
        None,
        BrightnessDown,
        BrightnessUp,
        MissionControl,
        Search,
        Microphone,
        Moon,
        Rewind,
        PlayPause,
        Forward,
        VolumeMute,
        VolumeDown,
        VolumeUp,
        Lock,
        Globe,
        Control,
        Option,
        Command,
        UpDown,
        UpArrow,
        DownArrow,
        LeftArrow,
        RightArrow,
    }

    private enum ArrowDirection
    {
        Up,
        Down,
        Left,
        Right,
    }

    private enum LabelAlign
    {
        Center,
        Left,
        Right,
    }

    private readonly record struct KeyboardKey(string Main, string Top, string Bottom, float Units, KeyGlyph Glyph, LabelAlign Align);

    private readonly record struct KeyHitRegion(string Id, Rectangle Bounds);

    private readonly List<KeyHitRegion> keyRegions = [];
    private readonly System.Windows.Forms.Timer keyLightTimer;
    private string? hoverKeyId;
    private string? litKeyId;

    public KeyboardPreview()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
            true);

        TabStop = false;
        keyLightTimer = new System.Windows.Forms.Timer { Interval = 900 };
        keyLightTimer.Tick += (_, _) =>
        {
            keyLightTimer.Stop();
            litKeyId = null;
            Invalidate();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            keyLightTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTestKey(e.Location);
        var nextHover = hit?.Id;
        if (nextHover == hoverKeyId)
        {
            return;
        }

        hoverKeyId = nextHover;
        Cursor = hit is null ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverKeyId is null)
        {
            return;
        }

        hoverKeyId = null;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var hit = HitTestKey(e.Location);
        if (hit is null)
        {
            return;
        }

        keyLightTimer.Stop();
        litKeyId = hit.Value.Id;
        Invalidate(hit.Value.Bounds);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (litKeyId is null)
        {
            return;
        }

        keyLightTimer.Stop();
        keyLightTimer.Start();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var stage = new Rectangle(0, 0, Width - 1, Height - 1);
        DrawStage(e.Graphics, stage);

        var availableWidth = Math.Max(360, stage.Width - 18);
        var availableHeight = Math.Max(210, stage.Height - 18);
        var width = Math.Min(availableWidth, (int)Math.Round(availableHeight / 0.36));
        var height = (int)Math.Round(width * 0.36);
        var keyboard = new Rectangle(stage.Left + (stage.Width - width) / 2, stage.Top + (stage.Height - height) / 2, width, height);

        using (var shadowPath = Rounded(new Rectangle(keyboard.Left + 12, keyboard.Bottom - 2, keyboard.Width - 24, Math.Max(10, keyboard.Height / 13)), Math.Max(10, keyboard.Height / 9)))
        using (var shadowBrush = new PathGradientBrush(shadowPath)
        {
            CenterColor = Color.FromArgb(62, 0, 0, 0),
            SurroundColors = [Color.FromArgb(0, 0, 0, 0)],
        })
        {
            e.Graphics.FillPath(shadowBrush, shadowPath);
        }

        var bodyRadius = Math.Max(18, (int)Math.Round(width * 0.024));
        using (var bodyPath = Rounded(keyboard, bodyRadius))
        using (var bodyFill = new LinearGradientBrush(keyboard, Color.FromArgb(232, 234, 236), Color.FromArgb(174, 178, 183), LinearGradientMode.Vertical))
        using (var border = new Pen(Color.FromArgb(130, 136, 142), Math.Max(1.2F, width * 0.0018F)))
        {
            e.Graphics.FillPath(bodyFill, bodyPath);
            e.Graphics.DrawPath(border, bodyPath);
            using var innerGlow = new Pen(Color.FromArgb(120, 255, 255, 255), Math.Max(1F, width * 0.0013F));
            e.Graphics.DrawPath(innerGlow, bodyPath);
        }

        var grain = new Rectangle(keyboard.Left + 14, keyboard.Top + 8, keyboard.Width - 28, keyboard.Height - 16);
        using (var grainPen = new Pen(Color.FromArgb(28, 255, 255, 255)))
        {
            for (var x = grain.Left; x < grain.Right; x += Math.Max(6, keyboard.Width / 140))
            {
                e.Graphics.DrawLine(grainPen, x, grain.Top, x + 12, grain.Bottom);
            }
        }

        var rows = KeyboardRows();
        var padX = Math.Max(12, (int)Math.Round(keyboard.Width * 0.021));
        var padY = Math.Max(10, (int)Math.Round(keyboard.Height * 0.033));
        var rowGap = Math.Max(5, (int)Math.Round(keyboard.Height * 0.017));
        var keyGap = Math.Max(5, (int)Math.Round(keyboard.Width * 0.0086));
        var innerWidth = keyboard.Width - padX * 2;
        var keyHeight = (keyboard.Height - padY * 2 - rowGap * (rows.Length - 1)) / rows.Length;
        var y = keyboard.Top + padY;
        keyRegions.Clear();
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var row = rows[rowIndex];
            var totalUnits = row.Sum(key => key.Units);
            var unit = (innerWidth - keyGap * (row.Length - 1)) / totalUnits;
            var rowWidth = (int)Math.Round(totalUnits * unit + keyGap * (row.Length - 1));
            var x = keyboard.Left + padX + Math.Max(0, (innerWidth - rowWidth) / 2);

            for (var keyIndex = 0; keyIndex < row.Length; keyIndex++)
            {
                var key = row[keyIndex];
                var keyWidth = (int)Math.Round(key.Units * unit);
                var rect = new Rectangle(x, y, keyWidth, keyHeight);
                var keyId = $"{rowIndex}:{keyIndex}";
                keyRegions.Add(new KeyHitRegion(keyId, rect));
                DrawKey(e.Graphics, rect, key, keyId == litKeyId, keyId == hoverKeyId);
                x += keyWidth + keyGap;
            }

            y += keyHeight + rowGap;
        }
    }

    private KeyHitRegion? HitTestKey(Point point)
    {
        for (var i = keyRegions.Count - 1; i >= 0; i--)
        {
            if (keyRegions[i].Bounds.Contains(point))
            {
                return keyRegions[i];
            }
        }

        return null;
    }

    private static void DrawStage(Graphics graphics, Rectangle rect)
    {
        using (var stagePath = Rounded(rect, 8))
        using (var fill = new SolidBrush(Color.White))
        using (var border = new Pen(ThemePalette.StrokeSoft))
        {
            graphics.FillPath(fill, stagePath);
            graphics.DrawPath(border, stagePath);
        }
    }

    private static void DrawKey(Graphics graphics, Rectangle rect, KeyboardKey key, bool lit, bool hovered)
    {
        var textColor = lit ? KeyLitTextColor : KeyTextColor;
        if (key.Glyph == KeyGlyph.UpDown)
        {
            DrawKeyShell(graphics, rect, lit, hovered);
            using var splitPen = new Pen(lit ? KeyLitBorderColor : Color.FromArgb(122, 126, 132), Math.Max(1F, rect.Height * 0.032F));
            graphics.DrawLine(splitPen, rect.Left + 2, rect.Top + rect.Height / 2, rect.Right - 2, rect.Top + rect.Height / 2);
            using var brush = new SolidBrush(textColor);
            DrawArrow(graphics, new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height / 2), ArrowDirection.Up, brush);
            DrawArrow(graphics, new Rectangle(rect.Left, rect.Top + rect.Height / 2, rect.Width, rect.Height / 2), ArrowDirection.Down, brush);
            return;
        }

        DrawKeyShell(graphics, rect, lit, hovered);

        if (key.Glyph != KeyGlyph.None)
        {
            DrawGlyph(graphics, rect, key.Glyph, textColor);
        }

        if (key.Top.Length > 0)
        {
            DrawText(graphics, key.Top, rect, textColor, KeyTopSize(rect), ContentAlignment.TopCenter, new Padding(0, Math.Max(4, rect.Height / 11), 0, 0), false);
        }

        if (key.Main.Length > 0)
        {
            if (key.Align != LabelAlign.Center)
            {
                var align = key.Align == LabelAlign.Left ? ContentAlignment.BottomLeft : ContentAlignment.BottomRight;
                var horizontalInset = Math.Max(8, rect.Width / 10);
                DrawText(graphics, key.Main, rect, textColor, KeySmallLabelSize(rect), align, new Padding(horizontalInset, 0, horizontalInset, Math.Max(6, rect.Height / 9)), false);
            }
            else if (key.Glyph != KeyGlyph.None)
            {
                DrawText(graphics, key.Main, rect, textColor, KeyBottomLabelSize(rect), ContentAlignment.BottomCenter, new Padding(0, 0, 0, Math.Max(5, rect.Height / 12)), false);
            }
            else if (key.Top.Length > 0)
            {
                DrawText(graphics, key.Main, rect, textColor, KeyMainSize(rect), ContentAlignment.BottomCenter, new Padding(0, 0, 0, Math.Max(4, rect.Height / 13)), true);
            }
            else if (key.Main.Length == 1)
            {
                DrawText(graphics, key.Main, rect, textColor, KeyLetterSize(rect), ContentAlignment.MiddleCenter, Padding.Empty, true);
            }
            else
            {
                DrawText(graphics, key.Main, rect, textColor, KeySmallLabelSize(rect), ContentAlignment.MiddleCenter, Padding.Empty, false);
            }
        }

        if (key.Bottom.Length > 0)
        {
            DrawText(graphics, key.Bottom, rect, textColor, KeyBottomLabelSize(rect), ContentAlignment.BottomCenter, new Padding(0, 0, 0, Math.Max(5, rect.Height / 12)), false);
        }
    }

    private static void DrawKeyShell(Graphics graphics, Rectangle rect, bool lit, bool hovered)
    {
        var radius = Math.Max(6, Math.Min(11, rect.Height / 5));
        var shadowOffset = lit ? 1 : 2;
        using (var shadowPath = Rounded(new Rectangle(rect.Left + 1, rect.Top + shadowOffset, rect.Width, rect.Height), radius))
        using (var shadow = new SolidBrush(lit ? Color.FromArgb(26, 0, 92, 180) : Color.FromArgb(42, 0, 0, 0)))
        {
            graphics.FillPath(shadow, shadowPath);
        }

        using var path = Rounded(rect, radius);
        var top = lit ? Color.FromArgb(235, 247, 255) : hovered ? Color.FromArgb(250, 253, 255) : Color.FromArgb(254, 255, 255);
        var bottom = lit ? Color.FromArgb(196, 227, 255) : hovered ? Color.FromArgb(231, 241, 249) : Color.FromArgb(238, 240, 244);
        var borderColor = lit ? KeyLitBorderColor : hovered ? Color.FromArgb(75, 130, 170) : Color.FromArgb(47, 50, 56);
        using var fill = new LinearGradientBrush(rect, top, bottom, LinearGradientMode.Vertical);
        using var border = new Pen(borderColor, lit ? Math.Max(1.8F, rect.Height * 0.052F) : Math.Max(1.15F, rect.Height * 0.042F));
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        var shineRect = new Rectangle(rect.Left + 3, rect.Top + 3, Math.Max(1, rect.Width - 6), Math.Max(1, rect.Height / 2));
        using var highlight = new LinearGradientBrush(shineRect, lit ? Color.FromArgb(190, 255, 255, 255) : Color.FromArgb(150, 255, 255, 255), Color.FromArgb(15, 255, 255, 255), LinearGradientMode.Vertical);
        using var highlightPath = Rounded(shineRect, Math.Max(4, radius - 2));
        graphics.FillPath(highlight, highlightPath);

        if (lit)
        {
            var glowRect = Rectangle.Inflate(rect, -Math.Max(3, rect.Width / 18), -Math.Max(3, rect.Height / 8));
            using var glowPath = Rounded(glowRect, Math.Max(4, radius - 4));
            using var glow = new SolidBrush(Color.FromArgb(80, 120, 190, 255));
            graphics.FillPath(glow, glowPath);
        }
    }

    private static KeyboardKey[][] KeyboardRows() =>
    [
        [Key("esc", 1.55F, align: LabelAlign.Left), Key("F1", 1, glyph: KeyGlyph.BrightnessDown), Key("F2", 1, glyph: KeyGlyph.BrightnessUp), Key("F3", 1, glyph: KeyGlyph.MissionControl), Key("F4", 1, glyph: KeyGlyph.Search), Key("F5", 1, glyph: KeyGlyph.Microphone), Key("F6", 1, glyph: KeyGlyph.Moon), Key("F7", 1, glyph: KeyGlyph.Rewind), Key("F8", 1, glyph: KeyGlyph.PlayPause), Key("F9", 1, glyph: KeyGlyph.Forward), Key("F10", 1, glyph: KeyGlyph.VolumeMute), Key("F11", 1, glyph: KeyGlyph.VolumeDown), Key("F12", 1, glyph: KeyGlyph.VolumeUp), Key("", 1.25F, glyph: KeyGlyph.Lock)],
        [Key("\\", 1.05F, top: "~"), Key("1", 1, top: "!"), Key("2", 1, top: "@"), Key("3", 1, top: "#"), Key("4", 1, top: "$"), Key("5", 1, top: "%"), Key("6", 1, top: "^"), Key("7", 1, top: "&"), Key("8", 1, top: "*"), Key("9", 1, top: "("), Key("0", 1, top: ")"), Key("-", 1, top: "_"), Key("=", 1, top: "+"), Key("delete", 1.65F, align: LabelAlign.Right)],
        [Key("tab", 1.55F, align: LabelAlign.Left), Key("Q", 1), Key("W", 1), Key("E", 1), Key("R", 1), Key("T", 1), Key("Y", 1), Key("U", 1), Key("I", 1), Key("O", 1), Key("P", 1), Key("[", 1, top: "{"), Key("]", 1, top: "}"), Key("\\", 1.25F, top: "|")],
        [Key("caps lock", 1.85F, align: LabelAlign.Left), Key("A", 1), Key("S", 1), Key("D", 1), Key("F", 1), Key("G", 1), Key("H", 1), Key("J", 1), Key("K", 1), Key("L", 1), Key(";", 1, top: ":"), Key("'", 1, top: "\""), Key("return", 1.95F, align: LabelAlign.Right)],
        [Key("shift", 2.55F, align: LabelAlign.Left), Key("Z", 1), Key("X", 1), Key("C", 1), Key("V", 1), Key("B", 1), Key("N", 1), Key("M", 1), Key(",", 1, top: "<"), Key(".", 1, top: ">"), Key("/", 1, top: "?"), Key("shift", 2.55F, align: LabelAlign.Right)],
        [Key("", 1.05F, glyph: KeyGlyph.Globe), Key("control", 1.05F, glyph: KeyGlyph.Control), Key("option", 1.05F, glyph: KeyGlyph.Option), Key("command", 1.35F, glyph: KeyGlyph.Command), Key("", 5.55F), Key("command", 1.35F, glyph: KeyGlyph.Command), Key("option", 1.05F, glyph: KeyGlyph.Option), Key("", 1, glyph: KeyGlyph.LeftArrow), Key("", 1, glyph: KeyGlyph.UpDown), Key("", 1, glyph: KeyGlyph.RightArrow)],
    ];

    private static KeyboardKey Key(string main, float units, string top = "", string bottom = "", KeyGlyph glyph = KeyGlyph.None, LabelAlign align = LabelAlign.Center)
    {
        return new KeyboardKey(main, top, bottom, units, glyph, align);
    }

    private static Color KeyTextColor => Color.FromArgb(128, 132, 136);

    private static Color KeyLitTextColor => Color.FromArgb(42, 92, 138);

    private static Color KeyLitBorderColor => Color.FromArgb(0, 120, 212);

    private static float KeyTopSize(Rectangle rect) => Math.Clamp(rect.Height * 0.18F, 7.5F, 13F);

    private static float KeyBottomLabelSize(Rectangle rect) => Math.Clamp(rect.Height * 0.17F, 7.2F, 12F);

    private static float KeySmallLabelSize(Rectangle rect) => Math.Clamp(rect.Height * 0.22F, 8.2F, 15F);

    private static float KeyMainSize(Rectangle rect) => Math.Clamp(rect.Height * 0.36F, 12F, 25F);

    private static float KeyLetterSize(Rectangle rect) => Math.Clamp(rect.Height * 0.37F, 12F, 26F);

    private static void DrawText(Graphics graphics, string text, Rectangle rect, Color color, float size, ContentAlignment align, Padding inset, bool light = false)
    {
        using var font = new Font(light ? "Segoe UI Light" : "Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point);
        var area = new Rectangle(rect.Left + inset.Left, rect.Top + inset.Top, rect.Width - inset.Left - inset.Right, rect.Height - inset.Top - inset.Bottom);
        var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        flags |= align switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => TextFormatFlags.HorizontalCenter,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.Left,
        };
        flags |= align switch
        {
            ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight => TextFormatFlags.Top,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => TextFormatFlags.Bottom,
            _ => TextFormatFlags.VerticalCenter,
        };
        TextRenderer.DrawText(graphics, text, font, area, color, flags);
    }

    private static void DrawGlyph(Graphics graphics, Rectangle rect, KeyGlyph glyph, Color color)
    {
        using var pen = new Pen(color, Math.Max(1F, rect.Height * 0.045F))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var brush = new SolidBrush(color);
        var cx = rect.Left + rect.Width / 2;
        var top = rect.Top + Math.Max(6, rect.Height / 6);
        var glyphRect = new Rectangle(cx - rect.Width / 8, top, rect.Width / 4, rect.Height / 4);

        switch (glyph)
        {
            case KeyGlyph.BrightnessDown:
            case KeyGlyph.BrightnessUp:
                DrawSun(graphics, cx, top + rect.Height / 8, Math.Max(3, rect.Height / (glyph == KeyGlyph.BrightnessUp ? 11 : 14)), pen);
                break;
            case KeyGlyph.MissionControl:
                graphics.DrawRectangle(pen, cx - 8, top + 2, 7, 7);
                graphics.DrawRectangle(pen, cx + 2, top + 2, 7, 11);
                graphics.DrawRectangle(pen, cx - 8, top + 13, 11, 7);
                break;
            case KeyGlyph.Search:
                graphics.DrawEllipse(pen, glyphRect);
                graphics.DrawLine(pen, glyphRect.Right - 1, glyphRect.Bottom - 1, glyphRect.Right + 7, glyphRect.Bottom + 7);
                break;
            case KeyGlyph.Microphone:
                using (var micPath = Rounded(new Rectangle(cx - 4, top, 8, 15), 4))
                {
                    graphics.DrawPath(pen, micPath);
                }

                graphics.DrawLine(pen, cx - 10, top + 11, cx + 10, top + 11);
                graphics.DrawLine(pen, cx, top + 16, cx, top + 22);
                break;
            case KeyGlyph.Moon:
                graphics.FillEllipse(brush, new Rectangle(cx - 9, top, 16, 16));
                using (var cutout = new SolidBrush(Color.FromArgb(248, 250, 253)))
                {
                    graphics.FillEllipse(cutout, new Rectangle(cx - 3, top - 2, 16, 16));
                }
                break;
            case KeyGlyph.Rewind:
                DrawText(graphics, "<<", rect, color, 10F, ContentAlignment.TopCenter, new Padding(0, 7, 0, 0));
                break;
            case KeyGlyph.PlayPause:
                DrawText(graphics, ">| |", rect, color, 8.5F, ContentAlignment.TopCenter, new Padding(0, 7, 0, 0));
                break;
            case KeyGlyph.Forward:
                DrawText(graphics, ">>", rect, color, 10F, ContentAlignment.TopCenter, new Padding(0, 7, 0, 0));
                break;
            case KeyGlyph.VolumeMute:
            case KeyGlyph.VolumeDown:
            case KeyGlyph.VolumeUp:
                DrawSpeaker(graphics, cx - 10, top + 7, glyph == KeyGlyph.VolumeMute ? 0 : glyph == KeyGlyph.VolumeDown ? 1 : 2, pen, brush);
                break;
            case KeyGlyph.Lock:
                graphics.DrawArc(pen, cx - 7, top + 3, 14, 14, 180, 180);
                using (var lockPath = Rounded(new Rectangle(cx - 9, top + 13, 18, 14), 3))
                {
                    graphics.DrawPath(pen, lockPath);
                }

                break;
            case KeyGlyph.Globe:
                DrawGlobe(graphics, new Rectangle(cx - 9, rect.Bottom - 25, 18, 18), pen);
                break;
            case KeyGlyph.Control:
                DrawText(graphics, "^", rect, color, 12F, ContentAlignment.TopCenter, new Padding(0, 4, 0, 0));
                break;
            case KeyGlyph.Option:
                graphics.DrawLine(pen, rect.Left + 13, rect.Top + 12, rect.Left + 22, rect.Top + 12);
                graphics.DrawLine(pen, rect.Left + 22, rect.Top + 12, rect.Left + 29, rect.Top + 26);
                graphics.DrawLine(pen, rect.Left + 29, rect.Top + 26, rect.Left + 38, rect.Top + 26);
                break;
            case KeyGlyph.Command:
                DrawCommand(graphics, cx, top + 11, rect.Height / 8, pen);
                break;
            case KeyGlyph.UpArrow:
                DrawArrow(graphics, rect, ArrowDirection.Up, brush);
                break;
            case KeyGlyph.DownArrow:
                DrawArrow(graphics, rect, ArrowDirection.Down, brush);
                break;
            case KeyGlyph.LeftArrow:
                DrawArrow(graphics, rect, ArrowDirection.Left, brush);
                break;
            case KeyGlyph.RightArrow:
                DrawArrow(graphics, rect, ArrowDirection.Right, brush);
                break;
        }
    }

    private static void DrawSun(Graphics graphics, int cx, int cy, int radius, Pen pen)
    {
        graphics.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
        for (var i = 0; i < 8; i++)
        {
            var angle = Math.PI * 2 * i / 8;
            var x1 = cx + (int)Math.Round(Math.Cos(angle) * (radius + 4));
            var y1 = cy + (int)Math.Round(Math.Sin(angle) * (radius + 4));
            var x2 = cx + (int)Math.Round(Math.Cos(angle) * (radius + 8));
            var y2 = cy + (int)Math.Round(Math.Sin(angle) * (radius + 8));
            graphics.DrawLine(pen, x1, y1, x2, y2);
        }
    }

    private static void DrawSpeaker(Graphics graphics, int x, int y, int waves, Pen pen, Brush brush)
    {
        var points = new[]
        {
            new Point(x, y + 8),
            new Point(x + 6, y + 8),
            new Point(x + 13, y + 3),
            new Point(x + 13, y + 21),
            new Point(x + 6, y + 16),
            new Point(x, y + 16),
        };
        graphics.FillPolygon(brush, points);
        for (var i = 0; i < waves; i++)
        {
            graphics.DrawArc(pen, x + 10 + i * 5, y + 5 - i * 2, 10 + i * 5, 14 + i * 4, -42, 84);
        }
    }

    private static void DrawCommand(Graphics graphics, int cx, int cy, int radius, Pen pen)
    {
        var loop = Math.Max(4, radius);
        var offset = Math.Max(7, loop + 3);
        graphics.DrawEllipse(pen, cx - offset - loop, cy - offset - loop, loop * 2, loop * 2);
        graphics.DrawEllipse(pen, cx + offset - loop, cy - offset - loop, loop * 2, loop * 2);
        graphics.DrawEllipse(pen, cx - offset - loop, cy + offset - loop, loop * 2, loop * 2);
        graphics.DrawEllipse(pen, cx + offset - loop, cy + offset - loop, loop * 2, loop * 2);
        graphics.DrawLine(pen, cx - offset, cy - offset + loop, cx - offset, cy + offset - loop);
        graphics.DrawLine(pen, cx + offset, cy - offset + loop, cx + offset, cy + offset - loop);
        graphics.DrawLine(pen, cx - offset + loop, cy - offset, cx + offset - loop, cy - offset);
        graphics.DrawLine(pen, cx - offset + loop, cy + offset, cx + offset - loop, cy + offset);
    }

    private static void DrawArrow(Graphics graphics, Rectangle rect, ArrowDirection direction, Brush brush)
    {
        var cx = rect.Left + rect.Width / 2;
        var cy = rect.Top + rect.Height / 2;
        var size = Math.Max(6, Math.Min(rect.Width, rect.Height) / 5);
        Point[] points = direction switch
        {
            ArrowDirection.Up => new[]
            {
                new Point(cx, cy - size),
                new Point(cx - size, cy + size / 2),
                new Point(cx + size, cy + size / 2),
            },
            ArrowDirection.Down => new[]
            {
                new Point(cx, cy + size),
                new Point(cx - size, cy - size / 2),
                new Point(cx + size, cy - size / 2),
            },
            ArrowDirection.Left => new[]
            {
                new Point(cx - size, cy),
                new Point(cx + size / 2, cy - size),
                new Point(cx + size / 2, cy + size),
            },
            _ => new[]
            {
                new Point(cx + size, cy),
                new Point(cx - size / 2, cy - size),
                new Point(cx - size / 2, cy + size),
            },
        };
        graphics.FillPolygon(brush, points);
    }

    private static void DrawGlobe(Graphics graphics, Rectangle rect, Pen pen)
    {
        graphics.DrawEllipse(pen, rect);
        graphics.DrawLine(pen, rect.Left + 2, rect.Top + rect.Height / 2, rect.Right - 2, rect.Top + rect.Height / 2);
        graphics.DrawArc(pen, rect.Left + 4, rect.Top + 1, rect.Width - 8, rect.Height - 2, 90, 180);
        graphics.DrawArc(pen, rect.Left + 4, rect.Top + 1, rect.Width - 8, rect.Height - 2, -90, 180);
    }

    private static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

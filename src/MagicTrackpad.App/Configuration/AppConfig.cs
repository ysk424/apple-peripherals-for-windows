using System.Text.Json;

namespace MagicTrackpad.Configuration;

public sealed class AppConfig
{
    public GestureConfig Gestures { get; set; } = new();
    public KeyboardConfig Keyboard { get; set; } = new();
    public bool EnableMultitouchOnStart { get; set; } = true;
    // Let the signed Windows driver own USB touch input; avoid duplicate gestures.
    public bool UseWindowsPrecisionTouchpad { get; set; }
    public double ReenableIntervalSeconds { get; set; } = 15.0;
    public bool LogRawReports { get; set; }
    public string RawLogPath { get; set; } = "logs/raw-reports.hex";
}

public sealed class GestureConfig
{
    public bool PointerEnabled { get; set; } = true;
    public double PointerSensitivity { get; set; } = 0.18;
    public bool InvertPointerX { get; set; }
    public bool InvertPointerY { get; set; }
    public bool ScrollEnabled { get; set; } = true;
    public bool NaturalScroll { get; set; } = true;
    public double ScrollSensitivity { get; set; } = 0.42;
    public bool HorizontalScrollEnabled { get; set; } = true;
    public bool TapToClick { get; set; } = true;
    public string OneFingerTapButton { get; set; } = "left";
    public string TwoFingerTapButton { get; set; } = "right";
    public string ThreeFingerTapButton { get; set; } = "middle";
    public string PhysicalClickButton { get; set; } = "left";
    public string MultiFingerPhysicalClickButton { get; set; } = "right";
    public double TapMaxSeconds { get; set; } = 0.18;
    public double TapMaxDistance { get; set; } = 95.0;
    public bool SecondaryClickEnabled { get; set; } = true;
    public bool ThreeFingerMiddleClick { get; set; } = true;
    public bool SwapLeftRightButtons { get; set; }
    public bool PinchZoomEnabled { get; set; } = true;
    public string PinchZoomModifier { get; set; } = "Ctrl";
    public double PinchSensitivity { get; set; } = 0.55;
    public double PinchThreshold { get; set; } = 14.0;
    public bool SmartZoomEnabled { get; set; } = true;
    public double SmartZoomDoubleTapSeconds { get; set; } = 0.35;
    public bool RotateEnabled { get; set; } = true;
    public double RotateThresholdDegrees { get; set; } = 18.0;
    public bool TwoFingerSwipePagesEnabled { get; set; } = true;
    public double TwoFingerSwipeThreshold { get; set; } = 520.0;
    public bool FourFingerPinchEnabled { get; set; } = true;
    public double FourFingerPinchThreshold { get; set; } = 180.0;
    public bool FourFingerTapEnabled { get; set; } = true;
    public bool ThreeFingerSwipesEnabled { get; set; } = true;
    public double SwipeThreshold { get; set; } = 650.0;
    public double SwipeVerticalThreshold { get; set; } = 540.0;
    public HotkeyConfig Hotkeys { get; set; } = new();
}

public sealed class HotkeyConfig
{
    public string TwoFingerSwipeLeft { get; set; } = "BrowserBack";
    public string TwoFingerSwipeRight { get; set; } = "BrowserForward";
    public string SmartZoomIn { get; set; } = "Ctrl+Plus";
    public string SmartZoomOut { get; set; } = "Ctrl+0";
    public string RotateClockwise { get; set; } = "Ctrl+R";
    public string RotateCounterClockwise { get; set; } = "Ctrl+Shift+R";
    public string FourFingerPinchIn { get; set; } = "Win";
    public string FourFingerSpread { get; set; } = "Win+D";
    public string FourFingerTap { get; set; } = "Win+N";
    public string ThreeFingerTap { get; set; } = "none";
    public string ThreeFingerSwipeLeft { get; set; } = "Win+Ctrl+Left";
    public string ThreeFingerSwipeRight { get; set; } = "Win+Ctrl+Right";
    public string ThreeFingerSwipeUp { get; set; } = "Win+Tab";
    public string ThreeFingerSwipeDown { get; set; } = "Win+D";
    public string FourFingerSwipeLeft { get; set; } = "Win+Ctrl+Left";
    public string FourFingerSwipeRight { get; set; } = "Win+Ctrl+Right";
    public string FourFingerSwipeUp { get; set; } = "Win+Tab";
    public string FourFingerSwipeDown { get; set; } = "Win+D";
}

public sealed class KeyboardConfig
{
    public bool Enabled { get; set; } = true;
    public bool OnlyWhenAppleKeyboardPresent { get; set; } = true;
    public bool SwapExchangedKeys { get; set; } = true;
    public string FKeyMode { get; set; } = "custom";
    public string LeftCommand { get; set; } = "Ctrl";
    public string RightCommand { get; set; } = "Ctrl";
    public string LeftControl { get; set; } = "Win";
    public string RightControl { get; set; } = "Win";
    public string LeftOption { get; set; } = "Alt";
    public string RightOption { get; set; } = "Alt";
    public string CapsLock { get; set; } = "CapsLock";
    public string FnGlobe { get; set; } = "Ctrl";
    public string F1 { get; set; } = "BrightnessDown";
    public string F2 { get; set; } = "BrightnessUp";
    public string F3 { get; set; } = "Win+Tab";
    public string F4 { get; set; } = "Win+S";
    public string F5 { get; set; } = "Win+H";
    public string F6 { get; set; } = "Win+N";
    public string F7 { get; set; } = "MediaPrevious";
    public string F8 { get; set; } = "MediaPlayPause";
    public string F9 { get; set; } = "MediaNext";
    public string F10 { get; set; } = "VolumeMute";
    public string F11 { get; set; } = "VolumeDown";
    public string F12 { get; set; } = "VolumeUp";
    public string F13 { get; set; } = "none";
    public string F14 { get; set; } = "none";
    public string F15 { get; set; } = "none";
    public string F16 { get; set; } = "none";
    public string F17 { get; set; } = "none";
    public string F18 { get; set; } = "none";
    public string F19 { get; set; } = "none";

    public void ApplyMacDefaultsIfOldConfig()
    {
        if (string.Equals(FnGlobe, "Win+Period", StringComparison.OrdinalIgnoreCase))
        {
            FnGlobe = "Ctrl";
        }

        if (!IsStandardUnchangedFRow())
        {
            return;
        }

        var defaults = new KeyboardConfig();
        FKeyMode = defaults.FKeyMode;
        F1 = defaults.F1;
        F2 = defaults.F2;
        F3 = defaults.F3;
        F4 = defaults.F4;
        F5 = defaults.F5;
        F6 = defaults.F6;
        F7 = defaults.F7;
        F8 = defaults.F8;
        F9 = defaults.F9;
        F10 = defaults.F10;
        F11 = defaults.F11;
        F12 = defaults.F12;
    }

    private bool IsStandardUnchangedFRow() =>
        string.Equals(FKeyMode, "standard", StringComparison.OrdinalIgnoreCase) &&
        new[] { F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12 }
            .All(value => string.Equals(value, "unchanged", StringComparison.OrdinalIgnoreCase));
}

public static class ConfigStore
{
    public static string DefaultConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".magictrackpad-bridge.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppConfig();
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        config.Gestures ??= new GestureConfig();
        config.Gestures.Hotkeys ??= new HotkeyConfig();
        config.Keyboard ??= new KeyboardConfig();
        config.Keyboard.ApplyMacDefaultsIfOldConfig();
        return config;
    }

    public static void Save(string path, AppConfig config)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json + Environment.NewLine);
    }
}

using System.IO;
using System.Text.Json;
using DuoLaBox.Models;
using Microsoft.Win32;

namespace DuoLaBox.Services;

public sealed class AppSettingsService
{
    private const string StartupRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = AppIdentity.Name;
    private static readonly string[] LegacyStartupValueNames =
    [
        "DoraemonTreasureBag",
        "DoraemonPocket",
        "WindowsToolbox"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly string _legacySettingsPath;

    public AppSettingsService()
    {
        _settingsPath = AppIdentity.GetLocalDataPath("settings.json");
        _legacySettingsPath =
            AppIdentity.GetLegacyLocalDataPath("settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            var path = File.Exists(_settingsPath)
                ? _settingsPath
                : _legacySettingsPath;
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(path);
            var settings =
                JsonSerializer.Deserialize<AppSettings>(
                    json,
                    JsonOptions) ??
                new AppSettings();
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(
                    nameof(AppSettings.RenameContextMenuEnabled),
                    out _) &&
                document.RootElement.TryGetProperty(
                    nameof(AppSettings.ExplorerContextMenusEnabled),
                    out var legacySetting) &&
                legacySetting.ValueKind is
                    JsonValueKind.True or JsonValueKind.False)
            {
                var enabled = legacySetting.GetBoolean();
                settings.RenameContextMenuEnabled = enabled;
                settings.UnlockContextMenuEnabled = enabled;
                settings.ResizeContextMenuEnabled = enabled;
            }

            if (string.Equals(
                    path,
                    _legacySettingsPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                Save(settings);
            }

            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public bool Save(AppSettings settings)
    {
        try
        {
            AtomicFile.WriteAllText(
                _settingsPath,
                JsonSerializer.Serialize(settings, JsonOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool ConfigureStartup(bool enabled, bool silent = false)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                StartupRegistryPath,
                writable: true);
            if (key is null)
            {
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(StartupValueName, throwOnMissingValue: false);
                DeleteLegacyStartupValues(key);
                return true;
            }

            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            key.SetValue(
                StartupValueName,
                silent
                    ? $"\"{executablePath}\" --silent"
                    : $"\"{executablePath}\"",
                RegistryValueKind.String);
            DeleteLegacyStartupValues(key);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void DeleteLegacyStartupValues(RegistryKey key)
    {
        foreach (var valueName in LegacyStartupValueNames)
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }
}

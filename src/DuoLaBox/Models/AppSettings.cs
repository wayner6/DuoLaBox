namespace DuoLaBox.Models;

public enum AppTheme
{
    FollowSystem,
    Light,
    Dark
}

public sealed class AppSettings
{
    public bool CloseToTrayOnClose { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public bool SilentStartup { get; set; }

    public bool ExplorerContextMenusEnabled { get; set; } = true;

    public bool RenameContextMenuEnabled { get; set; } = true;

    public bool UnlockContextMenuEnabled { get; set; } = true;

    public bool ResizeContextMenuEnabled { get; set; } = true;

    public AppTheme Theme { get; set; } = AppTheme.FollowSystem;

    public bool DayNightScheduleEnabled { get; set; }

    public TimeSpan DarkModeStartTime { get; set; } =
        new(20, 0, 0);

    public TimeSpan LightModeStartTime { get; set; } =
        new(7, 0, 0);

    public List<string> FavoriteColors { get; set; } = [];

    public AppSettings Clone() =>
        new()
        {
            CloseToTrayOnClose = CloseToTrayOnClose,
            StartWithWindows = StartWithWindows,
            SilentStartup = SilentStartup,
            ExplorerContextMenusEnabled =
                ExplorerContextMenusEnabled,
            RenameContextMenuEnabled =
                RenameContextMenuEnabled,
            UnlockContextMenuEnabled =
                UnlockContextMenuEnabled,
            ResizeContextMenuEnabled =
                ResizeContextMenuEnabled,
            Theme = Theme,
            DayNightScheduleEnabled = DayNightScheduleEnabled,
            DarkModeStartTime = DarkModeStartTime,
            LightModeStartTime = LightModeStartTime,
            FavoriteColors = [.. FavoriteColors]
        };
}

using System.Diagnostics;
using DuoLaBox.Models;
using Microsoft.Win32;

namespace DuoLaBox.Services;

public sealed class ShellContextMenuService
{
    private const string MenuRoot =
        "Software\\Classes\\*\\shell\\" + AppIdentity.Name;
    private const string SubMenuRoot =
        MenuRoot + "\\shell";
    private const string BrokenSubMenuRoot =
        MenuRoot + "\\ExtendedSubCommandsKey";
    private const string LegacyVerbPrefix =
        "Software\\Classes\\*\\shell\\DoraemonTreasureBag.";
    private const string LegacyMenuRoot =
        "Software\\Classes\\*\\shell\\DoraemonTreasureBag";
    private const string LegacyImageVerb =
        "Software\\Classes\\SystemFileAssociations\\image\\shell\\" +
        "DoraemonTreasureBag.Resize";

    public string? LastErrorMessage { get; private set; }

    public bool Configure(FileToolMode mode, bool enabled)
    {
        try
        {
            LastErrorMessage = null;
            RemoveLegacyEntries();
            var executablePath =
                Environment.ProcessPath ??
                Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            if (enabled)
            {
                EnsureMenuRoot(executablePath);
                RegisterSubCommand(
                    mode,
                    executablePath);
            }
            else
            {
                RemoveSubCommand(mode);
                RemoveMenuRootWhenEmpty();
            }

            NativeMethods.SHChangeNotify(
                NativeMethods.ShcneAssocChanged,
                NativeMethods.ShcnfIdList,
                nint.Zero,
                nint.Zero);
            return true;
        }
        catch (Exception exception)
        {
            LastErrorMessage = exception.Message;
            return false;
        }
    }

    private static void EnsureMenuRoot(string executablePath)
    {
        using var key =
            Registry.CurrentUser.CreateSubKey(MenuRoot, true);
        key.DeleteValue(string.Empty, throwOnMissingValue: false);
        key.SetValue("MUIVerb", AppIdentity.Name);
        key.SetValue("Icon", $"\"{executablePath}\",0");
        key.SetValue("MultiSelectModel", "Player");
        key.SetValue("SubCommands", string.Empty, RegistryValueKind.String);
        key.DeleteSubKeyTree(
            "ExtendedSubCommandsKey",
            throwOnMissingSubKey: false);
        key.CreateSubKey("shell", true)?.Dispose();
    }

    private static void RegisterSubCommand(
        FileToolMode mode,
        string executablePath)
    {
        var definition = GetDefinition(mode);
        using var key = Registry.CurrentUser.CreateSubKey(
            $"{SubMenuRoot}\\{definition.KeyName}",
            true);
        key.DeleteValue(string.Empty, throwOnMissingValue: false);
        key.SetValue("MUIVerb", definition.Label);
        key.SetValue("Icon", $"\"{executablePath}\",0");
        key.SetValue("MultiSelectModel", "Player");
        using var command = key.CreateSubKey("command", true);
        command.SetValue(
            string.Empty,
            $"\"{executablePath}\" --file-tool " +
            $"{definition.CommandMode} \"%1\"");
    }

    private static void RemoveSubCommand(FileToolMode mode)
    {
        var definition = GetDefinition(mode);
        Registry.CurrentUser.DeleteSubKeyTree(
            $"{SubMenuRoot}\\{definition.KeyName}",
            throwOnMissingSubKey: false);
    }

    private static void RemoveMenuRootWhenEmpty()
    {
        using var shell = Registry.CurrentUser.OpenSubKey(SubMenuRoot);
        if (shell is not null && shell.GetSubKeyNames().Length > 0)
        {
            return;
        }

        Registry.CurrentUser.DeleteSubKeyTree(
            MenuRoot,
            throwOnMissingSubKey: false);
    }

    private static void RemoveLegacyEntries()
    {
        Registry.CurrentUser.DeleteSubKeyTree(
            LegacyMenuRoot,
            throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(
            BrokenSubMenuRoot,
            throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(
            LegacyVerbPrefix + "Rename",
            throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(
            LegacyVerbPrefix + "Unlock",
            throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(
            LegacyImageVerb,
            throwOnMissingSubKey: false);
    }

    private static ContextMenuDefinition GetDefinition(
        FileToolMode mode) =>
        mode switch
        {
            FileToolMode.Rename =>
                new("01Rename", "批量重命名", "rename"),
            FileToolMode.Unlock =>
                new("02Unlock", "解除文件占用", "unlock"),
            FileToolMode.ResizeImages =>
                new("03Resize", "调整图片尺寸", "resize"),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

    private sealed record ContextMenuDefinition(
        string KeyName,
        string Label,
        string CommandMode);
}

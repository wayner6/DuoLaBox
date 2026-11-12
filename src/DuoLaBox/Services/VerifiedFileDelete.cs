using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DuoLaBox.Services;

// 管理员权限下删除文件：扫描之后，路径中的任何目录都可能被换成联接点或符号链接。
// 先打开文件，再确认句柄指向的正是预期路径，最后通过同一个句柄删除，
// 这样校验和删除之间没有可以被替换的间隙。
internal static class VerifiedFileDelete
{
    internal static void Delete(string fullPath)
    {
        using var handle = NativeMethods.CreateFile(
            PathForms.ToDevicePath(fullPath),
            NativeMethods.AccessDelete |
            NativeMethods.FileReadAttributes |
            NativeMethods.FileWriteAttributes,
            NativeMethods.FileShareAll,
            nint.Zero,
            NativeMethods.OpenExisting,
            NativeMethods.FileFlagOpenReparsePoint,
            nint.Zero);
        if (handle.IsInvalid)
        {
            throw CreateException(Marshal.GetLastPInvokeError(), fullPath);
        }

        var attributes = File.GetAttributes(handle);
        if ((attributes &
             (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
        {
            throw new CleanupPathChangedException(fullPath);
        }

        var expectedPath = PathForms.TryGetLongPath(fullPath) ?? fullPath;
        if (!string.Equals(
                GetFinalPath(handle, fullPath),
                expectedPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new CleanupPathChangedException(fullPath);
        }

        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            // 属性值 0 表示“不修改”；只读是唯一属性时必须显式写入 Normal。
            var writable = attributes & ~FileAttributes.ReadOnly;
            File.SetAttributes(
                handle,
                writable == 0 ? FileAttributes.Normal : writable);
        }

        byte deleteFile = 1;
        if (!NativeMethods.SetFileInformationByHandle(
                handle,
                NativeMethods.FileDispositionInfoClass,
                ref deleteFile,
                sizeof(byte)))
        {
            throw CreateException(Marshal.GetLastPInvokeError(), fullPath);
        }
    }

    private static string GetFinalPath(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        string fullPath)
    {
        var buffer = new StringBuilder(Math.Max(520, fullPath.Length + 64));
        var length = NativeMethods.GetFinalPathNameByHandle(
            handle,
            buffer,
            (uint)buffer.Capacity,
            0);
        if (length > buffer.Capacity)
        {
            buffer = new StringBuilder((int)length);
            length = NativeMethods.GetFinalPathNameByHandle(
                handle,
                buffer,
                (uint)buffer.Capacity,
                0);
        }

        if (length == 0 || length > buffer.Capacity)
        {
            throw CreateException(Marshal.GetLastPInvokeError(), fullPath);
        }

        return PathForms.FromDevicePath(buffer.ToString());
    }

    // 与 File.Delete 抛出的异常保持同样的分类依据（类型和 HResult 低 16 位）。
    private static Exception CreateException(int error, string path)
    {
        var message = new System.ComponentModel.Win32Exception(error).Message;
        return error switch
        {
            5 => new UnauthorizedAccessException($"{message} {path}"),
            2 or 3 => new FileNotFoundException(message, path),
            _ => new IOException(
                $"{message} {path}",
                unchecked((int)0x80070000 | error))
        };
    }
}

internal sealed class CleanupPathChangedException(string path)
    : InvalidOperationException(
        $"路径经过了联接点或符号链接，已拒绝删除：{path}");

using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace DuoLaBox.Services;

public sealed class HostsFileService
{
    private const string StagingFolderName = "HostsStaging";
    private const int BackupLimit = 10;
    private static readonly string BackupPrefix =
        $"hosts.{AppIdentity.Name}备份-";
    private readonly string _stagingDirectory;

    static HostsFileService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public HostsFileService()
    {
        _stagingDirectory =
            AppIdentity.GetLocalDataPath(StagingFolderName);
    }

    public string HostsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers",
        "etc",
        "hosts");

    public bool IsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(
                    WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public HostsOperationResult Read()
    {
        try
        {
            var bytes = File.ReadAllBytes(HostsPath);
            return new HostsOperationResult(
                true,
                Decode(bytes, DetectEncoding(bytes)),
                "已读取当前 Hosts 文件。");
        }
        catch (Exception exception)
        {
            return new HostsOperationResult(
                false,
                string.Empty,
                $"读取失败：{exception.Message}");
        }
    }

    public StagedFile StageContent(string content)
    {
        Directory.CreateDirectory(_stagingDirectory);
        var path = Path.Combine(
            _stagingDirectory,
            $"hosts-{Guid.NewGuid():N}.txt");
        File.WriteAllText(
            path,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return StagedFile.Create(path);
    }

    public HostsOperationResult ApplyStagedContent(
        string stagingPath,
        string expectedHash)
    {
        try
        {
            var fullPath = Path.GetFullPath(stagingPath);
            if (!StagingLocation.IsTrusted(fullPath, StagingFolderName))
            {
                return new HostsOperationResult(
                    false,
                    string.Empty,
                    "拒绝了不受信任的临时文件路径。");
            }

            if (!IsAdministrator)
            {
                return new HostsOperationResult(
                    false,
                    string.Empty,
                    "保存 Hosts 文件需要管理员权限。");
            }

            var bytes = new StagedFile(fullPath, expectedHash).ReadVerifiedBytes();
            using var reader = new StreamReader(new MemoryStream(bytes));
            var content = reader.ReadToEnd();
            var result = WriteAsAdministrator(content);
            File.Delete(fullPath);
            return result;
        }
        catch (Exception exception)
        {
            return new HostsOperationResult(
                false,
                string.Empty,
                $"保存失败：{exception.Message}");
        }
    }

    public HostsOperationResult WriteAsAdministrator(string content)
    {
        if (!IsAdministrator)
        {
            return new HostsOperationResult(
                false,
                string.Empty,
                "保存 Hosts 文件需要管理员权限。");
        }

        var directory = Path.GetDirectoryName(HostsPath)!;
        // 精确到毫秒：一秒内连续保存两次时备份文件名不会相同。
        var timestamp = DateTime.Now.ToString(
            "yyyyMMdd-HHmmss-fff",
            CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(
            directory,
            BackupPrefix + timestamp);
        var temporaryPath = Path.Combine(
            directory,
            $"hosts.{AppIdentity.Name}临时-{Guid.NewGuid():N}");

        try
        {
            var existingBytes = File.ReadAllBytes(HostsPath);
            var existingAttributes = File.GetAttributes(HostsPath);
            File.Copy(HostsPath, backupPath, overwrite: false);
            File.WriteAllBytes(
                temporaryPath,
                Encode(content, DetectEncoding(existingBytes)));
            // 有些用户把 Hosts 设为只读来防止被篡改；替换前临时取消，之后恢复。
            var wasReadOnly =
                (existingAttributes & FileAttributes.ReadOnly) != 0;
            if (wasReadOnly)
            {
                File.SetAttributes(
                    HostsPath,
                    existingAttributes & ~FileAttributes.ReadOnly);
            }

            try
            {
                File.Move(temporaryPath, HostsPath, overwrite: true);
            }
            finally
            {
                if (wasReadOnly)
                {
                    File.SetAttributes(
                        HostsPath,
                        File.GetAttributes(HostsPath) | FileAttributes.ReadOnly);
                }
            }

            PruneBackups(directory);
            return new HostsOperationResult(
                true,
                content,
                $"保存成功，备份已创建：{Path.GetFileName(backupPath)}");
        }
        catch (Exception exception)
        {
            TryDelete(temporaryPath);
            return new HostsOperationResult(
                false,
                string.Empty,
                $"保存失败：{exception.Message}");
        }
    }

    public void CleanupStagingFile(string path) => TryDelete(path);

    // Windows 按系统默认代码页读取 Hosts；旧版记事本保存的中文注释就是 GBK 等本地编码。
    // 不是合法 UTF-8 的文件按本地编码读写，原有注释才不会变成乱码。
    internal static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 2 &&
            ((bytes[0] == 0xFF && bytes[1] == 0xFE) ||
             (bytes[0] == 0xFE && bytes[1] == 0xFF)))
        {
            return bytes[0] == 0xFF
                ? Encoding.Unicode
                : Encoding.BigEndianUnicode;
        }

        try
        {
            new UTF8Encoding(false, throwOnInvalidBytes: true)
                .GetCharCount(bytes);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(
                CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
    }

    internal static string Decode(byte[] bytes, Encoding encoding)
    {
        var preamble = encoding is UTF8Encoding
            ? Encoding.UTF8.GetPreamble()
            : encoding.GetPreamble();
        var offset = preamble.Length > 0 &&
                     bytes.AsSpan().StartsWith(preamble)
            ? preamble.Length
            : 0;
        return encoding.GetString(bytes, offset, bytes.Length - offset);
    }

    // 沿用原文件的编码；新内容里有该编码无法表示的字符时改用 UTF-8。不写入 BOM。
    internal static byte[] Encode(string content, Encoding existing)
    {
        if (existing is UTF8Encoding or UnicodeEncoding)
        {
            return new UTF8Encoding(false).GetBytes(content);
        }

        var strict = Encoding.GetEncoding(
            existing.CodePage,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
        try
        {
            return strict.GetBytes(content);
        }
        catch (EncoderFallbackException)
        {
            return new UTF8Encoding(false).GetBytes(content);
        }
    }

    private static void PruneBackups(string directory)
    {
        try
        {
            foreach (var backup in SelectBackupsToPrune(
                         Directory.EnumerateFiles(directory, BackupPrefix + "*")))
            {
                TryDelete(backup);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // 清理旧备份失败不影响已经完成的保存。
        }
    }

    // 文件名里的时间戳按字典序即时间顺序；保留最新的若干份。
    internal static IEnumerable<string> SelectBackupsToPrune(
        IEnumerable<string> backups) =>
        backups
            .OrderByDescending(
                path => Path.GetFileName(path),
                StringComparer.Ordinal)
            .Skip(BackupLimit);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 清理失败不覆盖原始错误。
        }
    }
}

public sealed record HostsOperationResult(
    bool Success,
    string Content,
    string Message);

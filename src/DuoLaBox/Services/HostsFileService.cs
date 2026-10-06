using System.IO;
using System.Security.Principal;
using System.Text;

namespace DuoLaBox.Services;

public sealed class HostsFileService
{
    private readonly string _stagingDirectory;

    public HostsFileService()
    {
        _stagingDirectory =
            AppIdentity.GetLocalDataPath("HostsStaging");
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
            return new HostsOperationResult(
                true,
                File.ReadAllText(HostsPath),
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
            var stagingRoot = Path.GetFullPath(_stagingDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(
                    stagingRoot,
                    StringComparison.OrdinalIgnoreCase))
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
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = Path.Combine(
            directory,
            $"hosts.{AppIdentity.Name}备份-{timestamp}");
        var temporaryPath = Path.Combine(
            directory,
            $"hosts.{AppIdentity.Name}临时-{Guid.NewGuid():N}");

        try
        {
            File.Copy(HostsPath, backupPath, overwrite: false);
            File.WriteAllText(
                temporaryPath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, HostsPath, overwrite: true);
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

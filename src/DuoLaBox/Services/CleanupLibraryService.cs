using System.IO;
using System.Reflection;
using System.Text;

namespace DuoLaBox.Services;

public sealed class CleanupLibraryService
{
    private const string CurrentVersionMarker =
        "# " + AppIdentity.Name + "CleanupLibraryVersion=2";
    private const string LegacyVersionMarker =
        "# DoraemonCleanupLibraryVersion=2";
    private const string ResourceName =
        AppIdentity.Name + ".Assets.DefaultCleanupRules.txt";

    private readonly string _customLibraryPath;
    private readonly string _previousCustomLibraryPath;
    private readonly string _legacyLibraryPath;

    static CleanupLibraryService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public CleanupLibraryService()
    {
        _customLibraryPath =
            AppIdentity.GetLocalDataPath("DefaultCleanupRules.rules");
        _previousCustomLibraryPath =
            AppIdentity.GetLocalDataPath("CleanupLibrary.csv");
        _legacyLibraryPath =
            AppIdentity.GetLegacyLocalDataPath("CleanupLibrary.csv");
    }

    public string CustomLibraryPath => _customLibraryPath;

    public string Load()
    {
        if (File.Exists(_customLibraryPath))
        {
            var current = ReadText(_customLibraryPath);
            if (HasCurrentVersion(current))
            {
                return current;
            }

            return MigrateLegacyLibrary(current);
        }

        var migrationPath = File.Exists(_previousCustomLibraryPath)
            ? _previousCustomLibraryPath
            : _legacyLibraryPath;
        if (File.Exists(migrationPath))
        {
            var legacy = ReadText(migrationPath);
            if (HasCurrentVersion(legacy))
            {
                Save(legacy);
                return EnsureCurrentVersion(legacy);
            }

            return MigrateLegacyLibrary(legacy);
        }

        return LoadDefault();
    }

    public string LoadDefault()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return "文件名";
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Decode(memory.ToArray());
    }

    public string Import(string path)
    {
        return ReadText(path);
    }

    public bool Save(string content)
    {
        try
        {
            AtomicFile.WriteAllText(
                _customLibraryPath,
                EnsureCurrentVersion(content),
                new UTF8Encoding(false));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadText(string path)
    {
        return Decode(File.ReadAllBytes(path));
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF)
        {
            return Normalize(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
        }

        try
        {
            var utf8 = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);
            return Normalize(utf8.GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            return Normalize(Encoding.GetEncoding(936).GetString(bytes));
        }
    }

    private static string Normalize(string content)
    {
        return content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd() + Environment.NewLine;
    }

    private string MigrateLegacyLibrary(string legacyContent)
    {
        var replacement = LoadDefault();
        try
        {
            var backupPath = _customLibraryPath + ".before-v2.bak";
            if (!File.Exists(backupPath))
            {
                AtomicFile.WriteAllText(
                    backupPath,
                    legacyContent,
                    new UTF8Encoding(false),
                    overwrite: false);
            }

            AtomicFile.WriteAllText(
                _customLibraryPath,
                replacement,
                new UTF8Encoding(false));
        }
        catch
        {
            // 即使旧文件无法迁移，也优先使用新版安全规则。
        }

        return replacement;
    }

    private static bool HasCurrentVersion(string content) =>
        content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line =>
                string.Equals(
                    line.Trim(),
                    CurrentVersionMarker,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    line.Trim(),
                    LegacyVersionMarker,
                    StringComparison.OrdinalIgnoreCase));

    private static string EnsureCurrentVersion(string content)
    {
        var normalized = Normalize(content).Replace(
            LegacyVersionMarker,
            CurrentVersionMarker,
            StringComparison.OrdinalIgnoreCase);
        return HasCurrentVersion(normalized)
            ? normalized
            : CurrentVersionMarker + Environment.NewLine + normalized;
    }
}


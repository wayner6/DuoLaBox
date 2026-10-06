using System.IO;
using System.Text;

namespace DuoLaBox.Services;

internal static class AtomicFile
{
    internal static void WriteAllText(
        string path,
        string content,
        Encoding? encoding = null,
        bool overwrite = true)
    {
        ArgumentNullException.ThrowIfNull(content);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".duolabox-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(
                           stream, encoding ?? new UTF8Encoding(false), leaveOpen: true))
                {
                    writer.Write(content);
                }
                stream.Flush(flushToDisk: true);
            }

            if (overwrite && File.Exists(fullPath))
            {
                File.Replace(temporaryPath, fullPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A temporary-file cleanup error must not hide the original save error.
            }
        }
    }
}

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DuoLaBox.Services;

internal static class CleanupRuleParser
{
    internal static List<ParsedCleanupRule> Parse(
        string library,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rules = new List<ParsedCleanupRule>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var category = "自定义规则";
        var description = "由清理库中的自定义路径匹配。";
        foreach (var rawLine in library.Split(['\r', '\n']))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = rawLine.Trim().Trim('"');
            if (line.StartsWith("# [", StringComparison.Ordinal) &&
                line.EndsWith(']'))
            {
                category = line[3..^1].Trim();
                description = "此项目属于清理库中的“" + category + "”。";
                continue;
            }

            const string descriptionPrefix = "# 说明：";
            if (line.StartsWith(
                    descriptionPrefix,
                    StringComparison.Ordinal))
            {
                description = line[descriptionPrefix.Length..].Trim();
                continue;
            }

            if (line.Length == 0 ||
                line.StartsWith('#') ||
                line.Equals("文件名", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("FileName", StringComparison.OrdinalIgnoreCase) ||
                !paths.Add(line))
            {
                continue;
            }

            rules.Add(new ParsedCleanupRule(
                line,
                category,
                description));
        }

        return rules;
    }

    // expand: false 用于提权进程。规则已由发起清理的用户展开，
    // 不能再按管理员账户的环境变量和用户目录解释一次。
    internal static bool TryPrepare(
        string rule,
        out PreparedCleanupRule prepared,
        bool expand = true)
    {
        prepared = default!;
        try
        {
            return TryPrepareCore(rule, expand, out prepared);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    internal static string Expand(string rule) => ExpandPlaceholders(rule);

    private static bool TryPrepareCore(string rule, bool expand, out PreparedCleanupRule prepared)
    {
        prepared = default!;
        // Reject invalid characters before Windows environment expansion can truncate at NUL.
        if (rule.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return false;
        }
        var expanded = (expand ? ExpandPlaceholders(rule) : rule).Replace('/', '\\');
        if (!Path.IsPathFullyQualified(expanded) ||
            expanded.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return false;
        }

        var wildcardIndex = expanded.IndexOfAny(['*', '?']);
        if (wildcardIndex < 0 &&
            (expanded.EndsWith('\\') || Directory.Exists(expanded)))
        {
            var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
            prepared = new PreparedCleanupRule(
                directory,
                new Regex(
                    "^" + Regex.Escape(directory.TrimEnd('\\') + "\\") + ".+$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                Recursive: true);
            return true;
        }

        if (wildcardIndex < 0)
        {
            var file = Path.GetFullPath(expanded);
            var directory = Path.GetDirectoryName(file);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            prepared = new PreparedCleanupRule(
                Path.TrimEndingDirectorySeparator(directory),
                new Regex(
                    "^" + Regex.Escape(file) + "$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                Recursive: false);
            return true;
        }

        var prefixSeparator = expanded.LastIndexOf('\\', wildcardIndex);
        if (prefixSeparator < 2)
        {
            return false;
        }

        var rawDirectoryPrefix = prefixSeparator == 2 && expanded[1] == ':'
            ? expanded[..3]
            : expanded[..prefixSeparator];
        var directoryPrefix = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(rawDirectoryPrefix));
        var normalizedPattern =
            directoryPrefix.TrimEnd('\\') + expanded[prefixSeparator..];
        var recursive = wildcardIndex < expanded.LastIndexOf('\\') ||
            expanded.Contains("**", StringComparison.Ordinal);

        prepared = new PreparedCleanupRule(
            directoryPrefix,
            CreateGlobRegex(normalizedPattern),
            recursive,
            CreateGlobLevels(expanded[(prefixSeparator + 1)..]));
        return true;
    }

    // 通配规则按路径逐段匹配目录，遍历时只进入可能命中的子目录。
    // 例如 User Data\*\Cache\Cache_Data\** 不必走遍每个浏览器配置文件的全部内容。
    private static GlobLevels CreateGlobLevels(string pattern)
    {
        var segments = pattern.Split('\\');
        var firstUnbounded = Array.FindIndex(
            segments,
            segment => segment.Contains("**", StringComparison.Ordinal));
        return new GlobLevels(
            [.. segments.Select(CreateGlobRegex)],
            firstUnbounded < 0 ? segments.Length : firstUnbounded);
    }

    // index：该子目录名在通配部分中的位置（规则目录的直接子目录为 0）。
    private static bool ShouldDescend(
        PreparedCleanupRule rule,
        string name,
        int index)
    {
        if (rule.Levels is not { } levels)
        {
            return rule.Recursive;
        }

        if (index >= levels.FirstUnbounded)
        {
            return true;
        }

        return index < levels.Segments.Length - 1 &&
               levels.Segments[index].IsMatch(name);
    }

    // depth：所在目录相对规则目录的深度。** 之前的每一段对应固定的一层，文件只可能出现在最后一段。
    private static bool FilesCanMatch(PreparedCleanupRule rule, int depth) =>
        rule.Levels is not { } levels ||
        depth >= levels.FirstUnbounded ||
        depth == levels.Segments.Length - 1;

    internal static IEnumerable<string> EnumerateFiles(
        PreparedCleanupRule rule,
        CancellationToken cancellationToken,
        Stopwatch stopwatch,
        TimeSpan timeLimit)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        var directories = new Stack<(string Path, int Depth)>();
        directories.Push((rule.Directory, 0));
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed >= timeLimit)
            {
                yield break;
            }

            var (current, depth) = directories.Pop();
            var filesCanMatch = FilesCanMatch(rule, depth);
            IEnumerator<FileSystemInfo> entries;
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }
                // 属性随目录项一起返回，不必为每个文件再查询一次。
                entries = new DirectoryInfo(current)
                    .EnumerateFileSystemInfos("*", options)
                    .GetEnumerator();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            using (entries)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (stopwatch.Elapsed >= timeLimit)
                    {
                        yield break;
                    }

                    bool hasNext;
                    try
                    {
                        hasNext = entries.MoveNext();
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        break;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (stopwatch.Elapsed >= timeLimit || !hasNext)
                    {
                        break;
                    }

                    var entry = entries.Current;
                    var attributes = entry.Attributes;
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }
                    if (attributes.HasFlag(FileAttributes.Directory))
                    {
                        if (ShouldDescend(rule, entry.Name, depth))
                        {
                            directories.Push((entry.FullName, depth + 1));
                        }
                    }
                    else if (filesCanMatch)
                    {
                        yield return entry.FullName;
                    }
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal static bool IsFileAllowed(
        string file,
        PreparedCleanupRule rule)
    {
        var directoryPrefix = rule.Directory.TrimEnd('\\') + "\\";
        return file.StartsWith(
                directoryPrefix,
                StringComparison.OrdinalIgnoreCase) &&
            rule.PathRegex.IsMatch(file);
    }

    private static Regex CreateGlobRegex(string pattern)
    {
        var expression = new StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            if (character == '*')
            {
                if (index + 1 < pattern.Length && pattern[index + 1] == '*')
                {
                    expression.Append(".*");
                    index++;
                }
                else
                {
                    expression.Append("[^\\\\]*");
                }

                continue;
            }

            if (character == '?')
            {
                expression.Append("[^\\\\]");
                continue;
            }

            expression.Append(Regex.Escape(character.ToString()));
        }

        expression.Append('$');
        return new Regex(
            expression.ToString(),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string ExpandPlaceholders(string rule)
    {
        var profile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(
            Environment.SpecialFolder.MyDocuments);
        return Environment.ExpandEnvironmentVariables(rule)
            .Replace(
                "用户名",
                profile.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "我的文档",
                documents.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record PreparedCleanupRule(
    string Directory,
    Regex PathRegex,
    bool Recursive,
    GlobLevels? Levels = null);

internal sealed record GlobLevels(Regex[] Segments, int FirstUnbounded);

internal sealed record ParsedCleanupRule(
    string Path,
    string Category,
    string Description);

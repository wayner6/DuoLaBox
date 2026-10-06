using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DuoLaBox.Services;

public sealed partial class SystemInformationService
{
    private sealed class WmiReader : IDisposable
    {
        private object? _locator;
        private object? _service;

        public WmiReader(string scope = @"root\cimv2")
        {
            try
            {
                var locatorType = Type.GetTypeFromProgID(
                    "WbemScripting.SWbemLocator");
                if (locatorType is null)
                {
                    return;
                }

                _locator = Activator.CreateInstance(locatorType);
                if (_locator is null)
                {
                    return;
                }

                _service = locatorType.InvokeMember(
                    "ConnectServer",
                    BindingFlags.InvokeMethod,
                    null,
                    _locator,
                    [".", scope]);
            }
            catch
            {
                Dispose();
            }
        }

        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(
            string query)
        {
            var values = new List<IReadOnlyDictionary<string, object?>>();
            if (_service is null)
            {
                return values;
            }

            object? result = null;
            try
            {
                result = _service.GetType().InvokeMember(
                    "ExecQuery",
                    BindingFlags.InvokeMethod,
                    null,
                    _service,
                    [query]);
                if (result is not System.Collections.IEnumerable enumerable)
                {
                    return values;
                }

                foreach (var item in enumerable)
                {
                    if (item is null)
                    {
                        continue;
                    }

                    try
                    {
                        var properties = item.GetType().InvokeMember(
                            "Properties_",
                            BindingFlags.GetProperty,
                            null,
                            item,
                            null);
                        if (properties is not System.Collections.IEnumerable propertySet)
                        {
                            continue;
                        }

                        var row = new Dictionary<string, object?>(
                            StringComparer.OrdinalIgnoreCase);
                        foreach (var property in propertySet)
                        {
                            if (property is null)
                            {
                                continue;
                            }

                            var type = property.GetType();
                            var name = Convert.ToString(type.InvokeMember(
                                "Name",
                                BindingFlags.GetProperty,
                                null,
                                property,
                                null), CultureInfo.InvariantCulture);
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                row[name] = type.InvokeMember(
                                    "Value",
                                    BindingFlags.GetProperty,
                                    null,
                                    property,
                                    null);
                            }

                            ReleaseComObject(property);
                        }

                        ReleaseComObject(properties);
                        values.Add(row);
                    }
                    catch
                    {
                        // A single WMI device must not make the whole page fail.
                    }
                    finally
                    {
                        ReleaseComObject(item);
                    }
                }
            }
            catch
            {
                // Registry and managed API fallbacks still provide useful data.
            }
            finally
            {
                ReleaseComObject(result);
            }

            return values;
        }

        public IReadOnlyDictionary<string, object?>? QueryFirst(
            string query) => Query(query).FirstOrDefault();

        public void Dispose()
        {
            ReleaseComObject(_service);
            ReleaseComObject(_locator);
            _service = null;
            _locator = null;
        }
    }

    private static string? GetValue(
        IReadOnlyDictionary<string, object?>? values,
        string key)
    {
        if (values is null ||
            !values.TryGetValue(key, out var value) ||
            value is null)
        {
            return null;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
    }

    private static ulong GetUInt64(
        IReadOnlyDictionary<string, object?>? values,
        string key)
    {
        if (values is null ||
            !values.TryGetValue(key, out var value) ||
            value is null)
        {
            return 0;
        }

        try
        {
            return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    private static string? GetCharacterArrayValue(
        IReadOnlyDictionary<string, object?> values,
        string key)
    {
        if (!values.TryGetValue(key, out var rawValue) ||
            rawValue is not Array characters)
        {
            return null;
        }

        var result = new List<char>();
        foreach (var value in characters)
        {
            try
            {
                var code = Convert.ToUInt16(
                    value,
                    CultureInfo.InvariantCulture);
                if (code == 0)
                {
                    break;
                }

                result.Add((char)code);
            }
            catch
            {
                return null;
            }
        }

        return result.Count == 0
            ? null
            : new string(result.ToArray()).Trim();
    }

    private static string? ReadRegistryString(
        RegistryKey root,
        string path,
        string name)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            return Convert.ToString(
                key?.GetValue(name),
                CultureInfo.InvariantCulture)?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, ulong> ReadGraphicsMemory()
    {
        var values = new Dictionary<string, ulong>(
            StringComparer.OrdinalIgnoreCase);
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Video");
            if (root is null)
            {
                return values;
            }

            foreach (var adapter in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey($@"{adapter}\0000");
                var name = Convert.ToString(
                    key?.GetValue("DriverDesc"),
                    CultureInfo.InvariantCulture)?.Trim();
                var rawMemory = key?.GetValue(
                    "HardwareInformation.qwMemorySize");
                if (!string.IsNullOrWhiteSpace(name) && rawMemory is not null)
                {
                    values[name] = Convert.ToUInt64(
                        rawMemory,
                        CultureInfo.InvariantCulture);
                }
            }
        }
        catch
        {
            // Some display drivers do not expose memory in this registry key.
        }

        return values;
    }

    private static IReadOnlyList<string> ReadGraphicsNamesFromRegistry()
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Video");
            if (root is null)
            {
                return ["暂未读取到显卡信息"];
            }

            return root.GetSubKeyNames()
                .Select(adapter =>
                {
                    using var key = root.OpenSubKey($@"{adapter}\0000");
                    return Convert.ToString(
                        key?.GetValue("DriverDesc"),
                        CultureInfo.InvariantCulture)?.Trim();
                })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .DefaultIfEmpty("暂未读取到显卡信息")
                .ToArray();
        }
        catch
        {
            return ["暂未读取到显卡信息"];
        }
    }


}

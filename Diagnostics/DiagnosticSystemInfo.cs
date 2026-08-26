using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Runs;

namespace BetterMultiplayer.Diagnostics;

internal sealed record DiagnosticModInfo(string Id, string Version)
{
    internal DiagnosticModInfo? Normalize()
    {
        string id = FeedbackValueSanitizer.Identifier(Id);
        if (id == "unavailable")
            return null;
        return new DiagnosticModInfo(id, DiagnosticSystemInfo.SafeVersion(Version));
    }
}

internal sealed record DiagnosticSystemInfo(
    string ModVersion,
    string ModBuild,
    string ModSha256,
    string GameBuild,
    string BaseLibVersion,
    string DotNetVersion,
    string OperatingSystem,
    string ProcessArchitecture,
    string Language,
    int WindowWidth,
    int WindowHeight,
    int ViewportWidth,
    int ViewportHeight,
    IReadOnlyList<DiagnosticModInfo>? LoadedMods = null,
    bool LoadedModsTruncated = false)
{
    internal const int MaxLoadedMods = 64;

    private static readonly Regex VersionPattern = new(
        @"^[vV]?\d+(?:\.\d+){1,3}(?:[-+][0-9A-Za-z][0-9A-Za-z._-]{0,63})?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex WindowsPattern = new(
        @"^Windows-\d+\.\d+\.\d+$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Lazy<string> ModHash = new(ComputeModHash);

    internal static DiagnosticSystemInfo Capture(Control? source)
    {
        Assembly modAssembly = typeof(BetterMultiplayerMod).Assembly;
        Assembly gameAssembly = typeof(RunManager).Assembly;
        Vector2I windowSize = SafeWindowSize();
        Vector2 viewportSize = SafeViewportSize(source);
        LoadedModSnapshot loadedMods = CaptureLoadedMods();

        return new DiagnosticSystemInfo(
            SafeVersion(BetterMultiplayerMod.Version),
            ProductVersion(modAssembly),
            ModHash.Value,
            ProductVersion(gameAssembly),
            LoadedAssemblyVersion("BaseLib"),
            SafeVersion(System.Environment.Version.ToString()),
            WindowsVersion(),
            SafeToken(RuntimeInformation.ProcessArchitecture.ToString()),
            SafeLanguage(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName),
            Math.Clamp(windowSize.X, 0, 32768),
            Math.Clamp(windowSize.Y, 0, 32768),
            Math.Clamp((int)MathF.Round(viewportSize.X), 0, 32768),
            Math.Clamp((int)MathF.Round(viewportSize.Y), 0, 32768),
            loadedMods.Mods,
            loadedMods.Truncated);
    }

    internal static string SafeVersion(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        if (candidate.Length is 0 or > 96 || !VersionPattern.IsMatch(candidate))
            return "unknown";
        return candidate;
    }

    internal DiagnosticSystemInfo Normalize()
    {
        DiagnosticModInfo[] loadedMods = (LoadedMods ?? [])
            .Select(mod => mod.Normalize())
            .Where(mod => mod is not null)
            .Cast<DiagnosticModInfo>()
            .GroupBy(mod => mod.Id, StringComparer.Ordinal)
            .Select(group => group.Last())
            .OrderBy(mod => mod.Id, StringComparer.Ordinal)
            .Take(MaxLoadedMods)
            .ToArray();
        return new DiagnosticSystemInfo(
            SafeVersion(ModVersion),
            SafeVersion(ModBuild),
            SafeHash(ModSha256),
            SafeVersion(GameBuild),
            SafeVersion(BaseLibVersion),
            SafeVersion(DotNetVersion),
            OperatingSystem == "other" || WindowsPattern.IsMatch(OperatingSystem)
                ? OperatingSystem
                : "unknown",
            SafeArchitecture(ProcessArchitecture),
            SafeLanguage(Language),
            Math.Clamp(WindowWidth, 0, 32768),
            Math.Clamp(WindowHeight, 0, 32768),
            Math.Clamp(ViewportWidth, 0, 32768),
            Math.Clamp(ViewportHeight, 0, 32768),
            loadedMods,
            LoadedModsTruncated || (LoadedMods?.Count ?? 0) > MaxLoadedMods);
    }

    private static LoadedModSnapshot CaptureLoadedMods()
    {
        try
        {
            const BindingFlags staticFlags = BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic;
            Type? manager = typeof(RunManager).Assembly.GetType(
                "MegaCrit.Sts2.Core.Modding.ModManager");
            object? value = manager?.GetProperty("Mods", staticFlags)?.GetValue(null) ??
                manager?.GetMethods(staticFlags)
                    .FirstOrDefault(method =>
                        method.Name == "GetLoadedMods" &&
                        method.GetParameters().Length == 0)
                    ?.Invoke(null, null) ??
                manager?.GetField("_mods", staticFlags)?.GetValue(null);
            if (value is not IEnumerable mods)
                return LoadedModSnapshot.Empty;

            List<DiagnosticModInfo> loaded = [];
            foreach (object? mod in mods)
            {
                if (mod is null || !string.Equals(
                        ReadMember(mod, "state")?.ToString(),
                        "Loaded",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                object? manifest = ReadMember(mod, "manifest");
                string id = ReadMember(manifest, "id")?.ToString() ?? string.Empty;
                string version = ReadMember(mod, "version")?.ToString() ??
                    ReadMember(manifest, "version")?.ToString() ?? string.Empty;
                DiagnosticModInfo? normalized = new DiagnosticModInfo(id, version).Normalize();
                if (normalized is not null)
                    loaded.Add(normalized);
            }

            DiagnosticModInfo[] unique = loaded
                .GroupBy(mod => mod.Id, StringComparer.Ordinal)
                .Select(group => group.Last())
                .OrderBy(mod => mod.Id, StringComparer.Ordinal)
                .ToArray();
            return new LoadedModSnapshot(
                unique.Take(MaxLoadedMods).ToArray(),
                unique.Length > MaxLoadedMods);
        }
        catch
        {
            return LoadedModSnapshot.Empty;
        }
    }

    private static object? ReadMember(object? target, string name)
    {
        if (target is null)
            return null;
        try
        {
            const BindingFlags flags = BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic;
            return target.GetType().GetProperty(name, flags)?.GetValue(target) ??
                target.GetType().GetField(name, flags)?.GetValue(target);
        }
        catch
        {
            return null;
        }
    }

    private static string ProductVersion(Assembly assembly)
    {
        try
        {
            string? location = assembly.Location;
            string? productVersion = string.IsNullOrEmpty(location)
                ? null
                : FileVersionInfo.GetVersionInfo(location).ProductVersion;
            return SafeVersion(productVersion ?? assembly.GetName().Version?.ToString());
        }
        catch
        {
            return SafeVersion(assembly.GetName().Version?.ToString());
        }
    }

    private static string LoadedAssemblyVersion(string assemblyName)
    {
        try
        {
            Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.GetName().Name,
                    assemblyName,
                    StringComparison.Ordinal));
            return SafeVersion(assembly?.GetName().Version?.ToString());
        }
        catch
        {
            return "unknown";
        }
    }

    private static string ComputeModHash()
    {
        try
        {
            string location = typeof(BetterMultiplayerMod).Assembly.Location;
            if (string.IsNullOrEmpty(location))
                return "unavailable";
            using FileStream stream = File.OpenRead(location);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch
        {
            return "unavailable";
        }
    }

    private static Vector2I SafeWindowSize()
    {
        try
        {
            return DisplayServer.WindowGetSize();
        }
        catch
        {
            return Vector2I.Zero;
        }
    }

    private static Vector2 SafeViewportSize(Control? source)
    {
        try
        {
            return source?.GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        }
        catch
        {
            return Vector2.Zero;
        }
    }

    private static string WindowsVersion()
    {
        if (!System.OperatingSystem.IsWindows())
            return "other";

        Version version = System.Environment.OSVersion.Version;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Windows-{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}");
    }

    private static string SafeToken(string value)
    {
        Span<char> buffer = stackalloc char[Math.Min(value.Length, 32)];
        int length = 0;
        foreach (char c in value)
        {
            if (length >= buffer.Length)
                break;
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
                buffer[length++] = c;
        }
        return length == 0 ? "unknown" : new string(buffer[..length]);
    }

    private static string SafeLanguage(string value) =>
        value.Length == 2 && value.All(char.IsAsciiLetter)
            ? value.ToLowerInvariant()
            : "unknown";

    private static string SafeHash(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit)
            ? value.ToUpperInvariant()
            : "unavailable";

    private static string SafeArchitecture(string value) => value switch
    {
        "X64" or "X86" or "Arm" or "Arm64" or "Wasm" or "S390x" or "LoongArch64" => value,
        _ => "unknown"
    };

    private sealed record LoadedModSnapshot(
        IReadOnlyList<DiagnosticModInfo> Mods,
        bool Truncated)
    {
        internal static LoadedModSnapshot Empty { get; } = new([], false);
    }
}

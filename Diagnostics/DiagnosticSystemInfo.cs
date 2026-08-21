using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Runs;

namespace BetterMultiplayer.Diagnostics;

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
    int ViewportHeight)
{
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
            Math.Clamp((int)MathF.Round(viewportSize.Y), 0, 32768));
    }

    internal static string SafeVersion(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        if (candidate.Length is 0 or > 96 || !VersionPattern.IsMatch(candidate))
            return "unknown";
        return candidate;
    }

    internal DiagnosticSystemInfo Normalize() => new(
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
        Math.Clamp(ViewportHeight, 0, 32768));

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
}

using System.Text.Json;
using BepInEx;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;
using Microsoft.Win32;

namespace MortuaryAssistantVR.XR;

internal static class OpenXrRuntimeProbe
{
    private const string OpenXrRegistryPath =
        @"SOFTWARE\Khronos\OpenXR\1";

    [HideFromIl2Cpp]
    internal static void LogStatus(ManualLogSource logger)
    {
        logger.LogInfo("=== OpenXR Runtime Probe begin ===");

        try
        {
            var environmentOverride =
                Environment.GetEnvironmentVariable("XR_RUNTIME_JSON");

            if (!string.IsNullOrWhiteSpace(environmentOverride))
            {
                logger.LogInfo(
                    $"[OpenXR] XR_RUNTIME_JSON override: " +
                    $"'{environmentOverride}'");
            }
            else
            {
                logger.LogInfo(
                    "[OpenXR] XR_RUNTIME_JSON override is not set.");
            }

            var manifestPath =
                !string.IsNullOrWhiteSpace(environmentOverride)
                    ? environmentOverride
                    : ReadActiveRuntimeFromRegistry(logger);

            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                logger.LogWarning(
                    "[OpenXR] No active runtime manifest was detected.");
            }
            else
            {
                LogManifest(logger, manifestPath);
            }

            LogLoaderCandidates(logger);
        }
        catch (Exception exception)
        {
            logger.LogError(
                $"OpenXR Runtime Probe failed: {exception}");
        }
        finally
        {
            logger.LogInfo("=== OpenXR Runtime Probe end ===");
        }
    }

    [HideFromIl2Cpp]
    private static string? ReadActiveRuntimeFromRegistry(
        ManualLogSource logger)
    {
        var views = Environment.Is64BitProcess
            ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
            : new[] { RegistryView.Registry32 };

        foreach (var view in views)
        {
            foreach (var hive in new[]
                     {
                         RegistryHive.CurrentUser,
                         RegistryHive.LocalMachine
                     })
            {
                try
                {
                    using var baseKey =
                        RegistryKey.OpenBaseKey(hive, view);
                    using var key =
                        baseKey.OpenSubKey(OpenXrRegistryPath);

                    var value =
                        key?.GetValue("ActiveRuntime") as string;

                    logger.LogInfo(
                        $"[OpenXR] Registry probe: hive={hive}, " +
                        $"view={view}, found={!string.IsNullOrWhiteSpace(value)}");

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        logger.LogInfo(
                            $"[OpenXR] Active runtime manifest: '{value}'");
                        return value;
                    }
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        $"[OpenXR] Registry probe failed for " +
                        $"{hive}/{view}: {exception.Message}");
                }
            }
        }

        return null;
    }

    [HideFromIl2Cpp]
    private static void LogManifest(
        ManualLogSource logger,
        string manifestPath)
    {
        var fullManifestPath =
            Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(manifestPath));

        logger.LogInfo(
            $"[OpenXR] Runtime manifest exists: " +
            $"{File.Exists(fullManifestPath)}");

        if (!File.Exists(fullManifestPath))
        {
            logger.LogWarning(
                $"[OpenXR] Runtime manifest does not exist: " +
                $"'{fullManifestPath}'");
            return;
        }

        try
        {
            using var document =
                JsonDocument.Parse(File.ReadAllText(fullManifestPath));

            var root = document.RootElement;

            if (root.TryGetProperty(
                    "file_format_version",
                    out var formatVersion))
            {
                logger.LogInfo(
                    $"[OpenXR] Manifest format: " +
                    $"'{formatVersion.GetString()}'");
            }

            if (!root.TryGetProperty("runtime", out var runtime))
            {
                logger.LogWarning(
                    "[OpenXR] Manifest has no 'runtime' object.");
                return;
            }

            if (runtime.TryGetProperty("name", out var runtimeName))
            {
                logger.LogInfo(
                    $"[OpenXR] Runtime name: " +
                    $"'{runtimeName.GetString()}'");
            }

            if (!runtime.TryGetProperty(
                    "library_path",
                    out var libraryPathElement))
            {
                logger.LogWarning(
                    "[OpenXR] Manifest has no runtime library_path.");
                return;
            }

            var libraryPath =
                libraryPathElement.GetString();

            if (string.IsNullOrWhiteSpace(libraryPath))
            {
                logger.LogWarning(
                    "[OpenXR] Runtime library_path is empty.");
                return;
            }

            var manifestDirectory =
                Path.GetDirectoryName(fullManifestPath)
                ?? string.Empty;

            var resolvedLibraryPath =
                Path.IsPathRooted(libraryPath)
                    ? libraryPath
                    : Path.GetFullPath(
                        Path.Combine(
                            manifestDirectory,
                            libraryPath));

            logger.LogInfo(
                $"[OpenXR] Runtime library: " +
                $"'{resolvedLibraryPath}'");

            logger.LogInfo(
                $"[OpenXR] Runtime library exists: " +
                $"{File.Exists(resolvedLibraryPath)}");
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                $"[OpenXR] Could not parse runtime manifest: " +
                $"{exception.Message}");
        }
    }

    [HideFromIl2Cpp]
    private static void LogLoaderCandidates(
        ManualLogSource logger)
    {
        var candidates = new[]
        {
            Path.Combine(
                Paths.GameRootPath,
                "openxr_loader.dll"),
            Path.Combine(
                Paths.BepInExRootPath,
                "core",
                "openxr_loader.dll"),
            Path.Combine(
                Paths.PluginPath,
                "MortuaryAssistantVR",
                "openxr_loader.dll"),
            Path.Combine(
                AppContext.BaseDirectory,
                "openxr_loader.dll")
        }
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        var foundAny = false;

        for (var index = 0; index < candidates.Length; index++)
        {
            var exists = File.Exists(candidates[index]);
            foundAny |= exists;

            logger.LogInfo(
                $"[OpenXR] Loader candidate[{index}]: " +
                $"exists={exists}, path='{candidates[index]}'");
        }

        if (!foundAny)
        {
            logger.LogWarning(
                "[OpenXR] No application-local openxr_loader.dll was found. " +
                "v0.5 will not attempt to start OpenXR.");
        }
    }
}

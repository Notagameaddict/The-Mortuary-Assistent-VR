using BepInEx.Configuration;

namespace MortuaryAssistantVR.Config;

internal static class ModConfig
{
    internal static ConfigEntry<bool> EnableDiagnostics { get; private set; } = null!;
    internal static ConfigEntry<bool> LogCamerasOnSceneLoad { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableSceneExplorer { get; private set; } = null!;
    internal static ConfigEntry<int> SceneExplorerMaxDepth { get; private set; } = null!;
    internal static ConfigEntry<int> SceneExplorerMaxObjects { get; private set; } = null!;
    internal static ConfigEntry<bool> LogComponents { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableCameraProbe { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableRenderPipelineProbe { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableXrRigBootstrap { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableOpenXrRuntimeProbe { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableXrBackend { get; private set; } = null!;
    internal static ConfigEntry<bool> AttemptXrStartup { get; private set; } = null!;

    internal static void Bind(ConfigFile config)
    {
        EnableDiagnostics = config.Bind(
            "Diagnostics", "Enabled", true,
            "Enables diagnostic logging.");

        LogCamerasOnSceneLoad = config.Bind(
            "Diagnostics", "LogCamerasOnSceneLoad", true,
            "Logs active cameras after a scene loads.");

        EnableSceneExplorer = config.Bind(
            "SceneExplorer", "Enabled", false,
            "Logs the full scene hierarchy.");

        SceneExplorerMaxDepth = config.Bind(
            "SceneExplorer", "MaxDepth", 8,
            "Maximum hierarchy depth written to the log.");

        SceneExplorerMaxObjects = config.Bind(
            "SceneExplorer", "MaxObjects", 1500,
            "Maximum number of GameObjects written per scene.");

        LogComponents = config.Bind(
            "SceneExplorer", "LogComponents", true,
            "Logs component type names attached to each GameObject.");

        EnableCameraProbe = config.Bind(
            "CameraProbe", "Enabled", false,
            "Logs the gameplay camera hierarchy.");

        EnableRenderPipelineProbe = config.Bind(
            "RenderPipelineProbe", "Enabled", false,
            "Logs render-pipeline and camera settings.");

        EnableXrRigBootstrap = config.Bind(
            "XR", "EnableRigBootstrap", true,
            "Creates the XR origin/head hierarchy around the gameplay camera.");

        EnableOpenXrRuntimeProbe = config.Bind(
            "XR", "EnableRuntimeProbe", true,
            "Detects the active Windows OpenXR runtime.");

        EnableXrBackend = config.Bind(
            "XR", "EnableBackend", true,
            "Initializes the native XR backend foundation.");

        AttemptXrStartup = config.Bind(
            "XR", "AttemptStartup", false,
            "Attempts OpenXR instance creation. Keep false in v0.7.");
    }
}

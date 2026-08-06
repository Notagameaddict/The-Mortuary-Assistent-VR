using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MortuaryAssistantVR.Diagnostics;

internal static class RuntimeDiagnostics
{
    internal static void LogEnvironment(ManualLogSource logger)
    {
        logger.LogInfo($"Unity version: {Application.unityVersion}");
        logger.LogInfo(
            $"Product: {Application.productName} {Application.version}");
        logger.LogInfo($"Platform: {Application.platform}");
        logger.LogInfo($"Graphics API: {SystemInfo.graphicsDeviceType}");
        logger.LogInfo($"Graphics device: {SystemInfo.graphicsDeviceName}");
    }

    internal static void LogSceneSummary(
        ManualLogSource logger,
        Scene scene)
    {
        logger.LogInfo(
            $"Scene summary: name='{scene.name}', " +
            $"buildIndex={scene.buildIndex}, " +
            $"rootCount={scene.rootCount}, " +
            $"loaded={scene.isLoaded}.");
    }

    internal static void LogCameras(ManualLogSource logger)
    {
        var cameras = Camera.allCameras;

        logger.LogInfo($"Active cameras: {cameras.Length}");

        for (var index = 0; index < cameras.Length; index++)
        {
            var camera = cameras[index];

            if (camera is null)
            {
                continue;
            }

            logger.LogInfo(
                $"Camera[{index}]: " +
                $"name='{camera.name}', " +
                $"enabled={camera.enabled}, " +
                $"depth={camera.depth}, " +
                $"fieldOfView={camera.fieldOfView:0.##}, " +
                $"targetDisplay={camera.targetDisplay}, " +
                $"stereoTargetEye={camera.stereoTargetEye}, " +
                $"path='{SceneExplorer.GetTransformPath(camera.transform)}'.");
        }
    }

    internal static void LogXrStatus(ManualLogSource logger)
    {
        logger.LogInfo(
            "Unity XR device enumeration is disabled in bootstrap v0.2.");
    }
}

using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace MortuaryAssistantVR.Diagnostics;

internal static class CameraProbe
{
    [HideFromIl2Cpp]
    internal static void LogGameplayCamera(
        ManualLogSource logger,
        string sceneName)
    {
        logger.LogInfo(
            $"=== Camera Probe begin: scene='{sceneName}' ===");

        try
        {
            var cameras = Camera.allCameras;
            Camera? gameplayCamera = null;

            for (var index = 0; index < cameras.Length; index++)
            {
                var candidate = cameras[index];

                if (candidate is null || !candidate.enabled)
                {
                    continue;
                }

                var path = SceneExplorer.GetTransformPath(candidate.transform);

                if (path == "Player/CamHelper/Camera")
                {
                    gameplayCamera = candidate;
                    break;
                }

                if (gameplayCamera is null &&
                    candidate.name == "Camera" &&
                    path.Contains("Player", StringComparison.OrdinalIgnoreCase))
                {
                    gameplayCamera = candidate;
                }
            }

            if (gameplayCamera is null)
            {
                logger.LogWarning(
                    "Camera Probe could not find a gameplay camera under Player.");
                LogAllActiveCameras(logger, cameras);
                return;
            }

            LogCamera(logger, gameplayCamera);
            LogParentChain(logger, gameplayCamera.transform);
        }
        catch (Exception exception)
        {
            logger.LogError($"Camera Probe failed: {exception}");
        }
        finally
        {
            logger.LogInfo(
                $"=== Camera Probe end: scene='{sceneName}' ===");
        }
    }

    [HideFromIl2Cpp]
    private static void LogCamera(
        ManualLogSource logger,
        Camera camera)
    {
        var transform = camera.transform;

        logger.LogInfo(
            $"[CameraProbe] Gameplay camera path: " +
            $"'{SceneExplorer.GetTransformPath(transform)}'");

        logger.LogInfo(
            $"[CameraProbe] Camera properties: " +
            $"enabled={camera.enabled}, " +
            $"depth={camera.depth}, " +
            $"fieldOfView={camera.fieldOfView:0.###}, " +
            $"nearClip={camera.nearClipPlane:0.####}, " +
            $"farClip={camera.farClipPlane:0.###}, " +
            $"orthographic={camera.orthographic}, " +
            $"stereoTargetEye={camera.stereoTargetEye}");

        logger.LogInfo(
            $"[CameraProbe] World pose: " +
            $"position={FormatVector(transform.position)}, " +
            $"rotation={FormatVector(transform.eulerAngles)}");

        logger.LogInfo(
            $"[CameraProbe] Local pose: " +
            $"position={FormatVector(transform.localPosition)}, " +
            $"rotation={FormatVector(transform.localEulerAngles)}, " +
            $"scale={FormatVector(transform.localScale)}");

        LogComponents(logger, camera.gameObject, "Camera");
    }

    [HideFromIl2Cpp]
    private static void LogParentChain(
        ManualLogSource logger,
        Transform cameraTransform)
    {
        var current = cameraTransform;
        var level = 0;

        while (current is not null && level < 10)
        {
            logger.LogInfo(
                $"[CameraProbe] Parent[{level}]: " +
                $"name='{current.name}', " +
                $"path='{SceneExplorer.GetTransformPath(current)}', " +
                $"localPosition={FormatVector(current.localPosition)}, " +
                $"localRotation={FormatVector(current.localEulerAngles)}");

            LogComponents(logger, current.gameObject, $"Parent[{level}]");

            current = current.parent;
            level++;
        }
    }

    [HideFromIl2Cpp]
    private static void LogComponents(
        ManualLogSource logger,
        GameObject gameObject,
        string label)
    {
        try
        {
            var components = gameObject.GetComponents<Component>();

            for (var index = 0; index < components.Length; index++)
            {
                var component = components[index];

                if (component is null)
                {
                    logger.LogInfo(
                        $"[CameraProbe] {label} component[{index}]: <missing>");
                    continue;
                }

                var typeName = component.GetIl2CppType()?.FullName
                    ?? component.GetType().FullName
                    ?? component.GetType().Name;

                logger.LogInfo(
                    $"[CameraProbe] {label} component[{index}]: {typeName}");
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                $"[CameraProbe] Failed to enumerate {label} components: " +
                $"{exception.Message}");
        }
    }

    [HideFromIl2Cpp]
    private static void LogAllActiveCameras(
        ManualLogSource logger,
        Camera[] cameras)
    {
        for (var index = 0; index < cameras.Length; index++)
        {
            var camera = cameras[index];

            if (camera is null)
            {
                continue;
            }

            logger.LogInfo(
                $"[CameraProbe] Candidate[{index}]: " +
                $"name='{camera.name}', enabled={camera.enabled}, " +
                $"path='{SceneExplorer.GetTransformPath(camera.transform)}'");
        }
    }

    [HideFromIl2Cpp]
    private static string FormatVector(Vector3 value)
    {
        return
            $"({value.x:0.####}, {value.y:0.####}, {value.z:0.####})";
    }
}

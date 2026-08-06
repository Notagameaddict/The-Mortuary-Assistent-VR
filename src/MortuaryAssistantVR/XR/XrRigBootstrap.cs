using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;
using MortuaryAssistantVR.Diagnostics;
using UnityEngine;

namespace MortuaryAssistantVR.XR;

internal static class XrRigBootstrap
{
    private const string OriginName = "MortuaryAssistantVR_XROrigin";
    private const string HeadName = "MortuaryAssistantVR_Head";

    [HideFromIl2Cpp]
    internal static bool TryCreate(
        ManualLogSource logger,
        string sceneName)
    {
        logger.LogInfo(
            $"=== XR Rig Bootstrap begin: scene='{sceneName}' ===");

        try
        {
            var gameplayCamera = FindGameplayCamera();

            if (gameplayCamera is null)
            {
                logger.LogWarning(
                    "XR Rig Bootstrap could not find Player/CamHelper/Camera.");
                return false;
            }

            var cameraTransform = gameplayCamera.transform;
            var camHelper = cameraTransform.parent;

            if (camHelper is null || camHelper.name != "CamHelper")
            {
                logger.LogWarning(
                    $"XR Rig Bootstrap found the camera, but its parent was " +
                    $"'{camHelper?.name ?? "<null>"}' instead of 'CamHelper'.");
                return false;
            }

            var existingOrigin = camHelper.Find(OriginName);
            if (existingOrigin is not null)
            {
                logger.LogInfo(
                    $"XR rig already exists at " +
                    $"'{SceneExplorer.GetTransformPath(existingOrigin)}'.");
                LogRig(logger, gameplayCamera, existingOrigin);
                return true;
            }

            var originalLocalPosition = cameraTransform.localPosition;
            var originalLocalRotation = cameraTransform.localRotation;
            var originalLocalScale = cameraTransform.localScale;

            var originObject = new GameObject(OriginName);
            var origin = originObject.transform;
            origin.SetParent(camHelper, false);
            origin.localPosition = Vector3.zero;
            origin.localRotation = Quaternion.identity;
            origin.localScale = Vector3.one;

            var headObject = new GameObject(HeadName);
            var head = headObject.transform;
            head.SetParent(origin, false);
            head.localPosition = Vector3.zero;
            head.localRotation = Quaternion.identity;
            head.localScale = Vector3.one;

            cameraTransform.SetParent(head, false);
            cameraTransform.localPosition = originalLocalPosition;
            cameraTransform.localRotation = originalLocalRotation;
            cameraTransform.localScale = originalLocalScale;

            logger.LogInfo(
                "XR rig hierarchy created without enabling stereoscopic rendering.");

            LogRig(logger, gameplayCamera, origin);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                $"XR Rig Bootstrap failed: {exception}");
            return false;
        }
        finally
        {
            logger.LogInfo(
                $"=== XR Rig Bootstrap end: scene='{sceneName}' ===");
        }
    }

    [HideFromIl2Cpp]
    private static Camera? FindGameplayCamera()
    {
        var cameras = Camera.allCameras;

        for (var index = 0; index < cameras.Length; index++)
        {
            var camera = cameras[index];

            if (camera is null || !camera.enabled)
            {
                continue;
            }

            var path = SceneExplorer.GetTransformPath(camera.transform);

            if (path == "Player/CamHelper/Camera")
            {
                return camera;
            }

            if (camera.name == "Camera" &&
                path.Contains("Player/CamHelper", StringComparison.OrdinalIgnoreCase))
            {
                return camera;
            }
        }

        return null;
    }

    [HideFromIl2Cpp]
    private static void LogRig(
        ManualLogSource logger,
        Camera gameplayCamera,
        Transform origin)
    {
        var cameraTransform = gameplayCamera.transform;
        var head = cameraTransform.parent;

        logger.LogInfo(
            $"[XRRig] Origin path: " +
            $"'{SceneExplorer.GetTransformPath(origin)}'");

        logger.LogInfo(
            $"[XRRig] Head path: " +
            $"'{SceneExplorer.GetTransformPath(head)}'");

        logger.LogInfo(
            $"[XRRig] Camera path after bootstrap: " +
            $"'{SceneExplorer.GetTransformPath(cameraTransform)}'");

        logger.LogInfo(
            $"[XRRig] Origin local pose: " +
            $"position={FormatVector(origin.localPosition)}, " +
            $"rotation={FormatVector(origin.localEulerAngles)}");

        if (head is not null)
        {
            logger.LogInfo(
                $"[XRRig] Head local pose: " +
                $"position={FormatVector(head.localPosition)}, " +
                $"rotation={FormatVector(head.localEulerAngles)}");
        }

        logger.LogInfo(
            $"[XRRig] Camera local pose: " +
            $"position={FormatVector(cameraTransform.localPosition)}, " +
            $"rotation={FormatVector(cameraTransform.localEulerAngles)}");
    }

    [HideFromIl2Cpp]
    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:0.####}, {value.y:0.####}, {value.z:0.####})";
    }
}

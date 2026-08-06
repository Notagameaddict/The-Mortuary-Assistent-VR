using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.Rendering;

namespace MortuaryAssistantVR.Diagnostics;

internal static class RenderPipelineProbe
{
    [HideFromIl2Cpp]
    internal static void LogStatus(ManualLogSource logger, string sceneName)
    {
        logger.LogInfo($"=== Render Pipeline Probe begin: scene='{sceneName}' ===");

        try
        {
            LogPipelineAssets(logger);
            var gameplayCamera = FindGameplayCamera();

            if (gameplayCamera is null)
            {
                logger.LogWarning("[RenderProbe] Gameplay camera was not found.");
                LogAllCameras(logger);
                return;
            }

            LogCamera(logger, gameplayCamera);
            LogCameraComponents(logger, gameplayCamera);
        }
        catch (Exception exception)
        {
            logger.LogError($"Render Pipeline Probe failed: {exception}");
        }
        finally
        {
            logger.LogInfo($"=== Render Pipeline Probe end: scene='{sceneName}' ===");
        }
    }

    [HideFromIl2Cpp]
    private static void LogPipelineAssets(ManualLogSource logger)
    {
        logger.LogInfo(
            $"[RenderProbe] GraphicsSettings.currentRenderPipeline: " +
            $"{DescribeObject(GraphicsSettings.currentRenderPipeline)}");

        logger.LogInfo(
            $"[RenderProbe] QualitySettings.renderPipeline: " +
            $"{DescribeObject(QualitySettings.renderPipeline)}");

        var qualityLevel = QualitySettings.GetQualityLevel();
        var qualityName =
            qualityLevel >= 0 && qualityLevel < QualitySettings.names.Length
                ? QualitySettings.names[qualityLevel]
                : "<unknown>";

        logger.LogInfo(
            $"[RenderProbe] Quality level: index={qualityLevel}, name='{qualityName}'");
        logger.LogInfo($"[RenderProbe] Anti-aliasing: {QualitySettings.antiAliasing}x");
        logger.LogInfo($"[RenderProbe] Color space: {QualitySettings.activeColorSpace}");
    }

    [HideFromIl2Cpp]
    private static void LogCamera(ManualLogSource logger, Camera camera)
    {
        logger.LogInfo(
            $"[RenderProbe] Gameplay camera path: " +
            $"'{SceneExplorer.GetTransformPath(camera.transform)}'");

        logger.LogInfo(
            $"[RenderProbe] Projection: orthographic={camera.orthographic}, " +
            $"fieldOfView={camera.fieldOfView:0.###}, aspect={camera.aspect:0.####}, " +
            $"nearClip={camera.nearClipPlane:0.####}, farClip={camera.farClipPlane:0.###}");

        logger.LogInfo(
            $"[RenderProbe] Physical camera: usePhysicalProperties={camera.usePhysicalProperties}, " +
            $"focalLength={camera.focalLength:0.###}, " +
            $"sensorSize=({camera.sensorSize.x:0.###}, {camera.sensorSize.y:0.###}), " +
            $"lensShift=({camera.lensShift.x:0.###}, {camera.lensShift.y:0.###}), " +
            $"gateFit={camera.gateFit}");

        logger.LogInfo(
            $"[RenderProbe] Viewport: rect=({camera.rect.x:0.####}, {camera.rect.y:0.####}, " +
            $"{camera.rect.width:0.####}, {camera.rect.height:0.####}), " +
            $"pixelRect=({camera.pixelRect.x:0.##}, {camera.pixelRect.y:0.##}, " +
            $"{camera.pixelRect.width:0.##}, {camera.pixelRect.height:0.##})");

        logger.LogInfo(
            $"[RenderProbe] Rendering: enabled={camera.enabled}, depth={camera.depth:0.###}, " +
            $"clearFlags={camera.clearFlags}, cullingMask={camera.cullingMask}, " +
            $"renderingPath={camera.renderingPath}, actualRenderingPath={camera.actualRenderingPath}");

        logger.LogInfo(
            $"[RenderProbe] HDR/MSAA: allowHDR={camera.allowHDR}, " +
            $"allowMSAA={camera.allowMSAA}, " +
            $"allowDynamicResolution={camera.allowDynamicResolution}, " +
            $"forceIntoRenderTexture={camera.forceIntoRenderTexture}");

        logger.LogInfo(
            $"[RenderProbe] Stereo: stereoTargetEye={camera.stereoTargetEye}, " +
            $"stereoActiveEye={camera.stereoActiveEye}, stereoEnabled={camera.stereoEnabled}, " +
            $"stereoSeparation={camera.stereoSeparation:0.####}, " +
            $"stereoConvergence={camera.stereoConvergence:0.###}");

        logger.LogInfo(
            $"[RenderProbe] Target texture: {DescribeRenderTexture(camera.targetTexture)}");
        logger.LogInfo($"[RenderProbe] Target display: {camera.targetDisplay}");
        logger.LogInfo($"[RenderProbe] Command buffer count: {camera.commandBufferCount}");
    }

    [HideFromIl2Cpp]
    private static void LogCameraComponents(ManualLogSource logger, Camera camera)
    {
        var components = camera.gameObject.GetComponents<Component>();

        for (var index = 0; index < components.Length; index++)
        {
            var component = components[index];

            if (component is null)
            {
                logger.LogInfo($"[RenderProbe] Camera component[{index}]: <missing>");
                continue;
            }

            var typeName = component.GetIl2CppType()?.FullName
                ?? component.GetType().FullName
                ?? component.GetType().Name;

            logger.LogInfo($"[RenderProbe] Camera component[{index}]: {typeName}");
        }
    }

    [HideFromIl2Cpp]
    private static Camera? FindGameplayCamera()
    {
        var cameras = Camera.allCameras;

        for (var index = 0; index < cameras.Length; index++)
        {
            var camera = cameras[index];
            if (camera is null || !camera.enabled) continue;

            var path = SceneExplorer.GetTransformPath(camera.transform);

            if (path == "Player/CamHelper/Camera" ||
                path.EndsWith("/MortuaryAssistantVR_Head/Camera",
                    StringComparison.OrdinalIgnoreCase))
                return camera;

            if (camera.name == "Camera" &&
                path.Contains("Player/CamHelper", StringComparison.OrdinalIgnoreCase))
                return camera;
        }

        return null;
    }

    [HideFromIl2Cpp]
    private static void LogAllCameras(ManualLogSource logger)
    {
        var cameras = Camera.allCameras;

        for (var index = 0; index < cameras.Length; index++)
        {
            var camera = cameras[index];
            if (camera is null) continue;

            logger.LogInfo(
                $"[RenderProbe] Candidate[{index}]: name='{camera.name}', " +
                $"enabled={camera.enabled}, " +
                $"path='{SceneExplorer.GetTransformPath(camera.transform)}'");
        }
    }

    [HideFromIl2Cpp]
    private static string DescribeObject(UnityEngine.Object? value)
    {
        if (value is null) return "<null>";

        var typeName = value.GetIl2CppType()?.FullName
            ?? value.GetType().FullName
            ?? value.GetType().Name;

        return $"name='{value.name}', type='{typeName}'";
    }

    [HideFromIl2Cpp]
    private static string DescribeRenderTexture(RenderTexture? value)
    {
        if (value is null)
            return "<null; camera renders to display/backbuffer>";

        return
            $"name='{value.name}', size={value.width}x{value.height}, " +
            $"depth={value.depth}, format={value.format}, " +
            $"dimension={value.dimension}, volumeDepth={value.volumeDepth}, " +
            $"antiAliasing={value.antiAliasing}, useDynamicScale={value.useDynamicScale}";
    }
}

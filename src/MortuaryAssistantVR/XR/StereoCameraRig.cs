using BepInEx.Logging;
using UnityEngine;

namespace MortuaryAssistantVR.XR;

internal static class StereoCameraRig
{
    private const int EyeTextureWidth = 1600;
    private const int EyeTextureHeight = 1728;
    private const float HalfIpdMetres = 0.032f;
    private const float PrototypeFieldOfView = 92.0f;

    private static ManualLogSource? _logger;
    private static GameObject? _leftEyeObject;
    private static GameObject? _rightEyeObject;
    private static RenderTexture? _leftEyeTexture;
    private static RenderTexture? _rightEyeTexture;
    private static Camera? _leftEyeCamera;
    private static Camera? _rightEyeCamera;

    internal static bool IsReady =>
        _leftEyeTexture is not null &&
        _rightEyeTexture is not null &&
        _leftEyeCamera is not null &&
        _rightEyeCamera is not null;

    internal static bool TryCreate(
        ManualLogSource logger)
    {
        if (IsReady)
        {
            return true;
        }

        _logger =
            logger;

        var headObject =
            GameObject.Find(
                "MortuaryAssistantVR_Head");

        if (headObject is null)
        {
            logger.LogWarning(
                "[StereoRig] XR head object was not found.");

            return false;
        }

        var sourceCamera =
            FindGameplayCamera(
                headObject.transform);

        if (sourceCamera is null)
        {
            logger.LogWarning(
                "[StereoRig] Gameplay camera was not found.");

            return false;
        }

        Reset();

        try
        {
            _leftEyeTexture =
                CreateEyeTexture(
                    "MortuaryAssistantVR_LeftEyeTexture");

            _rightEyeTexture =
                CreateEyeTexture(
                    "MortuaryAssistantVR_RightEyeTexture");

            _leftEyeObject =
                new GameObject(
                    "MortuaryAssistantVR_LeftEyeCamera");

            _rightEyeObject =
                new GameObject(
                    "MortuaryAssistantVR_RightEyeCamera");

            _leftEyeObject.transform.SetParent(
                headObject.transform,
                false);

            _rightEyeObject.transform.SetParent(
                headObject.transform,
                false);

            _leftEyeObject.transform.localPosition =
                new Vector3(
                    -HalfIpdMetres,
                    0,
                    0);

            _rightEyeObject.transform.localPosition =
                new Vector3(
                    HalfIpdMetres,
                    0,
                    0);

            _leftEyeObject.transform.localRotation =
                Quaternion.identity;

            _rightEyeObject.transform.localRotation =
                Quaternion.identity;

            _leftEyeCamera =
                _leftEyeObject.AddComponent<Camera>();

            _rightEyeCamera =
                _rightEyeObject.AddComponent<Camera>();

            ConfigureEyeCamera(
                sourceCamera,
                _leftEyeCamera,
                _leftEyeTexture,
                "left");

            ConfigureEyeCamera(
                sourceCamera,
                _rightEyeCamera,
                _rightEyeTexture,
                "right");

            var leftPointer =
                _leftEyeTexture.GetNativeTexturePtr();

            var rightPointer =
                _rightEyeTexture.GetNativeTexturePtr();

            if (leftPointer == IntPtr.Zero ||
                rightPointer == IntPtr.Zero)
            {
                logger.LogError(
                    "[StereoRig] Unity returned a null native texture pointer.");

                Reset();
                return false;
            }

            if (!D3D11PresentHookProbe.SetStereoSourceTextures(
                    logger,
                    leftPointer,
                    rightPointer))
            {
                Reset();
                return false;
            }

            logger.LogInfo(
                $"[StereoRig] Prototype eye cameras created: " +
                $"{EyeTextureWidth}x{EyeTextureHeight}, " +
                $"FOV={PrototypeFieldOfView:F1}, " +
                $"IPD={HalfIpdMetres * 2.0f:F3}m, " +
                $"left=0x{leftPointer.ToInt64():X}, " +
                $"right=0x{rightPointer.ToInt64():X}.");

            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                $"[StereoRig] Creation failed: {exception}");

            Reset();
            return false;
        }
    }

    internal static void Reset()
    {
        if (_logger is not null)
        {
            D3D11PresentHookProbe.ClearStereoSourceTextures(
                _logger);
        }

        DestroyCameraObject(
            ref _leftEyeObject,
            ref _leftEyeCamera);

        DestroyCameraObject(
            ref _rightEyeObject,
            ref _rightEyeCamera);

        DestroyRenderTexture(
            ref _leftEyeTexture);

        DestroyRenderTexture(
            ref _rightEyeTexture);
    }

    private static Camera? FindGameplayCamera(
        Transform headTransform)
    {
        var cameras =
            headTransform.GetComponentsInChildren<Camera>(
                true);

        foreach (var camera in cameras)
        {
            if (camera is null)
            {
                continue;
            }

            if (camera.gameObject.name ==
                    "MortuaryAssistantVR_LeftEyeCamera" ||
                camera.gameObject.name ==
                    "MortuaryAssistantVR_RightEyeCamera")
            {
                continue;
            }

            return camera;
        }

        return Camera.main;
    }

    private static RenderTexture CreateEyeTexture(
        string name)
    {
        var texture =
            new RenderTexture(
                EyeTextureWidth,
                EyeTextureHeight,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name =
                    name,

                antiAliasing =
                    1,

                useMipMap =
                    false,

                autoGenerateMips =
                    false
            };

        texture.Create();

        return texture;
    }

    private static void ConfigureEyeCamera(
        Camera source,
        Camera eye,
        RenderTexture target,
        string eyeName)
    {
        eye.CopyFrom(
            source);

        eye.name =
            $"MortuaryAssistantVR_{eyeName}_Camera";

        eye.targetTexture =
            target;

        eye.stereoTargetEye =
            StereoTargetEyeMask.None;

        eye.fieldOfView =
            PrototypeFieldOfView;

        eye.aspect =
            (float)EyeTextureWidth /
            EyeTextureHeight;

        eye.depth =
            source.depth - 10.0f;

        eye.enabled =
            true;
    }

    private static void DestroyCameraObject(
        ref GameObject? cameraObject,
        ref Camera? camera)
    {
        camera =
            null;

        if (cameraObject is not null)
        {
            UnityEngine.Object.Destroy(
                cameraObject);

            cameraObject =
                null;
        }
    }

    private static void DestroyRenderTexture(
        ref RenderTexture? texture)
    {
        if (texture is null)
        {
            return;
        }

        texture.Release();

        UnityEngine.Object.Destroy(
            texture);

        texture =
            null;
    }
}

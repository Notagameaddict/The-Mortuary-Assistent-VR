using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using UnityEngine;

namespace MortuaryAssistantVR.XR;

internal static class StereoCameraRig
{
    private const int EyeTextureWidth = 1600;
    private const int EyeTextureHeight = 1728;
    // Match the game's normal 16:9 desktop backbuffer. The previous
    // 1600x900 target changed the Canvas pixel mapping relative to the
    // 2560x1440 screen, which made radial/tool-menu hit regions drift and
    // also softened the UI after resampling into the OpenXR quad.
    private const int ToolUiTextureWidth = 2560;
    private const int ToolUiTextureHeight = 1440;
    private const int ToolUiCaptureLayer = 30;
    private const float HalfIpdMetres = 0.032f;
    private const float PrototypeFieldOfView = 92.0f;
    private const float UiFallbackReleaseDelaySeconds = 0.75f;
    private const float ToolUiReleaseDelaySeconds = 0.75f;
    private const int VirtualKeyRightMouseButton = 0x02;

    private static ManualLogSource? _logger;
    private static GameObject? _leftEyeObject;
    private static GameObject? _rightEyeObject;
    private static RenderTexture? _leftEyeTexture;
    private static RenderTexture? _rightEyeTexture;
    private static Camera? _leftEyeCamera;
    private static Camera? _rightEyeCamera;
    private static Camera? _sourceGameplayCamera;
    private static bool _sourceGameplayCameraWasEnabled;

    private static GameObject? _toolUiCameraObject;
    private static Camera? _toolUiCamera;
    private static RenderTexture? _toolUiTexture;
    private static IntPtr _toolUiNativeTexture;

    private static Canvas? _inGameCanvas;
    private static RenderMode _originalCanvasRenderMode;
    private static Camera? _originalCanvasWorldCamera;
    private static float _originalCanvasPlaneDistance;
    private static bool _canvasCaptureApplied;
    private static bool _canvasCaptureFailureLogged;

    private static readonly List<GameObject> _toolUiLayerObjects =
        new();

    private static readonly List<int> _toolUiOriginalLayers =
        new();

    private static readonly List<CanvasRenderer> _suppressedRootCanvasRenderers =
        new();

    private static readonly List<bool> _suppressedRootCanvasRendererCullStates =
        new();
    private static long _lastProjectionSequence;
    private static IntPtr _leftNativeTexture;
    private static IntPtr _rightNativeTexture;
    private static bool _stereoSourcesRegistered;
    private static bool _pauseFallbackActive;
    private static bool _stereoUiLayerActive;
    private static float _lastUiFallbackSignalTime;
    private static float _lastToolUiSignalTime;

    private static bool _cursorReflectionInitialized;
    private static PropertyInfo? _cursorVisibleProperty;

    [DllImport(
        "user32.dll",
        EntryPoint = "GetAsyncKeyState")]
    private static extern short GetAsyncKeyState(
        int virtualKey);

    internal static bool IsUiFallbackActive =>
        _pauseFallbackActive;

    internal static IntPtr ToolUiNativeTexture =>
        _stereoUiLayerActive
            ? _toolUiNativeTexture
            : IntPtr.Zero;

    internal static bool StereoUiLayerActive =>
        _stereoUiLayerActive &&
        _stereoSourcesRegistered &&
        !_pauseFallbackActive;

    internal static bool IsReady =>
        _leftEyeTexture != null &&
        _rightEyeTexture != null &&
        _leftEyeCamera != null &&
        _rightEyeCamera != null;

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

        if (headObject == null)
        {
            logger.LogWarning(
                "[StereoRig] XR head object was not found.");

            return false;
        }

        var sourceCamera =
            FindGameplayCamera(
                headObject.transform);

        if (sourceCamera == null)
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

            CreateToolUiCaptureResources(
                sourceCamera);

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

            _leftNativeTexture =
                _leftEyeTexture.GetNativeTexturePtr();

            _rightNativeTexture =
                _rightEyeTexture.GetNativeTexturePtr();

            if (_leftNativeTexture == IntPtr.Zero ||
                _rightNativeTexture == IntPtr.Zero)
            {
                logger.LogError(
                    "[StereoRig] Unity returned a null native texture pointer.");

                Reset();
                return false;
            }

            if (!D3D11PresentHookProbe.SetStereoSourceTextures(
                    logger,
                    _leftNativeTexture,
                    _rightNativeTexture))
            {
                Reset();
                return false;
            }

            logger.LogInfo(
                $"[StereoRig] Prototype eye cameras created: " +
                $"{EyeTextureWidth}x{EyeTextureHeight}, " +
                $"FOV={PrototypeFieldOfView:F1}, " +
                $"IPD={HalfIpdMetres * 2.0f:F3}m, " +
                $"left=0x{_leftNativeTexture.ToInt64():X}, " +
                $"right=0x{_rightNativeTexture.ToInt64():X}.");

            _stereoSourcesRegistered =
                true;

            _pauseFallbackActive =
                false;

            _sourceGameplayCamera =
                sourceCamera;

            _sourceGameplayCameraWasEnabled =
                sourceCamera.enabled;

            // The desktop camera would otherwise render the HDRP scene a
            // third time every frame. The two eye cameras already render it.
            sourceCamera.enabled =
                false;

            logger.LogInfo(
                "[StereoRig] Disabled desktop gameplay camera while " +
                "stereo rendering is active.");

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

    internal static void UpdatePresentationMode()
    {
        if (!IsReady ||
            _logger is null)
        {
            return;
        }

        var paused =
            Time.timeScale <= 0.0001f;

        var cursorVisible =
            IsUnityCursorVisible();

        var rightMouseDown =
            (GetAsyncKeyState(
                 VirtualKeyRightMouseButton) &
             0x8000) != 0;

        var now =
            Time.realtimeSinceStartup;

        var computerUiSignal =
            cursorVisible &&
            InteractionPromptDetector.ComputerTargetActive;

        var documentUiSignal =
            InteractionPromptDetector.DocumentInspectionActive;

        if (paused ||
            computerUiSignal ||
            documentUiSignal)
        {
            _lastUiFallbackSignalTime =
                now;
        }

        var fallbackReleaseDelay =
            _pauseFallbackActive &&
            now -
            _lastUiFallbackSignalTime <
            UiFallbackReleaseDelaySeconds;

        var shouldUseFullFallback =
            paused ||
            computerUiSignal ||
            documentUiSignal ||
            fallbackReleaseDelay;

        // The deep probe established that the game's tool menu is
        // InGameUI/Rsystem/CircleRet. Use its real hierarchy visibility
        // instead of guessing from RMB/cursor state.
        var toolUiSignal =
            ToolMenuUiProbe.CircleRetVisible;

        if (toolUiSignal)
        {
            _lastToolUiSignalTime =
                now;
        }

        var toolUiReleaseDelay =
            _stereoUiLayerActive &&
            now -
            _lastToolUiSignalTime <
            0.10f;

        var shouldUseStereoUiLayer =
            !shouldUseFullFallback &&
            (toolUiSignal ||
             toolUiReleaseDelay);

        if (shouldUseFullFallback !=
            _pauseFallbackActive)
        {
            _pauseFallbackActive =
                shouldUseFullFallback;

            if (_pauseFallbackActive)
            {
                _stereoUiLayerActive =
                    false;

                RestoreToolUiCanvas();

                if (_sourceGameplayCamera != null)
                {
                    _sourceGameplayCamera.enabled =
                        _sourceGameplayCameraWasEnabled;
                }

                D3D11PresentHookProbe.ClearStereoSourceTextures(
                    _logger);

                _stereoSourcesRegistered =
                    false;

                _logger.LogInfo(
                    $"[StereoRig] Full UI fallback active; " +
                    $"paused={paused}, cursorVisible={cursorVisible}, " +
                    $"computerTarget={InteractionPromptDetector.ComputerTargetActive}, " +
                    $"documentInspection={documentUiSignal}. " +
                    "Using desktop/cinema output.");
            }
            else if (_leftNativeTexture != IntPtr.Zero &&
                     _rightNativeTexture != IntPtr.Zero)
            {
                _stereoSourcesRegistered =
                    D3D11PresentHookProbe.SetStereoSourceTextures(
                        _logger,
                        _leftNativeTexture,
                        _rightNativeTexture);

                if (_stereoSourcesRegistered)
                {
                    _logger.LogInfo(
                        "[StereoRig] Full UI fallback ended; " +
                        "stereo eye textures restored.");
                }
            }
        }

        if (_pauseFallbackActive)
        {
            return;
        }

        if (shouldUseStereoUiLayer)
        {
            ClearToolUiTextureTransparent();
        }

        if (shouldUseStereoUiLayer !=
            _stereoUiLayerActive)
        {
            _stereoUiLayerActive =
                shouldUseStereoUiLayer;

            if (_stereoUiLayerActive)
            {
                if (!TryApplyToolUiCanvasCapture())
                {
                    if (!_canvasCaptureFailureLogged)
                    {
                        _logger.LogWarning(
                            "[StereoRig] CircleRet UI capture could not be " +
                            "configured; stereo UI layer disabled.");
                    }

                    _stereoUiLayerActive =
                        false;
                }
                else
                {
                    _logger.LogInfo(
                        "[StereoRig] CircleRet stereo UI capture active with " +
                        "Canvas root included and Rsystem excluded. " +
                        "Tool UI texture is cleared to transparent each frame.");
                }
            }
            else
            {
                RestoreToolUiCanvas();

                _logger.LogInfo(
                    "[StereoRig] CircleRet stereo UI capture ended.");
            }
        }
        else
        {
            // Keep the desktop camera state deterministic while modes remain
            // unchanged.
            if (_sourceGameplayCamera != null)
            {
                _sourceGameplayCamera.enabled =
                    false;
            }
        }
    }

    private static bool IsUnityCursorVisible()
    {
        if (!_cursorReflectionInitialized)
        {
            _cursorReflectionInitialized =
                true;

            foreach (var assembly in
                     AppDomain.CurrentDomain.GetAssemblies())
            {
                var cursorType =
                    assembly.GetType(
                        "UnityEngine.Cursor",
                        throwOnError: false,
                        ignoreCase: false);

                if (cursorType is null)
                {
                    continue;
                }

                _cursorVisibleProperty =
                    cursorType.GetProperty(
                        "visible",
                        BindingFlags.Static |
                        BindingFlags.Public);

                if (_cursorVisibleProperty is not null)
                {
                    _logger?.LogInfo(
                        "[StereoRig] Unity cursor visibility bridge " +
                        "resolved for screen-space UI detection.");
                }

                break;
            }
        }

        if (_cursorVisibleProperty is null)
        {
            return false;
        }

        try
        {
            return _cursorVisibleProperty.GetValue(
                    null) is true;
        }
        catch
        {
            return false;
        }
    }

    internal static void ApplyOpenXrProjection(
        OpenXrHeadPose pose)
    {
        if (!IsReady ||
            pose.Sequence == _lastProjectionSequence)
        {
            return;
        }

        _lastProjectionSequence =
            pose.Sequence;

        ApplyEyeProjection(
            _leftEyeCamera!,
            pose.LeftAngleLeft,
            pose.LeftAngleRight,
            pose.LeftAngleDown,
            pose.LeftAngleUp);

        ApplyEyeProjection(
            _rightEyeCamera!,
            pose.RightAngleLeft,
            pose.RightAngleRight,
            pose.RightAngleDown,
            pose.RightAngleUp);

        if (pose.Sequence <= 5 ||
            pose.Sequence % 600 == 0)
        {
            _logger?.LogInfo(
                $"[StereoRig] Applied asymmetric OpenXR projections: " +
                $"sequence={pose.Sequence}, " +
                $"left=({pose.LeftAngleLeft:F3}, " +
                $"{pose.LeftAngleRight:F3}, " +
                $"{pose.LeftAngleDown:F3}, " +
                $"{pose.LeftAngleUp:F3}), " +
                $"right=({pose.RightAngleLeft:F3}, " +
                $"{pose.RightAngleRight:F3}, " +
                $"{pose.RightAngleDown:F3}, " +
                $"{pose.RightAngleUp:F3}).");
        }
    }

    private static void ApplyEyeProjection(
        Camera camera,
        float angleLeft,
        float angleRight,
        float angleDown,
        float angleUp)
    {
        var near =
            Math.Max(
                0.01f,
                camera.nearClipPlane);

        var far =
            Math.Max(
                near + 0.01f,
                camera.farClipPlane);

        var left =
            MathF.Tan(
                angleLeft) *
            near;

        var right =
            MathF.Tan(
                angleRight) *
            near;

        var bottom =
            MathF.Tan(
                angleDown) *
            near;

        var top =
            MathF.Tan(
                angleUp) *
            near;

        camera.projectionMatrix =
            Matrix4x4.Frustum(
                left,
                right,
                bottom,
                top,
                near,
                far);
    }

    internal static void Reset()
    {
        _lastProjectionSequence =
            0;

        RestoreToolUiCanvas();

        if (_sourceGameplayCamera != null)
        {
            _sourceGameplayCamera.enabled =
                _sourceGameplayCameraWasEnabled;
        }

        _sourceGameplayCamera =
            null;

        _sourceGameplayCameraWasEnabled =
            false;

        _leftNativeTexture =
            IntPtr.Zero;

        _rightNativeTexture =
            IntPtr.Zero;

        _stereoSourcesRegistered =
            false;

        _pauseFallbackActive =
            false;

        _stereoUiLayerActive =
            false;

        _lastUiFallbackSignalTime =
            0.0f;

        _lastToolUiSignalTime =
            0.0f;

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

        DestroyCameraObject(
            ref _toolUiCameraObject,
            ref _toolUiCamera);

        DestroyRenderTexture(
            ref _toolUiTexture);

        _toolUiNativeTexture =
            IntPtr.Zero;

        _inGameCanvas =
            null;

        _canvasCaptureFailureLogged =
            false;

        RestoreToolUiLayerIsolation();
    }

    private static void CreateToolUiCaptureResources(
        Camera sourceCamera)
    {
        _toolUiTexture =
            new RenderTexture(
                ToolUiTextureWidth,
                ToolUiTextureHeight,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name =
                    "MortuaryAssistantVR_ToolUiTexture",

                antiAliasing =
                    1,

                useMipMap =
                    false,

                autoGenerateMips =
                    false
            };

        _toolUiTexture.Create();

        _toolUiNativeTexture =
            _toolUiTexture.GetNativeTexturePtr();

        _toolUiCameraObject =
            new GameObject(
                "MortuaryAssistantVR_ToolUiCamera");

        _toolUiCamera =
            _toolUiCameraObject.AddComponent<Camera>();

        _toolUiCamera.CopyFrom(
            sourceCamera);

        _toolUiCamera.name =
            "MortuaryAssistantVR_ToolUiCamera";

        _toolUiCamera.targetTexture =
            _toolUiTexture;

        _toolUiCamera.rect =
            new Rect(
                0.0f,
                0.0f,
                1.0f,
                1.0f);

        _toolUiCamera.aspect =
            (float)ToolUiTextureWidth /
            ToolUiTextureHeight;

        _toolUiCamera.allowHDR =
            false;

        _toolUiCamera.allowMSAA =
            false;

        _toolUiCamera.allowDynamicResolution =
            false;

        _toolUiCamera.stereoTargetEye =
            StereoTargetEyeMask.None;

        _toolUiCamera.clearFlags =
            CameraClearFlags.Nothing;

        _toolUiCamera.backgroundColor =
            new Color(
                0.0f,
                0.0f,
                0.0f,
                0.0f);

        _toolUiCamera.cullingMask =
            1 << ToolUiCaptureLayer;

        _toolUiCamera.depth =
            sourceCamera.depth +
            50.0f;

        // Screen-space-camera UI does not need to follow the player's world
        // position. Keeping this camera at a neutral point prevents indoor
        // HDRP volumes/exposure/fog from changing the menu appearance.
        _toolUiCameraObject.transform.position =
            new Vector3(
                0.0f,
                -10000.0f,
                0.0f);

        _toolUiCameraObject.transform.rotation =
            Quaternion.identity;

        ConfigureNeutralToolUiCamera();

        _toolUiCamera.enabled =
            false;

        _logger?.LogInfo(
            $"[StereoRig] Tool UI capture texture created: " +
            $"{ToolUiTextureWidth}x{ToolUiTextureHeight}, " +
            $"native=0x{_toolUiNativeTexture.ToInt64():X}.");
    }

    private static void ConfigureNeutralToolUiCamera()
    {
        if (_toolUiCameraObject == null)
        {
            return;
        }

        foreach (var component in
                 _toolUiCameraObject.GetComponents<Component>())
        {
            if (component == null)
            {
                continue;
            }

            var type =
                component.GetType();

            var typeName =
                type.FullName ??
                type.Name;

            if (!typeName.Contains(
                    "HDAdditionalCameraData",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // These properties differ a little between HDRP versions. Set
            // whichever ones exist. Failures are diagnostic-only and should
            // never break the menu.
            TrySetBoolProperty(
                component,
                type,
                "customRenderingSettings",
                true);

            TrySetBoolProperty(
                component,
                type,
                "allowDynamicResolution",
                false);

            TrySetBoolProperty(
                component,
                type,
                "dithering",
                false);

            TrySetBoolProperty(
                component,
                type,
                "stopNaNs",
                false);

            TrySetBoolProperty(
                component,
                type,
                "hasPersistentHistory",
                false);

            _logger?.LogInfo(
                $"[StereoRig] Neutralized HDRP camera data on " +
                $"'{typeName}' for tool UI capture.");

            break;
        }
    }

    private static void TrySetBoolProperty(
        object target,
        Type type,
        string propertyName,
        bool value)
    {
        try
        {
            var property =
                type.GetProperty(
                    propertyName,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            if (property == null ||
                !property.CanWrite ||
                property.PropertyType !=
                    typeof(bool))
            {
                return;
            }

            property.SetValue(
                target,
                value);
        }
        catch
        {
            // Version-dependent HDRP property; ignore if unavailable.
        }
    }

    private static void ClearToolUiTextureTransparent()
    {
        if (_toolUiTexture == null)
        {
            return;
        }

        var previous =
            RenderTexture.active;

        try
        {
            RenderTexture.active =
                _toolUiTexture;

            GL.Clear(
                true,
                true,
                new Color(
                    0.0f,
                    0.0f,
                    0.0f,
                    0.0f));
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(
                $"[StereoRig] Transparent UI texture clear failed: " +
                $"{exception.Message}");
        }
        finally
        {
            RenderTexture.active =
                previous;
        }
    }

    private static bool TryApplyToolUiCanvasCapture()
    {
        if (_toolUiCamera == null ||
            _toolUiTexture == null ||
            _toolUiNativeTexture == IntPtr.Zero)
        {
            return false;
        }

        if (_canvasCaptureApplied)
        {
            _toolUiCamera.enabled =
                true;

            return true;
        }

        var inGameUi =
            GameObject.Find(
                "InGameUI");

        if (inGameUi == null)
        {
            return false;
        }

        _inGameCanvas =
            inGameUi.GetComponent<Canvas>();

        if (_inGameCanvas == null)
        {
            if (!_canvasCaptureFailureLogged)
            {
                _canvasCaptureFailureLogged =
                    true;

                _logger?.LogWarning(
                    "[StereoRig] InGameUI Canvas component was not found.");
            }

            return false;
        }

        try
        {
            _originalCanvasRenderMode =
                _inGameCanvas.renderMode;

            _originalCanvasWorldCamera =
                _inGameCanvas.worldCamera;

            _originalCanvasPlaneDistance =
                _inGameCanvas.planeDistance;

            _inGameCanvas.renderMode =
                RenderMode.ScreenSpaceCamera;

            _inGameCanvas.worldCamera =
                _toolUiCamera;

            _inGameCanvas.planeDistance =
                1.0f;

            if (!ApplyToolUiLayerIsolation(
                    inGameUi))
            {
                throw new InvalidOperationException(
                    "CircleRet layer isolation could not be configured.");
            }

            ClearToolUiTextureTransparent();

            _toolUiCamera.enabled =
                true;

            _canvasCaptureApplied =
                true;

            _canvasCaptureFailureLogged =
                false;

            _logger?.LogInfo(
                $"[StereoRig] InGameUI Canvas redirected directly to " +
                $"transparent tool UI RenderTexture; " +
                $"screen={Screen.width}x{Screen.height}, " +
                $"capture={ToolUiTextureWidth}x{ToolUiTextureHeight}.");

            return true;
        }
        catch (Exception exception)
        {
            if (!_canvasCaptureFailureLogged)
            {
                _canvasCaptureFailureLogged =
                    true;

                _logger?.LogWarning(
                    $"[StereoRig] Redirecting InGameUI Canvas failed: " +
                    $"{exception.Message}");
            }

            RestoreToolUiCanvas();

            return false;
        }
    }

    private static void RestoreToolUiCanvas()
    {
        if (_toolUiCamera != null)
        {
            _toolUiCamera.enabled =
                false;
        }

        RestoreToolUiLayerIsolation();

        if (!_canvasCaptureApplied ||
            _inGameCanvas == null)
        {
            _canvasCaptureApplied =
                false;

            return;
        }

        try
        {
            _inGameCanvas.renderMode =
                _originalCanvasRenderMode;

            _inGameCanvas.worldCamera =
                _originalCanvasWorldCamera;

            _inGameCanvas.planeDistance =
                _originalCanvasPlaneDistance;
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(
                $"[StereoRig] Restoring InGameUI Canvas failed: " +
                $"{exception.Message}");
        }
        finally
        {
            _canvasCaptureApplied =
                false;

            _inGameCanvas =
                null;
        }
    }

    private static bool ApplyToolUiLayerIsolation(
        GameObject inGameUi)
    {
        RestoreToolUiLayerIsolation();

        var circleRet =
            GameObject.Find(
                "InGameUI/Rsystem/CircleRet");

        if (circleRet == null)
        {
            _logger?.LogWarning(
                "[StereoRig] CircleRet was not found for layer isolation.");

            return false;
        }

        // The previous CircleRet-only experiment proved that the root
        // Canvas itself must also be visible to the capture camera. Without
        // the Canvas root, the child UI is not submitted at all and only the
        // camera background/cursor reaches the OpenXR quad.
        //
        // Move the InGameUI root (which owns the Canvas) plus CircleRet and
        // its descendants. Deliberately leave the intermediate Rsystem
        // object on its original layer so any full-screen background/dimmer
        // component there is excluded from the UI camera.
        SaveAndSetLayer(
            inGameUi);

        SuppressInGameUiRootCanvasRenderers(
            inGameUi);

        SetSubtreeLayer(
            circleRet.transform);

        _logger?.LogInfo(
            $"[StereoRig] InGameUI Canvas root + CircleRet subtree isolated " +
            $"on capture layer {ToolUiCaptureLayer}; " +
            $"objects={_toolUiLayerObjects.Count}. Rsystem remains excluded.");

        return true;
    }

    private static void SetSubtreeLayer(
        Transform transform)
    {
        if (transform == null)
        {
            return;
        }

        SaveAndSetLayer(
            transform.gameObject);

        for (var childIndex = 0;
             childIndex < transform.childCount;
             childIndex++)
        {
            SetSubtreeLayer(
                transform.GetChild(
                    childIndex));
        }
    }

    private static void SaveAndSetLayer(
        GameObject gameObject)
    {
        if (gameObject == null)
        {
            return;
        }

        for (var index = 0;
             index < _toolUiLayerObjects.Count;
             index++)
        {
            if (_toolUiLayerObjects[index] ==
                gameObject)
            {
                return;
            }
        }

        _toolUiLayerObjects.Add(
            gameObject);

        _toolUiOriginalLayers.Add(
            gameObject.layer);

        gameObject.layer =
            ToolUiCaptureLayer;
    }

    private static void SuppressInGameUiRootCanvasRenderers(
        GameObject inGameUi)
    {
        RestoreInGameUiRootCanvasRenderers();

        CanvasRenderer[] renderers;

        try
        {
            renderers =
                inGameUi.GetComponents<CanvasRenderer>();
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(
                $"[StereoRig] Reading InGameUI root CanvasRenderer(s) failed: " +
                $"{exception.Message}");

            return;
        }

        foreach (var renderer in
                 renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            _suppressedRootCanvasRenderers.Add(
                renderer);

            _suppressedRootCanvasRendererCullStates.Add(
                renderer.cull);

            // The root Canvas must be on the capture layer so the CircleRet
            // descendants are submitted, but any Graphic attached directly
            // to that same root must not draw. Its CanvasRenderer is the only
            // remaining non-CircleRet renderer on layer 30.
            renderer.cull =
                true;
        }

        _logger?.LogInfo(
            $"[StereoRig] Suppressed " +
            $"{_suppressedRootCanvasRenderers.Count} CanvasRenderer(s) " +
            "attached directly to InGameUI root during tool UI capture.");
    }

    private static void RestoreInGameUiRootCanvasRenderers()
    {
        var count =
            Math.Min(
                _suppressedRootCanvasRenderers.Count,
                _suppressedRootCanvasRendererCullStates.Count);

        for (var index = 0;
             index < count;
             index++)
        {
            var renderer =
                _suppressedRootCanvasRenderers[index];

            if (renderer != null)
            {
                renderer.cull =
                    _suppressedRootCanvasRendererCullStates[index];
            }
        }

        _suppressedRootCanvasRenderers.Clear();
        _suppressedRootCanvasRendererCullStates.Clear();
    }

    private static void RestoreToolUiLayerIsolation()
    {
        RestoreInGameUiRootCanvasRenderers();

        var count =
            Math.Min(
                _toolUiLayerObjects.Count,
                _toolUiOriginalLayers.Count);

        for (var index = 0;
             index < count;
             index++)
        {
            var gameObject =
                _toolUiLayerObjects[index];

            if (gameObject != null)
            {
                gameObject.layer =
                    _toolUiOriginalLayers[index];
            }
        }

        _toolUiLayerObjects.Clear();
        _toolUiOriginalLayers.Clear();
    }

    private static Camera? FindGameplayCamera(
        Transform headTransform)
    {
        var cameras =
            headTransform.GetComponentsInChildren<Camera>(
                true);

        foreach (var camera in cameras)
        {
            if (camera == null)
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

        if (cameraObject != null)
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
        if (texture == null)
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

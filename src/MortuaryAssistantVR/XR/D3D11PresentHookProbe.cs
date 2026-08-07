using System.Runtime.InteropServices;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;

namespace MortuaryAssistantVR.XR;

internal static class D3D11PresentHookProbe
{
    private static readonly object CallbackSyncRoot = new();

    private static Action? _managedPresentCallback;
    private static NativePresentCallbackDelegate? _nativePresentCallback;
    private static volatile bool _stereoSourceTexturesReady;

    private const string LibraryName =
        "MortuaryAssistantVR.PresentHookProbe";

    [HideFromIl2Cpp]
    internal static bool Install(
        ManualLogSource logger)
    {
        try
        {
            var result =
                MavrInstallPresentHook();

            logger.LogInfo(
                $"[PresentHookProbe] Install result={result}.");

            return result == 0;
        }
        catch (Exception exception)
        {
            logger.LogError(
                $"[PresentHookProbe] Install failed: {exception}");

            return false;
        }
    }

    [HideFromIl2Cpp]
    internal static bool TryGetDevice(
        ManualLogSource logger,
        out UnityD3D11DeviceInfo deviceInfo,
        bool logResult)
    {
        deviceInfo = default;

        try
        {
            var result =
                MavrGetCapturedD3D11DeviceInfo(
                    out var devicePointer,
                    out var luidLowPart,
                    out var luidHighPart,
                    out var featureLevel);

            if (logResult ||
                result == 0)
            {
                logger.LogInfo(
                    $"[PresentHookProbe] Query result={result}, " +
                    $"device=0x{devicePointer.ToInt64():X}, " +
                    $"luidHigh=0x{unchecked((uint)luidHighPart):X8}, " +
                    $"luidLow=0x{luidLowPart:X8}, " +
                    $"featureLevel=0x{featureLevel:X}.");
            }

            if (result != 0 ||
                devicePointer == IntPtr.Zero)
            {
                return false;
            }

            deviceInfo =
                new UnityD3D11DeviceInfo(
                    devicePointer,
                    luidLowPart,
                    luidHighPart,
                    featureLevel);

            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                $"[PresentHookProbe] Query failed: {exception}");

            return false;
        }
    }

    [HideFromIl2Cpp]
    internal static void SetInteractionPromptState(
        ManualLogSource? logger,
        bool visible,
        float leftU,
        float leftV,
        float rightU,
        float rightV)
    {
        try
        {
            var result =
                MavrSetInteractionPromptState(
                    visible
                        ? 1
                        : 0,
                    leftU,
                    leftV,
                    rightU,
                    rightV);

            if (result != 0)
            {
                logger?.LogWarning(
                    $"[PresentHookProbe] Interaction prompt state " +
                    $"result={result}.");
            }
        }
        catch (Exception exception)
        {
            logger?.LogWarning(
                $"[PresentHookProbe] Interaction prompt update failed: " +
                $"{exception.Message}");
        }
    }

    internal static bool StereoSourceTexturesReady =>
        _stereoSourceTexturesReady;

    [HideFromIl2Cpp]
    internal static bool SetStereoSourceTextures(
        ManualLogSource logger,
        IntPtr leftTexture,
        IntPtr rightTexture)
    {
        try
        {
            var result =
                MavrSetStereoSourceTextures(
                    leftTexture,
                    rightTexture);

            _stereoSourceTexturesReady =
                result == 0 &&
                leftTexture != IntPtr.Zero &&
                rightTexture != IntPtr.Zero;

            logger.LogInfo(
                $"[PresentHookProbe] Set stereo sources " +
                $"result={result}, " +
                $"left=0x{leftTexture.ToInt64():X}, " +
                $"right=0x{rightTexture.ToInt64():X}.");

            return _stereoSourceTexturesReady;
        }
        catch (Exception exception)
        {
            _stereoSourceTexturesReady =
                false;

            logger.LogError(
                $"[PresentHookProbe] Setting stereo sources failed: " +
                $"{exception}");

            return false;
        }
    }

    [HideFromIl2Cpp]
    internal static void ClearStereoSourceTextures(
        ManualLogSource logger)
    {
        try
        {
            var result =
                MavrSetStereoSourceTextures(
                    IntPtr.Zero,
                    IntPtr.Zero);

            logger.LogInfo(
                $"[PresentHookProbe] Clear stereo sources " +
                $"result={result}.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                $"[PresentHookProbe] Clearing stereo sources failed: " +
                $"{exception.Message}");
        }
        finally
        {
            _stereoSourceTexturesReady =
                false;
        }
    }

    [HideFromIl2Cpp]
    internal static bool BlitStereoSourceTexture(
        ManualLogSource logger,
        int eyeIndex,
        IntPtr destinationTexture,
        long destinationDxgiFormat)
    {
        try
        {
            var result =
                MavrBlitStereoSourceTexture(
                    eyeIndex,
                    destinationTexture,
                    destinationDxgiFormat,
                    out var nativeHResult);

            if (result != 0)
            {
                logger.LogError(
                    $"[PresentHookProbe] Stereo blit failed: " +
                    $"eye={eyeIndex}, result={result}, hresult=0x" +
                    $"{unchecked((uint)nativeHResult):X8}.");

                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                $"[PresentHookProbe] Stereo blit threw: {exception}");

            return false;
        }
    }

    [HideFromIl2Cpp]
    internal static bool BlitCapturedBackBuffer(
        ManualLogSource logger,
        IntPtr destinationTexture,
        long destinationDxgiFormat)
    {
        if (destinationTexture == IntPtr.Zero)
        {
            logger.LogError(
                "[PresentHookProbe] Cannot blit to a null texture.");

            return false;
        }

        try
        {
            var result =
                MavrBlitCapturedBackBuffer(
                    destinationTexture,
                    destinationDxgiFormat,
                    out var nativeHResult);

            if (result != 0)
            {
                logger.LogError(
                    $"[PresentHookProbe] Backbuffer blit failed: " +
                    $"result={result}, hresult=0x" +
                    $"{unchecked((uint)nativeHResult):X8}, " +
                    $"format={destinationDxgiFormat}, " +
                    $"destination=0x" +
                    $"{destinationTexture.ToInt64():X}.");

                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                $"[PresentHookProbe] Backbuffer blit threw: " +
                $"{exception}");

            return false;
        }
    }

    [HideFromIl2Cpp]
    internal static bool ClearTexture(
        ManualLogSource logger,
        IntPtr texture,
        long dxgiFormat,
        float red,
        float green,
        float blue,
        float alpha)
    {
        if (texture == IntPtr.Zero)
        {
            logger.LogError(
                "[PresentHookProbe] Cannot clear a null texture.");
            return false;
        }

        try
        {
            var result =
                MavrClearD3D11Texture(
                    texture,
                    dxgiFormat,
                    red,
                    green,
                    blue,
                    alpha,
                    out var createViewHResult);

            if (result != 0)
            {
                logger.LogError(
                    $"[PresentHookProbe] Clear texture failed: " +
                    $"result={result}, hresult=0x" +
                    $"{unchecked((uint)createViewHResult):X8}, " +
                    $"format={dxgiFormat}, " +
                    $"texture=0x{texture.ToInt64():X}.");
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                $"[PresentHookProbe] Clear texture threw: {exception}");
            return false;
        }
    }

    [HideFromIl2Cpp]
    internal static bool SetPresentFrameCallback(
        ManualLogSource logger,
        Action callback)
    {
        lock (CallbackSyncRoot)
        {
            try
            {
                _managedPresentCallback =
                    callback;

                _nativePresentCallback =
                    OnNativePresent;

                var callbackPointer =
                    Marshal.GetFunctionPointerForDelegate(
                        _nativePresentCallback);

                var result =
                    MavrSetPresentFrameCallback(
                        callbackPointer);

                logger.LogInfo(
                    $"[PresentHookProbe] Set frame callback " +
                    $"result={result}, pointer=0x" +
                    $"{callbackPointer.ToInt64():X}.");

                if (result != 0)
                {
                    _managedPresentCallback = null;
                    _nativePresentCallback = null;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    $"[PresentHookProbe] Setting frame callback failed: " +
                    $"{exception}");

                _managedPresentCallback = null;
                _nativePresentCallback = null;
                return false;
            }
        }
    }

    [HideFromIl2Cpp]
    internal static void ClearPresentFrameCallback(
        ManualLogSource logger)
    {
        lock (CallbackSyncRoot)
        {
            try
            {
                var result =
                    MavrSetPresentFrameCallback(
                        IntPtr.Zero);

                logger.LogInfo(
                    $"[PresentHookProbe] Clear frame callback " +
                    $"result={result}.");
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    $"[PresentHookProbe] Clearing frame callback failed: " +
                    $"{exception.Message}");
            }
            finally
            {
                _managedPresentCallback = null;
                _nativePresentCallback = null;
            }
        }
    }

    private static void OnNativePresent()
    {
        try
        {
            _managedPresentCallback?.Invoke();
        }
        catch
        {
            // Never unwind a managed exception through IDXGISwapChain::Present.
        }
    }

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate void
        NativePresentCallbackDelegate();

    [DllImport(
        LibraryName,
        EntryPoint = "MAVR_SetPresentFrameCallback",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int
        MavrSetPresentFrameCallback(
            IntPtr callback);

    [DllImport(
        LibraryName,
        EntryPoint = "MAVR_InstallPresentHook",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int
        MavrInstallPresentHook();

    [DllImport(
        LibraryName,
        EntryPoint = "MAVR_GetCapturedD3D11DeviceInfo",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int
        MavrGetCapturedD3D11DeviceInfo(
            out IntPtr devicePointer,
            out uint adapterLuidLowPart,
            out int adapterLuidHighPart,
            out int featureLevel);

    [DllImport(
        LibraryName,
        EntryPoint = "MAVR_SetInteractionPromptState",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int
        MavrSetInteractionPromptState(
            int visible,
            float leftU,
            float leftV,
            float rightU,
            float rightV);

    [DllImport(
        LibraryName,
        EntryPoint = "MAVR_SetStereoSourceTextures",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int
        MavrSetStereoSourceTextures(
            IntPtr leftTexture,
            IntPtr rightTexture);

    [DllImport(
        LibraryName,
        EntryPoint = "MAVR_BlitStereoSourceTexture",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int
        MavrBlitStereoSourceTexture(
            int eyeIndex,
            IntPtr destinationTexture,
            long destinationDxgiFormat,
            out int nativeHResult);

    [DllImport(
        LibraryName,
        EntryPoint = "MAVR_BlitCapturedBackBuffer",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int
        MavrBlitCapturedBackBuffer(
            IntPtr destinationTexture,
            long destinationDxgiFormat,
            out int nativeHResult);

    [DllImport(
        LibraryName,
        EntryPoint = "MAVR_ClearD3D11Texture",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int
        MavrClearD3D11Texture(
            IntPtr texture,
            long dxgiFormat,
            float red,
            float green,
            float blue,
            float alpha,
            out int createViewHResult);
}

internal readonly record struct UnityD3D11DeviceInfo(
    IntPtr DevicePointer,
    uint AdapterLuidLowPart,
    int AdapterLuidHighPart,
    int FeatureLevel);

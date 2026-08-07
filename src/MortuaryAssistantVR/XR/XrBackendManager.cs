using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;

namespace MortuaryAssistantVR.XR;

internal static class XrBackendManager
{
    private static readonly object SyncRoot = new();
    private static IXrBackend? _backend;
    private static bool _initializationAttempted;
    private static bool _updateObserved;
    private static bool _nativePresentObserved;
    private static ManualLogSource? _logger;

    [HideFromIl2Cpp]
    internal static void Initialize(
        ManualLogSource logger,
        bool attemptStartup)
    {
        lock (SyncRoot)
        {
            _logger =
                logger;

            if (_backend is not null)
            {
                logger.LogInfo(
                    $"[XRBackend] Backend already exists: " +
                    $"name='{_backend.Name}', state={_backend.State}, " +
                    $"status='{_backend.StatusMessage}'");
                return;
            }

            if (_initializationAttempted)
            {
                logger.LogInfo(
                    "[XRBackend] Initialization was already attempted. " +
                    "The backend will not be recreated during scene changes.");
                return;
            }

            _initializationAttempted = true;

            logger.LogInfo(
                "=== XR Backend initialization begin ===");

            try
            {
                _backend =
                    new OpenXrNativeBackend(logger);

                var success =
                    _backend.Initialize(attemptStartup);

                logger.LogInfo(
                    $"[XRBackend] Name='{_backend.Name}', " +
                    $"success={success}, state={_backend.State}, " +
                    $"status='{_backend.StatusMessage}'");

                if (success &&
                    !D3D11PresentHookProbe.SetPresentFrameCallback(
                        logger,
                        OnNativePresentFrame))
                {
                    logger.LogError(
                        "[XRBackend] Native Present frame callback " +
                        "could not be registered.");
                }
            }
            catch (Exception exception)
            {
                logger.LogError(
                    $"[XRBackend] Initialization failed: {exception}");

                try
                {
                    _backend?.Dispose();
                }
                catch
                {
                    // Preserve the original initialization exception.
                }

                _backend = null;
            }
            finally
            {
                logger.LogInfo(
                    "=== XR Backend initialization end ===");
            }
        }
    }

    [HideFromIl2Cpp]
    internal static void Update(
        ManualLogSource logger)
    {
        lock (SyncRoot)
        {
            if (!_updateObserved)
            {
                _updateObserved = true;
                logger.LogInfo(
                    "[XRBackend] First managed update poll observed.");
            }

            if (_backend is OpenXrNativeBackend nativeBackend)
            {
                nativeBackend.PollUnityGraphicsDevice();
            }
        }
    }

    private static void OnNativePresentFrame()
    {
        lock (SyncRoot)
        {
            if (!_nativePresentObserved)
            {
                _nativePresentObserved =
                    true;

                _logger?.LogInfo(
                    "[XRBackend] First native Present frame callback " +
                    "observed.");
            }

            if (_backend is OpenXrNativeBackend nativeBackend)
            {
                nativeBackend.PollUnityGraphicsDevice();
            }
        }
    }

    [HideFromIl2Cpp]
    internal static bool TryGetLatestHeadPose(
        out OpenXrHeadPose headPose)
    {
        lock (SyncRoot)
        {
            if (_backend is OpenXrNativeBackend nativeBackend)
            {
                return nativeBackend.TryGetLatestHeadPose(
                    out headPose);
            }

            headPose =
                default;

            return false;
        }
    }

    [HideFromIl2Cpp]
    internal static void LogState(
        ManualLogSource logger)
    {
        lock (SyncRoot)
        {
            if (_backend is null)
            {
                logger.LogInfo(
                    "[XRBackend] No active backend instance. " +
                    $"initializationAttempted={_initializationAttempted}");
                return;
            }

            logger.LogInfo(
                $"[XRBackend] Current state: " +
                $"name='{_backend.Name}', " +
                $"state={_backend.State}, " +
                $"status='{_backend.StatusMessage}'");
        }
    }

    [HideFromIl2Cpp]
    internal static void Shutdown(
        ManualLogSource logger)
    {
        lock (SyncRoot)
        {
            if (_backend is null)
            {
                return;
            }

            logger.LogInfo(
                "Shutting down XR backend.");

            D3D11PresentHookProbe.ClearPresentFrameCallback(
                logger);

            try
            {
                _backend.Dispose();
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    $"XR backend shutdown reported: " +
                    $"{exception.Message}");
            }
            finally
            {
                _backend = null;
                _logger = null;
            }
        }
    }
}

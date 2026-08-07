using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;

namespace MortuaryAssistantVR.XR;

internal sealed class OpenXrNativeBackend : IXrBackend
{
    private const int XrSuccess = 0;
    private const int XrEventUnavailable = 4;

    private const int XrTypeInstanceCreateInfo = 3;
    private const int XrTypeViewLocateInfo = 6;
    private const int XrTypeView = 7;
    private const int XrTypeSystemGetInfo = 4;
    private const int XrTypeSessionCreateInfo = 8;
    private const int XrTypeSwapchainCreateInfo = 9;
    private const int XrTypeSessionBeginInfo = 10;
    private const int XrTypeViewState = 11;
    private const int XrTypeFrameEndInfo = 12;
    private const int XrTypeEventDataBuffer = 16;
    private const int XrTypeEventDataSessionStateChanged = 18;
    private const int XrTypeFrameWaitInfo = 33;
    private const int XrTypeCompositionLayerProjection = 35;
    private const int XrTypeCompositionLayerQuad = 36;
    private const int XrTypeReferenceSpaceCreateInfo = 37;
    private const int XrTypeViewConfigurationView = 41;
    private const int XrTypeFrameState = 44;
    private const int XrTypeFrameBeginInfo = 46;
    private const int XrTypeCompositionLayerProjectionView = 48;
    private const int XrTypeSwapchainImageAcquireInfo = 55;
    private const int XrTypeSwapchainImageWaitInfo = 56;
    private const int XrTypeSwapchainImageReleaseInfo = 57;
    private const int XrTypeGraphicsBindingD3D11Khr = 1000027000;
    private const int XrTypeSwapchainImageD3D11Khr = 1000027001;
    private const int XrTypeGraphicsRequirementsD3D11Khr = 1000027002;

    private const int XrFormFactorHeadMountedDisplay = 1;
    private const int XrViewConfigurationTypePrimaryStereo = 2;
    private const int XrReferenceSpaceTypeView = 1;
    private const int XrReferenceSpaceTypeLocal = 2;
    private const int XrEnvironmentBlendModeOpaque = 1;
    private const int XrEyeVisibilityBoth = 0;
    private const int XrEyeVisibilityLeft = 1;
    private const int XrEyeVisibilityRight = 2;

    private const ulong XrCompositionLayerBlendTextureSourceAlphaBit =
        0x00000002;

    private const ulong XrCompositionLayerUnpremultipliedAlphaBit =
        0x00000004;

    private const float CinemaQuadDistanceMetres = 2.0f;
    private const float CinemaQuadWidthMetres = 2.4f;
    private const float CinemaQuadHeightMetres = 1.35f;

    private const uint ToolUiSwapchainWidth = 2560;
    private const uint ToolUiSwapchainHeight = 1440;

    // CircleRet occupies the central portion of the game's 16:9 UI. The
    // remaining edge pixels can contain environment-colored artifacts even
    // though the menu itself is correct. Crop only at OpenXR composition
    // time so Unity input and the native cursor keep using the original
    // 2560x1440 coordinate system.
    // v0.31.9 proved that cropping helps, but a large residual artifact
    // remained on the right. CircleRet itself lives close to screen centre,
    // so use a tighter asymmetric crop that removes substantially more of
    // the right-hand side while keeping generous room around the radial
    // slots and cursor.
    private const int ToolUiCropX = 620;
    private const int ToolUiCropY = 255;
    private const int ToolUiCropWidth = 1220;
    private const int ToolUiCropHeight = 900;

    // Eye-specific VIEW-space quads. Each eye gets the same source image,
    // centered approximately on that eye. This removes binocular disparity
    // from the menu so it behaves like a HUD instead of a panel at finite
    // world depth.
    private const float ToolUiHudDistanceMetres = 1.00f;
    private const float ToolUiHudHalfIpdMetres = 0.032f;
    private const float ToolUiHudWidthMetres = 1.09f;
    private const float ToolUiHudHeightMetres =
        ToolUiHudWidthMetres *
        ToolUiCropHeight /
        ToolUiCropWidth;

    private const ulong XrSwapchainUsageColorAttachmentBit = 0x00000001;
    private const ulong XrSwapchainUsageSampledBit = 0x00000020;

    private const long DxgiFormatR8G8B8A8Unorm = 28;
    private const long DxgiFormatR8G8B8A8UnormSrgb = 29;
    private const long DxgiFormatB8G8R8A8Unorm = 87;
    private const long DxgiFormatB8G8R8A8UnormSrgb = 91;

    private const int XrSessionStateUnknown = 0;
    private const int XrSessionStateIdle = 1;
    private const int XrSessionStateReady = 2;
    private const int XrSessionStateSynchronized = 3;
    private const int XrSessionStateVisible = 4;
    private const int XrSessionStateFocused = 5;
    private const int XrSessionStateStopping = 6;
    private const int XrSessionStateLossPending = 7;
    private const int XrSessionStateExiting = 8;

    private const int XrMaxEventDataSize = 4000;
    private const int XrMaxApplicationNameSize = 128;
    private const int XrMaxEngineNameSize = 128;

    private const string XrKhrD3D11Enable =
        "XR_KHR_D3D11_enable";

    private readonly ManualLogSource _logger;

    private IntPtr _loaderHandle;
    private IntPtr _xrGetInstanceProcAddrAddress;

    private XrGetInstanceProcAddrDelegate? _xrGetInstanceProcAddr;
    private XrCreateInstanceDelegate? _xrCreateInstance;
    private XrDestroyInstanceDelegate? _xrDestroyInstance;
    private XrGetSystemDelegate? _xrGetSystem;
    private XrCreateSessionDelegate? _xrCreateSession;
    private XrDestroySessionDelegate? _xrDestroySession;
    private XrPollEventDelegate? _xrPollEvent;
    private XrBeginSessionDelegate? _xrBeginSession;
    private XrEndSessionDelegate? _xrEndSession;
    private XrCreateReferenceSpaceDelegate? _xrCreateReferenceSpace;
    private XrDestroySpaceDelegate? _xrDestroySpace;
    private XrWaitFrameDelegate? _xrWaitFrame;
    private XrBeginFrameDelegate? _xrBeginFrame;
    private XrEndFrameDelegate? _xrEndFrame;
    private XrEnumerateViewConfigurationViewsDelegate?
        _xrEnumerateViewConfigurationViews;
    private XrEnumerateSwapchainFormatsDelegate?
        _xrEnumerateSwapchainFormats;
    private XrCreateSwapchainDelegate? _xrCreateSwapchain;
    private XrDestroySwapchainDelegate? _xrDestroySwapchain;
    private XrEnumerateSwapchainImagesDelegate?
        _xrEnumerateSwapchainImages;
    private XrAcquireSwapchainImageDelegate?
        _xrAcquireSwapchainImage;
    private XrWaitSwapchainImageDelegate?
        _xrWaitSwapchainImage;
    private XrReleaseSwapchainImageDelegate?
        _xrReleaseSwapchainImage;
    private XrLocateViewsDelegate? _xrLocateViews;
    private XrGetD3D11GraphicsRequirementsKhrDelegate?
        _xrGetD3D11GraphicsRequirementsKhr;

    private ulong _instance;
    private ulong _systemId;
    private ulong _session;
    private ulong _localSpace;
    private ulong _viewSpace;
    private ulong _toolUiSwapchain;
    private OpenXrSwapchainImageSet? _toolUiSwapchainImageSet;
    private readonly List<ulong> _colorSwapchains = new();
    private readonly List<OpenXrSwapchainImageSet> _swapchainImageSets = new();
    private readonly List<XrViewConfigurationView> _viewConfigurationViews = new();
    private long _colorSwapchainFormat;
    private XrGraphicsRequirementsD3D11Khr _graphicsRequirements;
    private bool _disposed;
    private bool _sessionRunning;
    private bool _exitRequested;
    private int _sessionState = XrSessionStateUnknown;
    private int _presentHookPollCount;
    private int _frameCount;
    private long _headPoseSequence;
    private OpenXrHeadPose _latestHeadPose;
    private bool _hasHeadPose;
    private OpenXrHeadPose _latestRenderedHeadPose;
    private bool _hasRenderedHeadPose;
    private long _lastSubmittedRenderPoseSequence;
    private bool _toolUiDiagnosticDumped;

    internal OpenXrNativeBackend(ManualLogSource logger)
    {
        _logger = logger;
    }

    public string Name => "OpenXR Native";

    public XrBackendState State { get; private set; } =
        XrBackendState.NotInitialized;

    public string StatusMessage { get; private set; } =
        "Backend has not been initialized.";

    [HideFromIl2Cpp]
    public bool Initialize(bool attemptStartup)
    {
        ThrowIfDisposed();

        var loaderPath = FindLoader();

        if (loaderPath is null)
        {
            State = XrBackendState.LoaderUnavailable;
            StatusMessage =
                "No application-local openxr_loader.dll was found.";

            _logger.LogWarning($"[XRBackend] {StatusMessage}");
            return false;
        }

        _logger.LogInfo(
            $"[XRBackend] Loading OpenXR loader from '{loaderPath}'.");

        if (!NativeLibrary.TryLoad(loaderPath, out _loaderHandle))
        {
            State = XrBackendState.Failed;
            StatusMessage =
                "NativeLibrary.TryLoad failed for openxr_loader.dll.";

            _logger.LogError($"[XRBackend] {StatusMessage}");
            return false;
        }

        State = XrBackendState.LoaderLoaded;
        StatusMessage = "OpenXR loader loaded.";

        if (!TryResolveLoaderExports())
        {
            return false;
        }

        State = XrBackendState.EntryPointResolved;
        StatusMessage =
            "OpenXR core loader entry points resolved.";

        if (!attemptStartup)
        {
            State = XrBackendState.StartupDisabled;
            StatusMessage =
                "OpenXR loader is ready; startup is disabled by config.";

            return true;
        }

        return InitializeOpenXrSystem();
    }

    [HideFromIl2Cpp]
    private bool TryResolveLoaderExports()
    {
        if (!NativeLibrary.TryGetExport(
                _loaderHandle,
                "xrGetInstanceProcAddr",
                out _xrGetInstanceProcAddrAddress))
        {
            return Fail(
                "xrGetInstanceProcAddr was not exported by the loader.");
        }

        if (!NativeLibrary.TryGetExport(
                _loaderHandle,
                "xrCreateInstance",
                out var createInstanceAddress))
        {
            return Fail(
                "xrCreateInstance was not exported by the loader.");
        }

        if (!NativeLibrary.TryGetExport(
                _loaderHandle,
                "xrDestroyInstance",
                out var destroyInstanceAddress))
        {
            return Fail(
                "xrDestroyInstance was not exported by the loader.");
        }

        _xrGetInstanceProcAddr =
            Marshal.GetDelegateForFunctionPointer<
                XrGetInstanceProcAddrDelegate>(
                _xrGetInstanceProcAddrAddress);

        _xrCreateInstance =
            Marshal.GetDelegateForFunctionPointer<
                XrCreateInstanceDelegate>(
                createInstanceAddress);

        _xrDestroyInstance =
            Marshal.GetDelegateForFunctionPointer<
                XrDestroyInstanceDelegate>(
                destroyInstanceAddress);

        _logger.LogInfo(
            $"[XRBackend] xrGetInstanceProcAddr=0x" +
            $"{_xrGetInstanceProcAddrAddress.ToInt64():X}");

        _logger.LogInfo(
            $"[XRBackend] xrCreateInstance=0x" +
            $"{createInstanceAddress.ToInt64():X}");

        _logger.LogInfo(
            $"[XRBackend] xrDestroyInstance=0x" +
            $"{destroyInstanceAddress.ToInt64():X}");

        return true;
    }

    [HideFromIl2Cpp]
    private bool InitializeOpenXrSystem()
    {
        if (!CreateInstance())
        {
            return false;
        }

        if (!ResolveInstanceFunctions())
        {
            return false;
        }

        if (!GetHeadMountedDisplaySystem())
        {
            return false;
        }

        return GetD3D11GraphicsRequirements();
    }

    [HideFromIl2Cpp]
    private bool CreateInstance()
    {
        if (_xrCreateInstance is null)
        {
            return Fail(
                "xrCreateInstance delegate is unavailable.");
        }

        var extensionName =
            Marshal.StringToCoTaskMemUTF8(XrKhrD3D11Enable);

        var extensionNames =
            Marshal.AllocHGlobal(IntPtr.Size);

        try
        {
            Marshal.WriteIntPtr(
                extensionNames,
                extensionName);

            var createInfo = new XrInstanceCreateInfo
            {
                Type = XrTypeInstanceCreateInfo,
                Next = IntPtr.Zero,
                CreateFlags = 0,
                ApplicationInfo = new XrApplicationInfo
                {
                    ApplicationName =
                        CreateFixedUtf8(
                            "The Mortuary Assistant VR",
                            XrMaxApplicationNameSize),

                    ApplicationVersion =
                        PackVersion(0, 10, 0),

                    EngineName =
                        CreateFixedUtf8(
                            "Unity/BepInEx",
                            XrMaxEngineNameSize),

                    EngineVersion =
                        PackVersion(2021, 2, 4),

                    ApiVersion =
                        MakeXrVersion(1, 0, 0)
                },
                EnabledApiLayerCount = 0,
                EnabledApiLayerNames = IntPtr.Zero,
                EnabledExtensionCount = 1,
                EnabledExtensionNames = extensionNames
            };

            _logger.LogInfo(
                "[XRBackend] Calling xrCreateInstance with " +
                $"extension '{XrKhrD3D11Enable}'.");

            var result =
                _xrCreateInstance(
                    ref createInfo,
                    out _instance);

            _logger.LogInfo(
                $"[XRBackend] xrCreateInstance result={result}, " +
                $"instance=0x{_instance:X}");

            if (result != XrSuccess || _instance == 0)
            {
                State =
                    XrBackendState.InstanceCreationFailed;

                StatusMessage =
                    $"xrCreateInstance failed with XrResult {result}.";

                _instance = 0;
                return false;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(extensionNames);
            Marshal.FreeCoTaskMem(extensionName);
        }

        State = XrBackendState.InstanceCreated;
        StatusMessage =
            "OpenXR instance created successfully.";

        return true;
    }

    [HideFromIl2Cpp]
    private bool ResolveInstanceFunctions()
    {
        if (_xrGetInstanceProcAddr is null)
        {
            return Fail(
                "xrGetInstanceProcAddr delegate is unavailable.");
        }

        if (!TryResolveInstanceFunction(
                "xrGetSystem",
                out var getSystemAddress))
        {
            return false;
        }

        _xrGetSystem =
            Marshal.GetDelegateForFunctionPointer<
                XrGetSystemDelegate>(
                getSystemAddress);

        if (!TryResolveInstanceFunction(
                "xrCreateSession",
                out var createSessionAddress))
        {
            return false;
        }

        _xrCreateSession =
            Marshal.GetDelegateForFunctionPointer<
                XrCreateSessionDelegate>(
                createSessionAddress);

        if (!TryResolveInstanceFunction(
                "xrDestroySession",
                out var destroySessionAddress))
        {
            return false;
        }

        _xrDestroySession =
            Marshal.GetDelegateForFunctionPointer<
                XrDestroySessionDelegate>(
                destroySessionAddress);

        if (!TryResolveInstanceFunction(
                "xrPollEvent",
                out var pollEventAddress))
        {
            return false;
        }

        _xrPollEvent =
            Marshal.GetDelegateForFunctionPointer<
                XrPollEventDelegate>(
                pollEventAddress);

        if (!TryResolveInstanceFunction(
                "xrBeginSession",
                out var beginSessionAddress))
        {
            return false;
        }

        _xrBeginSession =
            Marshal.GetDelegateForFunctionPointer<
                XrBeginSessionDelegate>(
                beginSessionAddress);

        if (!TryResolveInstanceFunction(
                "xrEndSession",
                out var endSessionAddress))
        {
            return false;
        }

        _xrEndSession =
            Marshal.GetDelegateForFunctionPointer<
                XrEndSessionDelegate>(
                endSessionAddress);

        if (!TryResolveInstanceFunction(
                "xrCreateReferenceSpace",
                out var createReferenceSpaceAddress))
        {
            return false;
        }

        _xrCreateReferenceSpace =
            Marshal.GetDelegateForFunctionPointer<
                XrCreateReferenceSpaceDelegate>(
                createReferenceSpaceAddress);

        if (!TryResolveInstanceFunction(
                "xrDestroySpace",
                out var destroySpaceAddress))
        {
            return false;
        }

        _xrDestroySpace =
            Marshal.GetDelegateForFunctionPointer<
                XrDestroySpaceDelegate>(
                destroySpaceAddress);

        if (!TryResolveInstanceFunction(
                "xrWaitFrame",
                out var waitFrameAddress))
        {
            return false;
        }

        _xrWaitFrame =
            Marshal.GetDelegateForFunctionPointer<
                XrWaitFrameDelegate>(
                waitFrameAddress);

        if (!TryResolveInstanceFunction(
                "xrBeginFrame",
                out var beginFrameAddress))
        {
            return false;
        }

        _xrBeginFrame =
            Marshal.GetDelegateForFunctionPointer<
                XrBeginFrameDelegate>(
                beginFrameAddress);

        if (!TryResolveInstanceFunction(
                "xrEndFrame",
                out var endFrameAddress))
        {
            return false;
        }

        _xrEndFrame =
            Marshal.GetDelegateForFunctionPointer<
                XrEndFrameDelegate>(
                endFrameAddress);

        if (!TryResolveInstanceFunction(
                "xrEnumerateViewConfigurationViews",
                out var enumerateViewsAddress))
        {
            return false;
        }

        _xrEnumerateViewConfigurationViews =
            Marshal.GetDelegateForFunctionPointer<
                XrEnumerateViewConfigurationViewsDelegate>(
                enumerateViewsAddress);

        if (!TryResolveInstanceFunction(
                "xrEnumerateSwapchainFormats",
                out var enumerateFormatsAddress))
        {
            return false;
        }

        _xrEnumerateSwapchainFormats =
            Marshal.GetDelegateForFunctionPointer<
                XrEnumerateSwapchainFormatsDelegate>(
                enumerateFormatsAddress);

        if (!TryResolveInstanceFunction(
                "xrCreateSwapchain",
                out var createSwapchainAddress))
        {
            return false;
        }

        _xrCreateSwapchain =
            Marshal.GetDelegateForFunctionPointer<
                XrCreateSwapchainDelegate>(
                createSwapchainAddress);

        if (!TryResolveInstanceFunction(
                "xrDestroySwapchain",
                out var destroySwapchainAddress))
        {
            return false;
        }

        _xrDestroySwapchain =
            Marshal.GetDelegateForFunctionPointer<
                XrDestroySwapchainDelegate>(
                destroySwapchainAddress);

        if (!TryResolveInstanceFunction(
                "xrEnumerateSwapchainImages",
                out var enumerateSwapchainImagesAddress))
        {
            return false;
        }

        _xrEnumerateSwapchainImages =
            Marshal.GetDelegateForFunctionPointer<
                XrEnumerateSwapchainImagesDelegate>(
                enumerateSwapchainImagesAddress);

        if (!TryResolveInstanceFunction(
                "xrAcquireSwapchainImage",
                out var acquireSwapchainImageAddress))
        {
            return false;
        }

        _xrAcquireSwapchainImage =
            Marshal.GetDelegateForFunctionPointer<
                XrAcquireSwapchainImageDelegate>(
                acquireSwapchainImageAddress);

        if (!TryResolveInstanceFunction(
                "xrWaitSwapchainImage",
                out var waitSwapchainImageAddress))
        {
            return false;
        }

        _xrWaitSwapchainImage =
            Marshal.GetDelegateForFunctionPointer<
                XrWaitSwapchainImageDelegate>(
                waitSwapchainImageAddress);

        if (!TryResolveInstanceFunction(
                "xrReleaseSwapchainImage",
                out var releaseSwapchainImageAddress))
        {
            return false;
        }

        _xrReleaseSwapchainImage =
            Marshal.GetDelegateForFunctionPointer<
                XrReleaseSwapchainImageDelegate>(
                releaseSwapchainImageAddress);

        if (!TryResolveInstanceFunction(
                "xrLocateViews",
                out var locateViewsAddress))
        {
            return false;
        }

        _xrLocateViews =
            Marshal.GetDelegateForFunctionPointer<
                XrLocateViewsDelegate>(
                locateViewsAddress);

        if (!TryResolveInstanceFunction(
                "xrGetD3D11GraphicsRequirementsKHR",
                out var graphicsRequirementsAddress))
        {
            return false;
        }

        _xrGetD3D11GraphicsRequirementsKhr =
            Marshal.GetDelegateForFunctionPointer<
                XrGetD3D11GraphicsRequirementsKhrDelegate>(
                graphicsRequirementsAddress);

        return true;
    }

    [HideFromIl2Cpp]
    private bool TryResolveInstanceFunction(
        string name,
        out IntPtr address)
    {
        address = IntPtr.Zero;

        if (_xrGetInstanceProcAddr is null)
        {
            return Fail(
                "xrGetInstanceProcAddr delegate is unavailable.");
        }

        var result =
            _xrGetInstanceProcAddr(
                _instance,
                name,
                out address);

        _logger.LogInfo(
            $"[XRBackend] Resolve {name} result={result}, " +
            $"address=0x{address.ToInt64():X}");

        if (result != XrSuccess ||
            address == IntPtr.Zero)
        {
            return Fail(
                $"Could not resolve {name}; XrResult {result}.");
        }

        return true;
    }

    [HideFromIl2Cpp]
    private bool GetHeadMountedDisplaySystem()
    {
        if (_xrGetSystem is null)
        {
            return Fail(
                "xrGetSystem delegate is unavailable.");
        }

        var getInfo = new XrSystemGetInfo
        {
            Type = XrTypeSystemGetInfo,
            Next = IntPtr.Zero,
            FormFactor =
                XrFormFactorHeadMountedDisplay
        };

        _logger.LogInfo(
            "[XRBackend] Calling xrGetSystem for " +
            "XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY.");

        var result =
            _xrGetSystem(
                _instance,
                ref getInfo,
                out _systemId);

        _logger.LogInfo(
            $"[XRBackend] xrGetSystem result={result}, " +
            $"systemId=0x{_systemId:X}");

        if (result != XrSuccess ||
            _systemId == 0)
        {
            _systemId = 0;

            return Fail(
                $"xrGetSystem failed with XrResult {result}.");
        }

        StatusMessage =
            $"OpenXR HMD system found; systemId=0x{_systemId:X}.";

        return true;
    }

    [HideFromIl2Cpp]
    private bool GetD3D11GraphicsRequirements()
    {
        if (_xrGetD3D11GraphicsRequirementsKhr is null)
        {
            return Fail(
                "xrGetD3D11GraphicsRequirementsKHR delegate " +
                "is unavailable.");
        }

        _graphicsRequirements =
            new XrGraphicsRequirementsD3D11Khr
            {
                Type =
                    XrTypeGraphicsRequirementsD3D11Khr,

                Next = IntPtr.Zero,
                AdapterLuid = default,
                MinFeatureLevel = 0
            };

        _logger.LogInfo(
            "[XRBackend] Calling " +
            "xrGetD3D11GraphicsRequirementsKHR.");

        var result =
            _xrGetD3D11GraphicsRequirementsKhr(
                _instance,
                _systemId,
                ref _graphicsRequirements);

        var luid =
            FormatLuid(
                _graphicsRequirements.AdapterLuid);

        var featureLevel =
            FormatD3DFeatureLevel(
                _graphicsRequirements.MinFeatureLevel);

        _logger.LogInfo(
            $"[XRBackend] " +
            $"xrGetD3D11GraphicsRequirementsKHR result={result}, " +
            $"adapterLuid={luid}, " +
            $"minFeatureLevel={featureLevel}");

        if (result != XrSuccess)
        {
            return Fail(
                "xrGetD3D11GraphicsRequirementsKHR failed " +
                $"with XrResult {result}.");
        }

        State =
            XrBackendState.GraphicsRequirementsReady;

        StatusMessage =
            "OpenXR D3D11 graphics requirements are ready; " +
            $"adapterLuid={luid}, " +
            $"minFeatureLevel={featureLevel}.";

        _logger.LogInfo(
            $"[XRBackend] {StatusMessage}");

        return InstallPresentHookProbe();
    }

    [HideFromIl2Cpp]
    private bool InstallPresentHookProbe()
    {
        _logger.LogInfo(
            "[XRBackend] Installing D3D11 Present-hook probe.");

        if (!D3D11PresentHookProbe.Install(_logger))
        {
            return Fail(
                "The D3D11 Present-hook probe could not be installed.");
        }

        State =
            XrBackendState.WaitingForUnityGraphicsDevice;

        StatusMessage =
            "D3D11 Present hook installed; waiting for Unity's " +
            "first presented frame.";

        _logger.LogInfo(
            $"[XRBackend] {StatusMessage}");

        return true;
    }

    [HideFromIl2Cpp]
    internal void PollUnityGraphicsDevice()
    {
        if (_disposed)
        {
            return;
        }

        if (State == XrBackendState.WaitingForUnityGraphicsDevice)
        {
            PollForUnityGraphicsDevice();
        }

        if (_session != 0)
        {
            PollOpenXrEvents();

            if (_sessionRunning &&
                !_exitRequested)
            {
                RunEmptyFrame();
            }
        }
    }

    [HideFromIl2Cpp]
    private void PollForUnityGraphicsDevice()
    {
        _presentHookPollCount++;

        var shouldLogAttempt =
            _presentHookPollCount <= 5 ||
            _presentHookPollCount % 120 == 0;

        if (shouldLogAttempt)
        {
            _logger.LogInfo(
                $"[XRBackend] Present-hook poll attempt " +
                $"{_presentHookPollCount}.");
        }

        if (!D3D11PresentHookProbe.TryGetDevice(
                _logger,
                out var deviceInfo,
                shouldLogAttempt))
        {
            return;
        }

        var runtimeLuid =
            FormatLuid(
                _graphicsRequirements.AdapterLuid);

        var unityLuid =
            FormatLuid(
                new Luid
                {
                    LowPart =
                        deviceInfo.AdapterLuidLowPart,

                    HighPart =
                        deviceInfo.AdapterLuidHighPart
                });

        var adapterMatches =
            _graphicsRequirements.AdapterLuid.LowPart ==
                deviceInfo.AdapterLuidLowPart &&
            _graphicsRequirements.AdapterLuid.HighPart ==
                deviceInfo.AdapterLuidHighPart;

        var featureLevelMatches =
            deviceInfo.FeatureLevel >=
                _graphicsRequirements.MinFeatureLevel;

        _logger.LogInfo(
            $"[XRBackend] Captured Unity D3D11 device=0x" +
            $"{deviceInfo.DevicePointer.ToInt64():X}, " +
            $"adapterLuid={unityLuid}, " +
            $"featureLevel=" +
            $"{FormatD3DFeatureLevel(deviceInfo.FeatureLevel)}");

        _logger.LogInfo(
            $"[XRBackend] Present-hook comparison: " +
            $"adapterMatches={adapterMatches}, " +
            $"featureLevelMatches={featureLevelMatches}, " +
            $"runtimeAdapter={runtimeLuid}, " +
            $"unityAdapter={unityLuid}");

        if (!adapterMatches)
        {
            Fail(
                "The captured Unity D3D11 device uses a different " +
                "GPU adapter than OpenXR requires.");

            return;
        }

        if (!featureLevelMatches)
        {
            Fail(
                "The captured Unity D3D11 device feature level is " +
                "below the OpenXR runtime requirement.");

            return;
        }

        State =
            XrBackendState.UnityGraphicsDeviceReady;

        StatusMessage =
            "Unity's captured D3D11 device matches the OpenXR " +
            "graphics requirements.";

        _logger.LogInfo(
            $"[XRBackend] {StatusMessage}");

        CreateSession(
            deviceInfo.DevicePointer);
    }

    [HideFromIl2Cpp]
    private bool CreateSession(
        IntPtr d3d11Device)
    {
        if (_xrCreateSession is null)
        {
            return Fail(
                "xrCreateSession delegate is unavailable.");
        }

        if (d3d11Device == IntPtr.Zero)
        {
            return Fail(
                "Cannot create an OpenXR session with a null " +
                "D3D11 device pointer.");
        }

        var binding =
            new XrGraphicsBindingD3D11Khr
            {
                Type =
                    XrTypeGraphicsBindingD3D11Khr,

                Next =
                    IntPtr.Zero,

                Device =
                    d3d11Device
            };

        var bindingPointer =
            Marshal.AllocHGlobal(
                Marshal.SizeOf<XrGraphicsBindingD3D11Khr>());

        try
        {
            Marshal.StructureToPtr(
                binding,
                bindingPointer,
                false);

            var createInfo =
                new XrSessionCreateInfo
                {
                    Type =
                        XrTypeSessionCreateInfo,

                    Next =
                        bindingPointer,

                    CreateFlags =
                        0,

                    SystemId =
                        _systemId
                };

            _logger.LogInfo(
                "[XRBackend] Calling xrCreateSession with " +
                $"D3D11 device=0x{d3d11Device.ToInt64():X}.");

            var result =
                _xrCreateSession(
                    _instance,
                    ref createInfo,
                    out _session);

            _logger.LogInfo(
                $"[XRBackend] xrCreateSession result={result}, " +
                $"session=0x{_session:X}.");

            if (result != XrSuccess ||
                _session == 0)
            {
                State =
                    XrBackendState.SessionCreationFailed;

                StatusMessage =
                    $"xrCreateSession failed with XrResult {result}.";

                _session = 0;
                return false;
            }

            State =
                XrBackendState.SessionCreated;

            StatusMessage =
                "OpenXR D3D11 session created successfully.";

            _logger.LogInfo(
                $"[XRBackend] {StatusMessage}");

            if (!CreateLocalReferenceSpace())
            {
                return false;
            }

            if (!CreateViewReferenceSpace())
            {
                return false;
            }

            if (!CreateStereoColorSwapchains())
            {
                return false;
            }

            if (!CreateToolUiSwapchain())
            {
                return false;
            }

            State =
                XrBackendState.WaitingForSessionReady;

            StatusMessage =
                "OpenXR session and LOCAL reference space are ready; " +
                "waiting for XR_SESSION_STATE_READY.";

            _logger.LogInfo(
                $"[XRBackend] {StatusMessage}");

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(
                bindingPointer);
        }
    }

    [HideFromIl2Cpp]
    private bool CreateLocalReferenceSpace()
    {
        if (_xrCreateReferenceSpace is null)
        {
            return Fail(
                "xrCreateReferenceSpace delegate is unavailable.");
        }

        var createInfo =
            new XrReferenceSpaceCreateInfo
            {
                Type =
                    XrTypeReferenceSpaceCreateInfo,

                Next =
                    IntPtr.Zero,

                ReferenceSpaceType =
                    XrReferenceSpaceTypeLocal,

                PoseInReferenceSpace =
                    XrPosef.Identity
            };

        _logger.LogInfo(
            "[XRBackend] Calling xrCreateReferenceSpace " +
            "for XR_REFERENCE_SPACE_TYPE_LOCAL.");

        var result =
            _xrCreateReferenceSpace(
                _session,
                ref createInfo,
                out _localSpace);

        _logger.LogInfo(
            $"[XRBackend] xrCreateReferenceSpace result={result}, " +
            $"space=0x{_localSpace:X}.");

        if (result != XrSuccess ||
            _localSpace == 0)
        {
            _localSpace = 0;

            return Fail(
                $"xrCreateReferenceSpace failed with XrResult {result}.");
        }

        State =
            XrBackendState.ReferenceSpaceCreated;

        StatusMessage =
            "OpenXR LOCAL reference space created successfully.";

        return true;
    }

    [HideFromIl2Cpp]
    private bool CreateViewReferenceSpace()
    {
        if (_xrCreateReferenceSpace is null)
        {
            return Fail(
                "xrCreateReferenceSpace delegate is unavailable.");
        }

        var createInfo =
            new XrReferenceSpaceCreateInfo
            {
                Type =
                    XrTypeReferenceSpaceCreateInfo,

                Next =
                    IntPtr.Zero,

                ReferenceSpaceType =
                    XrReferenceSpaceTypeView,

                PoseInReferenceSpace =
                    XrPosef.Identity
            };

        var result =
            _xrCreateReferenceSpace(
                _session,
                ref createInfo,
                out _viewSpace);

        _logger.LogInfo(
            $"[XRBackend] xrCreateReferenceSpace VIEW result={result}, " +
            $"space=0x{_viewSpace:X}.");

        if (result != XrSuccess ||
            _viewSpace == 0)
        {
            _viewSpace =
                0;

            return Fail(
                $"xrCreateReferenceSpace VIEW failed with XrResult {result}.");
        }

        return true;
    }

    [HideFromIl2Cpp]
    private bool CreateStereoColorSwapchains()
    {
        if (_xrEnumerateViewConfigurationViews is null ||
            _xrEnumerateSwapchainFormats is null ||
            _xrCreateSwapchain is null)
        {
            return Fail(
                "OpenXR swapchain delegates are unavailable.");
        }

        var countResult =
            _xrEnumerateViewConfigurationViews(
                _instance,
                _systemId,
                XrViewConfigurationTypePrimaryStereo,
                0,
                out var viewCount,
                IntPtr.Zero);

        _logger.LogInfo(
            $"[XRBackend] View configuration count result={countResult}, " +
            $"count={viewCount}.");

        if (countResult != XrSuccess ||
            viewCount == 0)
        {
            return Fail(
                "Could not enumerate PRIMARY_STEREO view configuration.");
        }

        var viewSize =
            Marshal.SizeOf<XrViewConfigurationView>();

        var viewsPointer =
            Marshal.AllocHGlobal(
                checked((int)viewCount * viewSize));

        try
        {
            for (var index = 0;
                 index < viewCount;
                 index++)
            {
                var view =
                    new XrViewConfigurationView
                    {
                        Type =
                            XrTypeViewConfigurationView,

                        Next =
                            IntPtr.Zero
                    };

                Marshal.StructureToPtr(
                    view,
                    IntPtr.Add(
                        viewsPointer,
                        index * viewSize),
                    false);
            }

            var viewsResult =
                _xrEnumerateViewConfigurationViews(
                    _instance,
                    _systemId,
                    XrViewConfigurationTypePrimaryStereo,
                    viewCount,
                    out var writtenViewCount,
                    viewsPointer);

            _logger.LogInfo(
                $"[XRBackend] View configuration query result={viewsResult}, " +
                $"written={writtenViewCount}.");

            if (viewsResult != XrSuccess ||
                writtenViewCount != viewCount)
            {
                return Fail(
                    "OpenXR did not return the expected stereo views.");
            }

            _viewConfigurationViews.Clear();

            for (var index = 0;
                 index < writtenViewCount;
                 index++)
            {
                var view =
                    Marshal.PtrToStructure<XrViewConfigurationView>(
                        IntPtr.Add(
                            viewsPointer,
                            index * viewSize));

                _viewConfigurationViews.Add(
                    view);

                _logger.LogInfo(
                    $"[XRBackend] View[{index}]: " +
                    $"recommended={view.RecommendedImageRectWidth}x" +
                    $"{view.RecommendedImageRectHeight}, " +
                    $"max={view.MaxImageRectWidth}x" +
                    $"{view.MaxImageRectHeight}, " +
                    $"samples={view.RecommendedSwapchainSampleCount}.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(
                viewsPointer);
        }

        if (!ChooseSwapchainFormat())
        {
            return false;
        }

        DestroyColorSwapchains();

        for (var viewIndex = 0;
             viewIndex < _viewConfigurationViews.Count;
             viewIndex++)
        {
            var view =
                _viewConfigurationViews[viewIndex];

            var createInfo =
                new XrSwapchainCreateInfo
                {
                    Type =
                        XrTypeSwapchainCreateInfo,

                    Next =
                        IntPtr.Zero,

                    CreateFlags =
                        0,

                    UsageFlags =
                        XrSwapchainUsageColorAttachmentBit |
                        XrSwapchainUsageSampledBit,

                    Format =
                        _colorSwapchainFormat,

                    SampleCount =
                        Math.Max(
                            1u,
                            view.RecommendedSwapchainSampleCount),

                    Width =
                        view.RecommendedImageRectWidth,

                    Height =
                        view.RecommendedImageRectHeight,

                    FaceCount =
                        1,

                    ArraySize =
                        1,

                    MipCount =
                        1
                };

            var result =
                _xrCreateSwapchain(
                    _session,
                    ref createInfo,
                    out var swapchain);

            _logger.LogInfo(
                $"[XRBackend] xrCreateSwapchain view={viewIndex}, " +
                $"result={result}, swapchain=0x{swapchain:X}, " +
                $"size={createInfo.Width}x{createInfo.Height}, " +
                $"format={createInfo.Format}.");

            if (result != XrSuccess ||
                swapchain == 0)
            {
                DestroyColorSwapchains();

                return Fail(
                    $"xrCreateSwapchain failed for view {viewIndex} " +
                    $"with XrResult {result}.");
            }

            _colorSwapchains.Add(
                swapchain);

            if (!EnumerateSwapchainImages(
                    viewIndex,
                    swapchain,
                    createInfo.Width,
                    createInfo.Height,
                    createInfo.Format))
            {
                DestroyColorSwapchains();
                return false;
            }
        }

        State =
            XrBackendState.SwapchainImagesReady;

        StatusMessage =
            $"Created {_colorSwapchains.Count} OpenXR color swapchains " +
            $"and enumerated {_swapchainImageSets.Sum(set => set.Textures.Count)} " +
            $"D3D11 textures using DXGI format {_colorSwapchainFormat}.";

        _logger.LogInfo(
            $"[XRBackend] {StatusMessage}");

        return true;
    }

    [HideFromIl2Cpp]
    private bool CreateToolUiSwapchain()
    {
        if (_xrCreateSwapchain is null)
        {
            return Fail(
                "xrCreateSwapchain delegate is unavailable for tool UI.");
        }

        DestroyToolUiSwapchain();

        var createInfo =
            new XrSwapchainCreateInfo
            {
                Type =
                    XrTypeSwapchainCreateInfo,

                Next =
                    IntPtr.Zero,

                CreateFlags =
                    0,

                UsageFlags =
                    XrSwapchainUsageColorAttachmentBit |
                    XrSwapchainUsageSampledBit,

                Format =
                    _colorSwapchainFormat,

                SampleCount =
                    1,

                Width =
                    ToolUiSwapchainWidth,

                Height =
                    ToolUiSwapchainHeight,

                FaceCount =
                    1,

                ArraySize =
                    1,

                MipCount =
                    1
            };

        var result =
            _xrCreateSwapchain(
                _session,
                ref createInfo,
                out _toolUiSwapchain);

        _logger.LogInfo(
            $"[XRBackend] xrCreateSwapchain tool-ui result={result}, " +
            $"swapchain=0x{_toolUiSwapchain:X}, " +
            $"size={createInfo.Width}x{createInfo.Height}, " +
            $"format={createInfo.Format}.");

        if (result != XrSuccess ||
            _toolUiSwapchain == 0)
        {
            _toolUiSwapchain =
                0;

            return Fail(
                $"xrCreateSwapchain failed for tool UI with XrResult {result}.");
        }

        if (!EnumerateToolUiSwapchainImages(
                _toolUiSwapchain,
                createInfo.Width,
                createInfo.Height,
                createInfo.Format))
        {
            DestroyToolUiSwapchain();
            return false;
        }

        return true;
    }

    [HideFromIl2Cpp]
    private bool EnumerateToolUiSwapchainImages(
        ulong swapchain,
        uint width,
        uint height,
        long format)
    {
        if (_xrEnumerateSwapchainImages is null)
        {
            return Fail(
                "xrEnumerateSwapchainImages delegate is unavailable.");
        }

        var countResult =
            _xrEnumerateSwapchainImages(
                swapchain,
                0,
                out var imageCount,
                IntPtr.Zero);

        if (countResult != XrSuccess ||
            imageCount == 0)
        {
            return Fail(
                "Could not query tool UI swapchain images.");
        }

        var imageSize =
            Marshal.SizeOf<XrSwapchainImageD3D11Khr>();

        var imagesPointer =
            Marshal.AllocHGlobal(
                checked((int)imageCount * imageSize));

        try
        {
            for (var imageIndex = 0;
                 imageIndex < imageCount;
                 imageIndex++)
            {
                var image =
                    new XrSwapchainImageD3D11Khr
                    {
                        Type =
                            XrTypeSwapchainImageD3D11Khr,

                        Next =
                            IntPtr.Zero,

                        Texture =
                            IntPtr.Zero
                    };

                Marshal.StructureToPtr(
                    image,
                    IntPtr.Add(
                        imagesPointer,
                        imageIndex * imageSize),
                    false);
            }

            var imagesResult =
                _xrEnumerateSwapchainImages(
                    swapchain,
                    imageCount,
                    out var writtenImageCount,
                    imagesPointer);

            if (imagesResult != XrSuccess ||
                writtenImageCount != imageCount)
            {
                return Fail(
                    "OpenXR did not return the expected tool UI images.");
            }

            var textures =
                new List<IntPtr>(
                    checked((int)writtenImageCount));

            for (var imageIndex = 0;
                 imageIndex < writtenImageCount;
                 imageIndex++)
            {
                var image =
                    Marshal.PtrToStructure<XrSwapchainImageD3D11Khr>(
                        IntPtr.Add(
                            imagesPointer,
                            imageIndex * imageSize));

                if (image.Texture == IntPtr.Zero)
                {
                    return Fail(
                        $"Tool UI swapchain texture {imageIndex} is null.");
                }

                textures.Add(
                    image.Texture);
            }

            _toolUiSwapchainImageSet =
                new OpenXrSwapchainImageSet(
                    -1,
                    swapchain,
                    width,
                    height,
                    format,
                    textures);

            _logger.LogInfo(
                $"[XRBackend] Tool UI swapchain ready with " +
                $"{textures.Count} images.");

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(
                imagesPointer);
        }
    }

    [HideFromIl2Cpp]
    private bool EnumerateSwapchainImages(
        int viewIndex,
        ulong swapchain,
        uint width,
        uint height,
        long format)
    {
        if (_xrEnumerateSwapchainImages is null)
        {
            return Fail(
                "xrEnumerateSwapchainImages delegate is unavailable.");
        }

        var countResult =
            _xrEnumerateSwapchainImages(
                swapchain,
                0,
                out var imageCount,
                IntPtr.Zero);

        _logger.LogInfo(
            $"[XRBackend] Swapchain image count view={viewIndex}, " +
            $"result={countResult}, count={imageCount}.");

        if (countResult != XrSuccess ||
            imageCount == 0)
        {
            return Fail(
                $"Could not query swapchain images for view {viewIndex}.");
        }

        var imageSize =
            Marshal.SizeOf<XrSwapchainImageD3D11Khr>();

        var imagesPointer =
            Marshal.AllocHGlobal(
                checked((int)imageCount * imageSize));

        try
        {
            for (var imageIndex = 0;
                 imageIndex < imageCount;
                 imageIndex++)
            {
                var image =
                    new XrSwapchainImageD3D11Khr
                    {
                        Type =
                            XrTypeSwapchainImageD3D11Khr,

                        Next =
                            IntPtr.Zero,

                        Texture =
                            IntPtr.Zero
                    };

                Marshal.StructureToPtr(
                    image,
                    IntPtr.Add(
                        imagesPointer,
                        imageIndex * imageSize),
                    false);
            }

            var imagesResult =
                _xrEnumerateSwapchainImages(
                    swapchain,
                    imageCount,
                    out var writtenImageCount,
                    imagesPointer);

            _logger.LogInfo(
                $"[XRBackend] Swapchain image query view={viewIndex}, " +
                $"result={imagesResult}, written={writtenImageCount}.");

            if (imagesResult != XrSuccess ||
                writtenImageCount != imageCount)
            {
                return Fail(
                    $"OpenXR did not return the expected swapchain images " +
                    $"for view {viewIndex}.");
            }

            var textures =
                new List<IntPtr>(
                    checked((int)writtenImageCount));

            for (var imageIndex = 0;
                 imageIndex < writtenImageCount;
                 imageIndex++)
            {
                var image =
                    Marshal.PtrToStructure<XrSwapchainImageD3D11Khr>(
                        IntPtr.Add(
                            imagesPointer,
                            imageIndex * imageSize));

                if (image.Texture == IntPtr.Zero)
                {
                    return Fail(
                        $"Swapchain texture {imageIndex} for view " +
                        $"{viewIndex} is null.");
                }

                textures.Add(
                    image.Texture);

                _logger.LogInfo(
                    $"[XRBackend] Swapchain image view={viewIndex}, " +
                    $"index={imageIndex}, " +
                    $"texture=0x{image.Texture.ToInt64():X}.");
            }

            _swapchainImageSets.Add(
                new OpenXrSwapchainImageSet(
                    viewIndex,
                    swapchain,
                    width,
                    height,
                    format,
                    textures));

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(
                imagesPointer);
        }
    }

    [HideFromIl2Cpp]
    private bool ChooseSwapchainFormat()
    {
        if (_xrEnumerateSwapchainFormats is null)
        {
            return Fail(
                "xrEnumerateSwapchainFormats delegate is unavailable.");
        }

        var countResult =
            _xrEnumerateSwapchainFormats(
                _session,
                0,
                out var formatCount,
                IntPtr.Zero);

        _logger.LogInfo(
            $"[XRBackend] Swapchain format count result={countResult}, " +
            $"count={formatCount}.");

        if (countResult != XrSuccess ||
            formatCount == 0)
        {
            return Fail(
                "OpenXR returned no swapchain formats.");
        }

        var formatsPointer =
            Marshal.AllocHGlobal(
                checked((int)formatCount * sizeof(long)));

        try
        {
            var formatsResult =
                _xrEnumerateSwapchainFormats(
                    _session,
                    formatCount,
                    out var writtenFormatCount,
                    formatsPointer);

            if (formatsResult != XrSuccess ||
                writtenFormatCount == 0)
            {
                return Fail(
                    "Could not enumerate OpenXR swapchain formats.");
            }

            var formats =
                new long[writtenFormatCount];

            Marshal.Copy(
                formatsPointer,
                formats,
                0,
                checked((int)writtenFormatCount));

            _logger.LogInfo(
                $"[XRBackend] Supported swapchain formats: " +
                $"{string.Join(", ", formats)}.");

            var preferred =
                new[]
                {
                    DxgiFormatR8G8B8A8UnormSrgb,
                    DxgiFormatB8G8R8A8UnormSrgb,
                    DxgiFormatR8G8B8A8Unorm,
                    DxgiFormatB8G8R8A8Unorm
                };

            foreach (var candidate in preferred)
            {
                if (Array.IndexOf(
                        formats,
                        candidate) >= 0)
                {
                    _colorSwapchainFormat =
                        candidate;

                    _logger.LogInfo(
                        $"[XRBackend] Selected swapchain format " +
                        $"{_colorSwapchainFormat}.");

                    return true;
                }
            }

            return Fail(
                "No supported RGBA8/BGRA8 color swapchain format was found.");
        }
        finally
        {
            Marshal.FreeHGlobal(
                formatsPointer);
        }
    }

    [HideFromIl2Cpp]
    private void DestroyToolUiSwapchain()
    {
        _toolUiSwapchainImageSet =
            null;

        if (_toolUiSwapchain == 0)
        {
            return;
        }

        if (_xrDestroySwapchain is not null)
        {
            try
            {
                var result =
                    _xrDestroySwapchain(
                        _toolUiSwapchain);

                _logger.LogInfo(
                    $"[XRBackend] xrDestroySwapchain tool-ui " +
                    $"swapchain=0x{_toolUiSwapchain:X}, result={result}.");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    $"[XRBackend] Destroy tool UI swapchain threw: " +
                    $"{exception.Message}");
            }
        }

        _toolUiSwapchain =
            0;
    }

    [HideFromIl2Cpp]
    private void DestroyColorSwapchains()
    {
        _swapchainImageSets.Clear();

        if (_xrDestroySwapchain is null)
        {
            _colorSwapchains.Clear();
            return;
        }

        foreach (var swapchain in _colorSwapchains)
        {
            if (swapchain == 0)
            {
                continue;
            }

            try
            {
                var result =
                    _xrDestroySwapchain(
                        swapchain);

                _logger.LogInfo(
                    $"[XRBackend] xrDestroySwapchain " +
                    $"swapchain=0x{swapchain:X}, result={result}.");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    $"[XRBackend] xrDestroySwapchain threw: " +
                    $"{exception.Message}");
            }
        }

        _colorSwapchains.Clear();
    }

    [HideFromIl2Cpp]
    private void PollOpenXrEvents()
    {
        if (_xrPollEvent is null ||
            _instance == 0)
        {
            return;
        }

        var eventBuffer =
            Marshal.AllocHGlobal(
                XrMaxEventDataSize);

        try
        {
            for (var eventIndex = 0;
                 eventIndex < 32;
                 eventIndex++)
            {
                ZeroMemory(
                    eventBuffer,
                    XrMaxEventDataSize);

                Marshal.WriteInt32(
                    eventBuffer,
                    XrTypeEventDataBuffer);

                var result =
                    _xrPollEvent(
                        _instance,
                        eventBuffer);

                if (result == XrEventUnavailable)
                {
                    break;
                }

                if (result != XrSuccess)
                {
                    _logger.LogWarning(
                        $"[XRBackend] xrPollEvent result={result}.");
                    break;
                }

                var eventType =
                    Marshal.ReadInt32(
                        eventBuffer);

                if (eventType ==
                    XrTypeEventDataSessionStateChanged)
                {
                    var stateChanged =
                        Marshal.PtrToStructure<
                            XrEventDataSessionStateChanged>(
                                eventBuffer);

                    HandleSessionStateChanged(
                        stateChanged);
                }
                else
                {
                    _logger.LogInfo(
                        $"[XRBackend] OpenXR event type={eventType}.");
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(
                eventBuffer);
        }
    }

    [HideFromIl2Cpp]
    private void HandleSessionStateChanged(
        XrEventDataSessionStateChanged stateChanged)
    {
        _sessionState =
            stateChanged.State;

        _logger.LogInfo(
            $"[XRBackend] Session state changed: " +
            $"{FormatSessionState(_sessionState)} " +
            $"({_sessionState}), time={stateChanged.Time}.");

        switch (_sessionState)
        {
            case XrSessionStateReady:
                BeginSession();
                break;

            case XrSessionStateStopping:
                EndSession();
                break;

            case XrSessionStateLossPending:
            case XrSessionStateExiting:
                _exitRequested = true;

                StatusMessage =
                    $"OpenXR requested session exit: " +
                    $"{FormatSessionState(_sessionState)}.";

                _logger.LogWarning(
                    $"[XRBackend] {StatusMessage}");
                break;
        }
    }

    [HideFromIl2Cpp]
    private bool BeginSession()
    {
        if (_sessionRunning)
        {
            return true;
        }

        if (_xrBeginSession is null)
        {
            return Fail(
                "xrBeginSession delegate is unavailable.");
        }

        var beginInfo =
            new XrSessionBeginInfo
            {
                Type =
                    XrTypeSessionBeginInfo,

                Next =
                    IntPtr.Zero,

                PrimaryViewConfigurationType =
                    XrViewConfigurationTypePrimaryStereo
            };

        _logger.LogInfo(
            "[XRBackend] Calling xrBeginSession for " +
            "XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO.");

        var result =
            _xrBeginSession(
                _session,
                ref beginInfo);

        _logger.LogInfo(
            $"[XRBackend] xrBeginSession result={result}.");

        if (result != XrSuccess)
        {
            return Fail(
                $"xrBeginSession failed with XrResult {result}.");
        }

        _sessionRunning = true;

        State =
            XrBackendState.SessionRunning;

        StatusMessage =
            "OpenXR session is running.";

        _logger.LogInfo(
            $"[XRBackend] {StatusMessage}");

        return true;
    }

    [HideFromIl2Cpp]
    private void EndSession()
    {
        if (!_sessionRunning ||
            _xrEndSession is null)
        {
            return;
        }

        var result =
            _xrEndSession(
                _session);

        _logger.LogInfo(
            $"[XRBackend] xrEndSession result={result}.");

        _sessionRunning = false;

        if (result == XrSuccess)
        {
            State =
                XrBackendState.WaitingForSessionReady;

            StatusMessage =
                "OpenXR session stopped; waiting for READY.";
        }
        else
        {
            Fail(
                $"xrEndSession failed with XrResult {result}.");
        }
    }

    [HideFromIl2Cpp]
    private void RunEmptyFrame()
    {
        if (_xrWaitFrame is null ||
            _xrBeginFrame is null ||
            _xrEndFrame is null)
        {
            return;
        }

        var waitInfo =
            new XrFrameWaitInfo
            {
                Type =
                    XrTypeFrameWaitInfo,

                Next =
                    IntPtr.Zero
            };

        var frameState =
            new XrFrameState
            {
                Type =
                    XrTypeFrameState,

                Next =
                    IntPtr.Zero
            };

        var waitResult =
            _xrWaitFrame(
                _session,
                ref waitInfo,
                ref frameState);

        if (waitResult != XrSuccess)
        {
            _logger.LogWarning(
                $"[XRBackend] xrWaitFrame result={waitResult}.");
            return;
        }

        var beginInfo =
            new XrFrameBeginInfo
            {
                Type =
                    XrTypeFrameBeginInfo,

                Next =
                    IntPtr.Zero
            };

        var beginResult =
            _xrBeginFrame(
                _session,
                ref beginInfo);

        if (beginResult != XrSuccess)
        {
            _logger.LogWarning(
                $"[XRBackend] xrBeginFrame result={beginResult}.");
            return;
        }

        var renderResult =
            SubmitTestPatternFrame(
                frameState,
                out var projectionSubmitted);

        _frameCount++;

        if (_frameCount <= 5 ||
            _frameCount % 120 == 0 ||
            renderResult != XrSuccess)
        {
            _logger.LogInfo(
                $"[XRBackend] Test frame {_frameCount}: " +
                $"wait={waitResult}, begin={beginResult}, " +
                $"end={renderResult}, shouldRender=" +
                $"{frameState.ShouldRender}, " +
                $"displayTime={frameState.PredictedDisplayTime}.");
        }

        if (renderResult == XrSuccess &&
            projectionSubmitted)
        {
            if (D3D11PresentHookProbe.StereoSourceTexturesReady)
            {
                State =
                    XrBackendState.StereoPrototypeRendering;

                StatusMessage =
                    "OpenXR stereo camera prototype is active.";
            }
            else
            {
                State =
                    XrBackendState.CinemaQuadRendering;

                StatusMessage =
                    "OpenXR head-locked cinema quad is active.";
            }
        }
    }

    [HideFromIl2Cpp]
    private int SubmitTestPatternFrame(
        XrFrameState frameState,
        out bool projectionSubmitted)
    {
        projectionSubmitted = false;

        if (_xrEndFrame is null)
        {
            return -1;
        }

        if (frameState.ShouldRender == 0)
        {
            var emptyEndInfo =
                new XrFrameEndInfo
                {
                    Type =
                        XrTypeFrameEndInfo,

                    Next =
                        IntPtr.Zero,

                    DisplayTime =
                        frameState.PredictedDisplayTime,

                    EnvironmentBlendMode =
                        XrEnvironmentBlendModeOpaque,

                    LayerCount =
                        0,

                    Layers =
                        IntPtr.Zero
                };

            return _xrEndFrame(
                _session,
                ref emptyEndInfo);
        }

        if (!TryLocateStereoViews(
                frameState.PredictedDisplayTime,
                out var views))
        {
            return EndFrameWithoutLayers(
                frameState.PredictedDisplayTime);
        }

        var stereoRendering =
            D3D11PresentHookProbe.StereoSourceTexturesReady;

        var stereoUiLayer =
            stereoRendering &&
            StereoCameraRig.StereoUiLayerActive;

        if (!TryRenderOutputToSwapchains(
                stereoRendering,
                out var acquiredViewCount))
        {
            ReleaseAcquiredSwapchainImages(
                acquiredViewCount);

            return EndFrameWithoutLayers(
                frameState.PredictedDisplayTime);
        }

        if (!ReleaseAcquiredSwapchainImages(
                acquiredViewCount))
        {
            return EndFrameWithoutLayers(
                frameState.PredictedDisplayTime);
        }

        if (stereoUiLayer &&
            !TryRenderToolUiSwapchain())
        {
            // Do not lose stereo gameplay if the optional UI layer fails.
            stereoUiLayer =
                false;
        }

        var projectionViews =
            BuildProjectionViewsForRenderedPose(
                views);

        var endResult =
            stereoRendering
                ? stereoUiLayer
                    ? EndFrameWithProjectionAndToolUiLayer(
                        frameState.PredictedDisplayTime,
                        projectionViews)
                    : EndFrameWithProjectionLayer(
                        frameState.PredictedDisplayTime,
                        projectionViews)
                : EndFrameWithCinemaQuad(
                    frameState.PredictedDisplayTime);

        projectionSubmitted =
            endResult == XrSuccess;

        return endResult;
    }

    [HideFromIl2Cpp]
    private bool TryLocateStereoViews(
        long displayTime,
        out XrView[] views)
    {
        views = Array.Empty<XrView>();

        if (_xrLocateViews is null ||
            _localSpace == 0)
        {
            return false;
        }

        var locateInfo =
            new XrViewLocateInfo
            {
                Type =
                    XrTypeViewLocateInfo,

                Next =
                    IntPtr.Zero,

                ViewConfigurationType =
                    XrViewConfigurationTypePrimaryStereo,

                DisplayTime =
                    displayTime,

                Space =
                    _localSpace
            };

        var viewState =
            new XrViewState
            {
                Type =
                    XrTypeViewState,

                Next =
                    IntPtr.Zero
            };

        const uint viewCapacity = 2;

        var viewSize =
            Marshal.SizeOf<XrView>();

        var viewsPointer =
            Marshal.AllocHGlobal(
                checked((int)viewCapacity * viewSize));

        try
        {
            for (var viewIndex = 0;
                 viewIndex < viewCapacity;
                 viewIndex++)
            {
                var view =
                    new XrView
                    {
                        Type =
                            XrTypeView,

                        Next =
                            IntPtr.Zero
                    };

                Marshal.StructureToPtr(
                    view,
                    IntPtr.Add(
                        viewsPointer,
                        viewIndex * viewSize),
                    false);
            }

            var result =
                _xrLocateViews(
                    _session,
                    ref locateInfo,
                    ref viewState,
                    viewCapacity,
                    out var viewCount,
                    viewsPointer);

            if (result != XrSuccess ||
                viewCount != viewCapacity)
            {
                _logger.LogWarning(
                    $"[XRBackend] xrLocateViews result={result}, " +
                    $"viewCount={viewCount}.");
                return false;
            }

            views =
                new XrView[viewCapacity];

            for (var viewIndex = 0;
                 viewIndex < viewCapacity;
                 viewIndex++)
            {
                views[viewIndex] =
                    Marshal.PtrToStructure<XrView>(
                        IntPtr.Add(
                            viewsPointer,
                            viewIndex * viewSize));
            }

            StoreLatestHeadPose(
                views);

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(
                viewsPointer);
        }
    }

    [HideFromIl2Cpp]
    private bool TryRenderOutputToSwapchains(
        bool stereoRendering,
        out int acquiredViewCount)
    {
        acquiredViewCount = 0;

        if (_xrAcquireSwapchainImage is null ||
            _xrWaitSwapchainImage is null ||
            _xrReleaseSwapchainImage is null ||
            _swapchainImageSets.Count < 1)
        {
            return false;
        }

        var requiredViewCount =
            stereoRendering
                ? Math.Min(
                    2,
                    _swapchainImageSets.Count)
                : 1;

        for (var viewIndex = 0;
             viewIndex < requiredViewCount;
             viewIndex++)
        {
            var imageSet =
                _swapchainImageSets[viewIndex];

            var acquireInfo =
                new XrSwapchainImageAcquireInfo
                {
                    Type =
                        XrTypeSwapchainImageAcquireInfo,

                    Next =
                        IntPtr.Zero
                };

            var acquireResult =
                _xrAcquireSwapchainImage(
                    imageSet.Swapchain,
                    ref acquireInfo,
                    out var imageIndex);

            if (acquireResult != XrSuccess ||
                imageIndex >= imageSet.Textures.Count)
            {
                _logger.LogWarning(
                    $"[XRBackend] xrAcquireSwapchainImage " +
                    $"view={viewIndex}, result={acquireResult}, " +
                    $"index={imageIndex}.");
                return false;
            }

            acquiredViewCount =
                viewIndex + 1;

            var waitInfo =
                new XrSwapchainImageWaitInfo
                {
                    Type =
                        XrTypeSwapchainImageWaitInfo,

                    Next =
                        IntPtr.Zero,

                    Timeout =
                        long.MaxValue
                };

            var waitResult =
                _xrWaitSwapchainImage(
                    imageSet.Swapchain,
                    ref waitInfo);

            if (waitResult != XrSuccess)
            {
                _logger.LogWarning(
                    $"[XRBackend] xrWaitSwapchainImage " +
                    $"view={viewIndex}, result={waitResult}.");
                return false;
            }

            var texture =
                imageSet.Textures[
                    checked((int)imageIndex)];

            var blitSucceeded =
                stereoRendering
                    ? D3D11PresentHookProbe.BlitStereoSourceTexture(
                        _logger,
                        viewIndex,
                        texture,
                        imageSet.Format)
                    : D3D11PresentHookProbe.BlitCapturedBackBuffer(
                        _logger,
                        texture,
                        imageSet.Format);

            if (!blitSucceeded)
            {
                return false;
            }
        }

        return true;
    }

    [HideFromIl2Cpp]
    private bool TryRenderToolUiSwapchain()
    {
        if (_xrAcquireSwapchainImage is null ||
            _xrWaitSwapchainImage is null ||
            _xrReleaseSwapchainImage is null ||
            _toolUiSwapchainImageSet is null)
        {
            return false;
        }

        var imageSet =
            _toolUiSwapchainImageSet;

        var acquireInfo =
            new XrSwapchainImageAcquireInfo
            {
                Type =
                    XrTypeSwapchainImageAcquireInfo,

                Next =
                    IntPtr.Zero
            };

        var acquireResult =
            _xrAcquireSwapchainImage(
                imageSet.Swapchain,
                ref acquireInfo,
                out var imageIndex);

        if (acquireResult != XrSuccess ||
            imageIndex >= imageSet.Textures.Count)
        {
            _logger.LogWarning(
                $"[XRBackend] Tool UI acquire failed: " +
                $"result={acquireResult}, index={imageIndex}.");

            return false;
        }

        var acquired =
            true;

        try
        {
            var waitInfo =
                new XrSwapchainImageWaitInfo
                {
                    Type =
                        XrTypeSwapchainImageWaitInfo,

                    Next =
                        IntPtr.Zero,

                    Timeout =
                        long.MaxValue
                };

            var waitResult =
                _xrWaitSwapchainImage(
                    imageSet.Swapchain,
                    ref waitInfo);

            if (waitResult != XrSuccess)
            {
                _logger.LogWarning(
                    $"[XRBackend] Tool UI wait failed: " +
                    $"result={waitResult}.");

                return false;
            }

            var texture =
                imageSet.Textures[
                    checked((int)imageIndex)];

            var sourceTexture =
                StereoCameraRig.ToolUiNativeTexture;

            var copied =
                sourceTexture != IntPtr.Zero
                    ? D3D11PresentHookProbe.BlitSourceTexture(
                        _logger,
                        sourceTexture,
                        texture,
                        imageSet.Format)
                    : D3D11PresentHookProbe.BlitCapturedBackBuffer(
                        _logger,
                        texture,
                        imageSet.Format);

            if (!copied)
            {
                return false;
            }

            if (!_toolUiDiagnosticDumped &&
                sourceTexture != IntPtr.Zero)
            {
                _toolUiDiagnosticDumped =
                    true;

                try
                {
                    var dumpDirectory =
                        Path.Combine(
                            Paths.GameRootPath,
                            "BepInEx",
                            "plugins",
                            "MortuaryAssistantVR");

                    Directory.CreateDirectory(
                        dumpDirectory);

                    D3D11PresentHookProbe.DumpTextureTga(
                        _logger,
                        sourceTexture,
                        Path.Combine(
                            dumpDirectory,
                            "ToolUiSource-v0.31.15.tga"),
                        "Tool UI Unity source");

                    D3D11PresentHookProbe.DumpTextureTga(
                        _logger,
                        texture,
                        Path.Combine(
                            dumpDirectory,
                            "ToolUiOpenXR-v0.31.15.tga"),
                        "Tool UI OpenXR destination");
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        $"[XRBackend] Tool UI diagnostic dump failed: " +
                        $"{exception.Message}");
                }
            }

            return true;
        }
        finally
        {
            if (acquired)
            {
                var releaseInfo =
                    new XrSwapchainImageReleaseInfo
                    {
                        Type =
                            XrTypeSwapchainImageReleaseInfo,

                        Next =
                            IntPtr.Zero
                    };

                var releaseResult =
                    _xrReleaseSwapchainImage(
                        imageSet.Swapchain,
                        ref releaseInfo);

                if (releaseResult != XrSuccess)
                {
                    _logger.LogWarning(
                        $"[XRBackend] Tool UI release failed: " +
                        $"result={releaseResult}.");
                }
            }
        }
    }

    [HideFromIl2Cpp]
    private bool ReleaseAcquiredSwapchainImages(
        int acquiredViewCount)
    {
        if (acquiredViewCount <= 0)
        {
            return true;
        }

        if (_xrReleaseSwapchainImage is null)
        {
            _logger.LogError(
                "[XRBackend] xrReleaseSwapchainImage delegate " +
                "is unavailable.");

            return false;
        }

        var success =
            true;

        for (var viewIndex = 0;
             viewIndex < acquiredViewCount &&
             viewIndex < _swapchainImageSets.Count;
             viewIndex++)
        {
            var releaseInfo =
                new XrSwapchainImageReleaseInfo
                {
                    Type =
                        XrTypeSwapchainImageReleaseInfo,

                    Next =
                        IntPtr.Zero
                };

            var result =
                _xrReleaseSwapchainImage(
                    _swapchainImageSets[viewIndex].Swapchain,
                    ref releaseInfo);

            if (result != XrSuccess)
            {
                success =
                    false;

                _logger.LogWarning(
                    $"[XRBackend] xrReleaseSwapchainImage " +
                    $"view={viewIndex}, result={result}.");
            }
        }

        return success;
    }

    [HideFromIl2Cpp]
    internal void MarkRenderedHeadPose(
        OpenXrHeadPose headPose)
    {
        _latestRenderedHeadPose =
            headPose;

        _hasRenderedHeadPose =
            true;
    }

    [HideFromIl2Cpp]
    internal bool TryGetLatestHeadPose(
        out OpenXrHeadPose headPose)
    {
        headPose =
            _latestHeadPose;

        return _hasHeadPose;
    }

    [HideFromIl2Cpp]
    private void StoreLatestHeadPose(
        XrView[] views)
    {
        if (views.Length < 1)
        {
            return;
        }

        // Both eye views carry the same head orientation. Their positions
        // differ by the IPD, so use the midpoint for the head position.
        var left =
            views[0].Pose;

        var right =
            views.Length > 1
                ? views[1].Pose
                : views[0].Pose;

        _headPoseSequence++;

        _latestHeadPose =
            new OpenXrHeadPose(
                (left.Position.X + right.Position.X) * 0.5f,
                (left.Position.Y + right.Position.Y) * 0.5f,
                (left.Position.Z + right.Position.Z) * 0.5f,
                left.Orientation.X,
                left.Orientation.Y,
                left.Orientation.Z,
                left.Orientation.W,
                views[0].Fov.AngleLeft,
                views[0].Fov.AngleRight,
                views[0].Fov.AngleUp,
                views[0].Fov.AngleDown,
                views.Length > 1
                    ? views[1].Fov.AngleLeft
                    : views[0].Fov.AngleLeft,
                views.Length > 1
                    ? views[1].Fov.AngleRight
                    : views[0].Fov.AngleRight,
                views.Length > 1
                    ? views[1].Fov.AngleUp
                    : views[0].Fov.AngleUp,
                views.Length > 1
                    ? views[1].Fov.AngleDown
                    : views[0].Fov.AngleDown,
                _headPoseSequence);

        _hasHeadPose =
            true;
    }

    [HideFromIl2Cpp]
    private XrView[] BuildProjectionViewsForRenderedPose(
        XrView[] currentViews)
    {
        if (!_hasRenderedHeadPose ||
            currentViews.Length < 2)
        {
            return currentViews;
        }

        var rendered =
            _latestRenderedHeadPose;

        var orientation =
            new XrQuaternionf
            {
                X = rendered.OrientationX,
                Y = rendered.OrientationY,
                Z = rendered.OrientationZ,
                W = rendered.OrientationW
            };

        const float halfIpdMetres =
            0.032f;

        var leftOffset =
            RotateVector(
                orientation,
                new XrVector3f
                {
                    X = -halfIpdMetres,
                    Y = 0,
                    Z = 0
                });

        var rightOffset =
            RotateVector(
                orientation,
                new XrVector3f
                {
                    X = halfIpdMetres,
                    Y = 0,
                    Z = 0
                });

        var views =
            new XrView[2];

        views[0] =
            currentViews[0];

        views[1] =
            currentViews[1];

        views[0].Pose =
            new XrPosef
            {
                Orientation =
                    orientation,

                Position =
                    new XrVector3f
                    {
                        X =
                            rendered.PositionX +
                            leftOffset.X,

                        Y =
                            rendered.PositionY +
                            leftOffset.Y,

                        Z =
                            rendered.PositionZ +
                            leftOffset.Z
                    }
            };

        views[1].Pose =
            new XrPosef
            {
                Orientation =
                    orientation,

                Position =
                    new XrVector3f
                    {
                        X =
                            rendered.PositionX +
                            rightOffset.X,

                        Y =
                            rendered.PositionY +
                            rightOffset.Y,

                        Z =
                            rendered.PositionZ +
                            rightOffset.Z
                    }
            };

        views[0].Fov =
            new XrFovf
            {
                AngleLeft =
                    rendered.LeftAngleLeft,

                AngleRight =
                    rendered.LeftAngleRight,

                AngleUp =
                    rendered.LeftAngleUp,

                AngleDown =
                    rendered.LeftAngleDown
            };

        views[1].Fov =
            new XrFovf
            {
                AngleLeft =
                    rendered.RightAngleLeft,

                AngleRight =
                    rendered.RightAngleRight,

                AngleUp =
                    rendered.RightAngleUp,

                AngleDown =
                    rendered.RightAngleDown
            };

        if (rendered.Sequence !=
            _lastSubmittedRenderPoseSequence)
        {
            _lastSubmittedRenderPoseSequence =
                rendered.Sequence;

            if (rendered.Sequence <= 5 ||
                rendered.Sequence % 600 == 0)
            {
                _logger.LogInfo(
                    $"[XRBackend] Submitting render-matched pose " +
                    $"sequence={rendered.Sequence}.");
            }
        }

        return views;
    }

    [HideFromIl2Cpp]
    private static XrVector3f RotateVector(
        XrQuaternionf quaternion,
        XrVector3f vector)
    {
        // q * v * inverse(q), expanded without allocations.
        var qx =
            quaternion.X;

        var qy =
            quaternion.Y;

        var qz =
            quaternion.Z;

        var qw =
            quaternion.W;

        var tx =
            2.0f *
            (qy * vector.Z -
             qz * vector.Y);

        var ty =
            2.0f *
            (qz * vector.X -
             qx * vector.Z);

        var tz =
            2.0f *
            (qx * vector.Y -
             qy * vector.X);

        return new XrVector3f
        {
            X =
                vector.X +
                qw * tx +
                (qy * tz -
                 qz * ty),

            Y =
                vector.Y +
                qw * ty +
                (qz * tx -
                 qx * tz),

            Z =
                vector.Z +
                qw * tz +
                (qx * ty -
                 qy * tx)
        };
    }

    [HideFromIl2Cpp]
    private int EndFrameWithProjectionAndToolUiLayer(
        long displayTime,
        XrView[] views)
    {
        if (_xrEndFrame is null ||
            views.Length < 2 ||
            _swapchainImageSets.Count < 2 ||
            _toolUiSwapchainImageSet is null ||
            _viewSpace == 0)
        {
            return EndFrameWithProjectionLayer(
                displayTime,
                views);
        }

        var projectionViewSize =
            Marshal.SizeOf<XrCompositionLayerProjectionView>();

        var projectionViewsPointer =
            Marshal.AllocHGlobal(
                2 * projectionViewSize);

        var projectionLayerPointer =
            IntPtr.Zero;

        var leftQuadLayerPointer =
            IntPtr.Zero;

        var rightQuadLayerPointer =
            IntPtr.Zero;

        var layersPointer =
            IntPtr.Zero;

        try
        {
            for (var viewIndex = 0;
                 viewIndex < 2;
                 viewIndex++)
            {
                var imageSet =
                    _swapchainImageSets[viewIndex];

                var projectionView =
                    new XrCompositionLayerProjectionView
                    {
                        Type =
                            XrTypeCompositionLayerProjectionView,

                        Next =
                            IntPtr.Zero,

                        Pose =
                            views[viewIndex].Pose,

                        Fov =
                            views[viewIndex].Fov,

                        SubImage =
                            new XrSwapchainSubImage
                            {
                                Swapchain =
                                    imageSet.Swapchain,

                                ImageRect =
                                    new XrRect2Di
                                    {
                                        Offset =
                                            new XrOffset2Di
                                            {
                                                X = 0,
                                                Y = 0
                                            },

                                        Extent =
                                            new XrExtent2Di
                                            {
                                                Width =
                                                    checked((int)imageSet.Width),

                                                Height =
                                                    checked((int)imageSet.Height)
                                            }
                                    },

                                ImageArrayIndex =
                                    0
                            }
                    };

                Marshal.StructureToPtr(
                    projectionView,
                    IntPtr.Add(
                        projectionViewsPointer,
                        viewIndex * projectionViewSize),
                    false);
            }

            var projectionLayer =
                new XrCompositionLayerProjection
                {
                    Type =
                        XrTypeCompositionLayerProjection,

                    Next =
                        IntPtr.Zero,

                    LayerFlags =
                        0,

                    Space =
                        _localSpace,

                    ViewCount =
                        2,

                    Views =
                        projectionViewsPointer
                };

            projectionLayerPointer =
                Marshal.AllocHGlobal(
                    Marshal.SizeOf<XrCompositionLayerProjection>());

            Marshal.StructureToPtr(
                projectionLayer,
                projectionLayerPointer,
                false);

            var uiImageSet =
                _toolUiSwapchainImageSet;

            var subImage =
                new XrSwapchainSubImage
                {
                    Swapchain =
                        uiImageSet.Swapchain,

                    ImageRect =
                        new XrRect2Di
                        {
                            Offset =
                                new XrOffset2Di
                                {
                                    X =
                                        ToolUiCropX,

                                    Y =
                                        ToolUiCropY
                                },

                            Extent =
                                new XrExtent2Di
                                {
                                    Width =
                                        ToolUiCropWidth,

                                    Height =
                                        ToolUiCropHeight
                                }
                        },

                    ImageArrayIndex =
                        0
                };

            var leftQuad =
                new XrCompositionLayerQuad
                {
                    Type =
                        XrTypeCompositionLayerQuad,

                    Next =
                        IntPtr.Zero,

                    LayerFlags =
                        XrCompositionLayerBlendTextureSourceAlphaBit,

                    Space =
                        _viewSpace,

                    EyeVisibility =
                        XrEyeVisibilityLeft,

                    SubImage =
                        subImage,

                    Pose =
                        new XrPosef
                        {
                            Orientation =
                                new XrQuaternionf
                                {
                                    X = 0.0f,
                                    Y = 0.0f,
                                    Z = 0.0f,
                                    W = 1.0f
                                },

                            Position =
                                new XrVector3f
                                {
                                    X =
                                        -ToolUiHudHalfIpdMetres,

                                    Y =
                                        0.0f,

                                    Z =
                                        -ToolUiHudDistanceMetres
                                }
                        },

                    Size =
                        new XrExtent2Df
                        {
                            Width =
                                ToolUiHudWidthMetres,

                            Height =
                                ToolUiHudHeightMetres
                        }
                };

            var rightQuad =
                new XrCompositionLayerQuad
                {
                    Type =
                        XrTypeCompositionLayerQuad,

                    Next =
                        IntPtr.Zero,

                    LayerFlags =
                        XrCompositionLayerBlendTextureSourceAlphaBit,

                    Space =
                        _viewSpace,

                    EyeVisibility =
                        XrEyeVisibilityRight,

                    SubImage =
                        subImage,

                    Pose =
                        new XrPosef
                        {
                            Orientation =
                                new XrQuaternionf
                                {
                                    X = 0.0f,
                                    Y = 0.0f,
                                    Z = 0.0f,
                                    W = 1.0f
                                },

                            Position =
                                new XrVector3f
                                {
                                    X =
                                        ToolUiHudHalfIpdMetres,

                                    Y =
                                        0.0f,

                                    Z =
                                        -ToolUiHudDistanceMetres
                                }
                        },

                    Size =
                        new XrExtent2Df
                        {
                            Width =
                                ToolUiHudWidthMetres,

                            Height =
                                ToolUiHudHeightMetres
                        }
                };

            leftQuadLayerPointer =
                Marshal.AllocHGlobal(
                    Marshal.SizeOf<XrCompositionLayerQuad>());

            rightQuadLayerPointer =
                Marshal.AllocHGlobal(
                    Marshal.SizeOf<XrCompositionLayerQuad>());

            Marshal.StructureToPtr(
                leftQuad,
                leftQuadLayerPointer,
                false);

            Marshal.StructureToPtr(
                rightQuad,
                rightQuadLayerPointer,
                false);

            layersPointer =
                Marshal.AllocHGlobal(
                    3 * IntPtr.Size);

            Marshal.WriteIntPtr(
                layersPointer,
                0,
                projectionLayerPointer);

            Marshal.WriteIntPtr(
                layersPointer,
                IntPtr.Size,
                leftQuadLayerPointer);

            Marshal.WriteIntPtr(
                layersPointer,
                2 * IntPtr.Size,
                rightQuadLayerPointer);

            if ((_frameCount % 600) == 0)
            {
                _logger.LogInfo(
                    $"[XRBackend] Tool UI zero-disparity HUD: " +
                    $"crop={ToolUiCropX},{ToolUiCropY} " +
                    $"{ToolUiCropWidth}x{ToolUiCropHeight}, " +
                    $"distance={ToolUiHudDistanceMetres:F2}m, " +
                    $"eyeOffset=+/-{ToolUiHudHalfIpdMetres:F3}m, " +
                    $"size={ToolUiHudWidthMetres:F3}x" +
                    $"{ToolUiHudHeightMetres:F3}m.");
            }

            var endInfo =
                new XrFrameEndInfo
                {
                    Type =
                        XrTypeFrameEndInfo,

                    Next =
                        IntPtr.Zero,

                    DisplayTime =
                        displayTime,

                    EnvironmentBlendMode =
                        XrEnvironmentBlendModeOpaque,

                    LayerCount =
                        3,

                    Layers =
                        layersPointer
                };

            return _xrEndFrame(
                _session,
                ref endInfo);
        }
        finally
        {
            if (layersPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(
                    layersPointer);
            }

            if (rightQuadLayerPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(
                    rightQuadLayerPointer);
            }

            if (leftQuadLayerPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(
                    leftQuadLayerPointer);
            }

            if (projectionLayerPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(
                    projectionLayerPointer);
            }

            Marshal.FreeHGlobal(
                projectionViewsPointer);
        }
    }

    [HideFromIl2Cpp]
    private int EndFrameWithProjectionLayer(
        long displayTime,
        XrView[] views)
    {
        if (_xrEndFrame is null ||
            views.Length < 2 ||
            _swapchainImageSets.Count < 2)
        {
            return -1;
        }

        var projectionViewSize =
            Marshal.SizeOf<XrCompositionLayerProjectionView>();

        var projectionViewsPointer =
            Marshal.AllocHGlobal(
                2 * projectionViewSize);

        var layerPointer =
            IntPtr.Zero;

        var layersPointer =
            IntPtr.Zero;

        try
        {
            for (var viewIndex = 0;
                 viewIndex < 2;
                 viewIndex++)
            {
                var imageSet =
                    _swapchainImageSets[viewIndex];

                var projectionView =
                    new XrCompositionLayerProjectionView
                    {
                        Type =
                            XrTypeCompositionLayerProjectionView,

                        Next =
                            IntPtr.Zero,

                        Pose =
                            views[viewIndex].Pose,

                        Fov =
                            views[viewIndex].Fov,

                        SubImage =
                            new XrSwapchainSubImage
                            {
                                Swapchain =
                                    imageSet.Swapchain,

                                ImageRect =
                                    new XrRect2Di
                                    {
                                        Offset =
                                            new XrOffset2Di
                                            {
                                                X = 0,
                                                Y = 0
                                            },

                                        Extent =
                                            new XrExtent2Di
                                            {
                                                Width =
                                                    checked((int)imageSet.Width),

                                                Height =
                                                    checked((int)imageSet.Height)
                                            }
                                    },

                                ImageArrayIndex =
                                    0
                            }
                    };

                Marshal.StructureToPtr(
                    projectionView,
                    IntPtr.Add(
                        projectionViewsPointer,
                        viewIndex * projectionViewSize),
                    false);
            }

            var layer =
                new XrCompositionLayerProjection
                {
                    Type =
                        XrTypeCompositionLayerProjection,

                    Next =
                        IntPtr.Zero,

                    LayerFlags =
                        0,

                    Space =
                        _localSpace,

                    ViewCount =
                        2,

                    Views =
                        projectionViewsPointer
                };

            layerPointer =
                Marshal.AllocHGlobal(
                    Marshal.SizeOf<XrCompositionLayerProjection>());

            Marshal.StructureToPtr(
                layer,
                layerPointer,
                false);

            layersPointer =
                Marshal.AllocHGlobal(
                    IntPtr.Size);

            Marshal.WriteIntPtr(
                layersPointer,
                layerPointer);

            var endInfo =
                new XrFrameEndInfo
                {
                    Type =
                        XrTypeFrameEndInfo,

                    Next =
                        IntPtr.Zero,

                    DisplayTime =
                        displayTime,

                    EnvironmentBlendMode =
                        XrEnvironmentBlendModeOpaque,

                    LayerCount =
                        1,

                    Layers =
                        layersPointer
                };

            return _xrEndFrame(
                _session,
                ref endInfo);
        }
        finally
        {
            if (layersPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(
                    layersPointer);
            }

            if (layerPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(
                    layerPointer);
            }

            Marshal.FreeHGlobal(
                projectionViewsPointer);
        }
    }

    [HideFromIl2Cpp]
    private int EndFrameWithCinemaQuad(
        long displayTime)
    {
        if (_xrEndFrame is null ||
            _swapchainImageSets.Count < 1)
        {
            return -1;
        }

        var imageSet =
            _swapchainImageSets[0];

        var quad =
            new XrCompositionLayerQuad
            {
                Type =
                    XrTypeCompositionLayerQuad,

                Next =
                    IntPtr.Zero,

                LayerFlags =
                    0,

                Space =
                    _localSpace,

                EyeVisibility =
                    XrEyeVisibilityBoth,

                SubImage =
                    new XrSwapchainSubImage
                    {
                        Swapchain =
                            imageSet.Swapchain,

                        ImageRect =
                            new XrRect2Di
                            {
                                Offset =
                                    new XrOffset2Di
                                    {
                                        X = 0,
                                        Y = 0
                                    },

                                Extent =
                                    new XrExtent2Di
                                    {
                                        Width =
                                            checked((int)imageSet.Width),

                                        Height =
                                            checked((int)imageSet.Height)
                                    }
                            },

                        ImageArrayIndex =
                            0
                    },

                Pose =
                    new XrPosef
                    {
                        Orientation =
                            new XrQuaternionf
                            {
                                X = 0,
                                Y = 0,
                                Z = 0,
                                W = 1
                            },

                        Position =
                            new XrVector3f
                            {
                                X = 0,
                                Y = 0,
                                Z =
                                    -CinemaQuadDistanceMetres
                            }
                    },

                Size =
                    new XrExtent2Df
                    {
                        Width =
                            CinemaQuadWidthMetres,

                        Height =
                            CinemaQuadHeightMetres
                    }
            };

        var layerPointer =
            Marshal.AllocHGlobal(
                Marshal.SizeOf<XrCompositionLayerQuad>());

        var layersPointer =
            Marshal.AllocHGlobal(
                IntPtr.Size);

        try
        {
            Marshal.StructureToPtr(
                quad,
                layerPointer,
                false);

            Marshal.WriteIntPtr(
                layersPointer,
                layerPointer);

            var endInfo =
                new XrFrameEndInfo
                {
                    Type =
                        XrTypeFrameEndInfo,

                    Next =
                        IntPtr.Zero,

                    DisplayTime =
                        displayTime,

                    EnvironmentBlendMode =
                        XrEnvironmentBlendModeOpaque,

                    LayerCount =
                        1,

                    Layers =
                        layersPointer
                };

            return _xrEndFrame(
                _session,
                ref endInfo);
        }
        finally
        {
            Marshal.FreeHGlobal(
                layersPointer);

            Marshal.FreeHGlobal(
                layerPointer);
        }
    }

    [HideFromIl2Cpp]
    private int EndFrameWithoutLayers(
        long displayTime)
    {
        if (_xrEndFrame is null)
        {
            return -1;
        }

        var endInfo =
            new XrFrameEndInfo
            {
                Type =
                    XrTypeFrameEndInfo,

                Next =
                    IntPtr.Zero,

                DisplayTime =
                    displayTime,

                EnvironmentBlendMode =
                    XrEnvironmentBlendModeOpaque,

                LayerCount =
                    0,

                Layers =
                    IntPtr.Zero
            };

        return _xrEndFrame(
            _session,
            ref endInfo);
    }

    private static void ZeroMemory(
        IntPtr address,
        int byteCount)
    {
        var zeroes =
            new byte[byteCount];

        Marshal.Copy(
            zeroes,
            0,
            address,
            byteCount);
    }

    private static string FormatSessionState(
        int state)
    {
        return state switch
        {
            XrSessionStateUnknown => "UNKNOWN",
            XrSessionStateIdle => "IDLE",
            XrSessionStateReady => "READY",
            XrSessionStateSynchronized => "SYNCHRONIZED",
            XrSessionStateVisible => "VISIBLE",
            XrSessionStateFocused => "FOCUSED",
            XrSessionStateStopping => "STOPPING",
            XrSessionStateLossPending => "LOSS_PENDING",
            XrSessionStateExiting => "EXITING",
            _ => $"UNKNOWN_{state}"
        };
    }

    [HideFromIl2Cpp]
    private bool Fail(string message)
    {
        State = XrBackendState.Failed;
        StatusMessage = message;

        _logger.LogError(
            $"[XRBackend] {message}");

        return false;
    }

    private static string FormatLuid(
        Luid value)
    {
        var unsignedHigh =
            unchecked((uint)value.HighPart);

        return
            $"0x{unsignedHigh:X8}{value.LowPart:X8}";
    }

    private static string FormatD3DFeatureLevel(
        int value)
    {
        return value switch
        {
            0x9100 => "D3D_FEATURE_LEVEL_9_1 (0x9100)",
            0x9200 => "D3D_FEATURE_LEVEL_9_2 (0x9200)",
            0x9300 => "D3D_FEATURE_LEVEL_9_3 (0x9300)",
            0xA000 => "D3D_FEATURE_LEVEL_10_0 (0xA000)",
            0xA100 => "D3D_FEATURE_LEVEL_10_1 (0xA100)",
            0xB000 => "D3D_FEATURE_LEVEL_11_0 (0xB000)",
            0xB100 => "D3D_FEATURE_LEVEL_11_1 (0xB100)",
            0xC000 => "D3D_FEATURE_LEVEL_12_0 (0xC000)",
            0xC100 => "D3D_FEATURE_LEVEL_12_1 (0xC100)",
            0xC200 => "D3D_FEATURE_LEVEL_12_2 (0xC200)",
            _ => $"Unknown (0x{value:X})"
        };
    }

    private static byte[] CreateFixedUtf8(
        string value,
        int capacity)
    {
        var result = new byte[capacity];
        var source =
            System.Text.Encoding.UTF8.GetBytes(value);

        var length =
            Math.Min(
                source.Length,
                capacity - 1);

        Array.Copy(
            source,
            result,
            length);

        result[length] = 0;

        return result;
    }

    private static uint PackVersion(
        int major,
        int minor,
        int patch)
    {
        return
            ((uint)(major & 0x3FF) << 22) |
            ((uint)(minor & 0x3FF) << 12) |
            (uint)(patch & 0xFFF);
    }

    private static ulong MakeXrVersion(
        ulong major,
        ulong minor,
        ulong patch)
    {
        return
            (major << 48) |
            (minor << 32) |
            patch;
    }

    private static string? FindLoader()
    {
        var candidates = new[]
        {
            Path.Combine(
                Paths.PluginPath,
                "MortuaryAssistantVR",
                "openxr_loader.dll"),

            Path.Combine(
                Paths.GameRootPath,
                "openxr_loader.dll"),

            Path.Combine(
                Paths.BepInExRootPath,
                "core",
                "openxr_loader.dll")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(OpenXrNativeBackend));
        }
    }

    [HideFromIl2Cpp]
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _graphicsRequirements = default;
        _systemId = 0;
        _exitRequested = true;

        if (_sessionRunning)
        {
            EndSession();
        }

        DestroyToolUiSwapchain();

        DestroyColorSwapchains();

        if (_viewSpace != 0 &&
            _xrDestroySpace is not null)
        {
            try
            {
                var result =
                    _xrDestroySpace(
                        _viewSpace);

                _logger.LogInfo(
                    $"[XRBackend] xrDestroySpace VIEW result={result}.");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    $"[XRBackend] xrDestroySpace VIEW threw: " +
                    $"{exception.Message}");
            }

            _viewSpace =
                0;
        }

        if (_localSpace != 0 &&
            _xrDestroySpace is not null)
        {
            try
            {
                var result =
                    _xrDestroySpace(_localSpace);

                _logger.LogInfo(
                    $"[XRBackend] xrDestroySpace result={result}.");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    $"[XRBackend] xrDestroySpace threw: " +
                    $"{exception.Message}");
            }

            _localSpace = 0;
        }

        _xrGetD3D11GraphicsRequirementsKhr = null;
        _xrGetSystem = null;

        if (_session != 0 &&
            _xrDestroySession is not null)
        {
            try
            {
                var result =
                    _xrDestroySession(_session);

                _logger.LogInfo(
                    $"[XRBackend] " +
                    $"xrDestroySession result={result}.");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    $"[XRBackend] xrDestroySession threw: " +
                    $"{exception.Message}");
            }

            _session = 0;
        }

        if (_instance != 0 &&
            _xrDestroyInstance is not null)
        {
            try
            {
                var result =
                    _xrDestroyInstance(_instance);

                _logger.LogInfo(
                    $"[XRBackend] " +
                    $"xrDestroyInstance result={result}.");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    $"[XRBackend] xrDestroyInstance threw: " +
                    $"{exception.Message}");
            }

            _instance = 0;
        }

        _xrGetInstanceProcAddr = null;
        _xrCreateInstance = null;
        _xrDestroyInstance = null;
        _xrCreateSession = null;
        _xrDestroySession = null;
        _xrPollEvent = null;
        _xrBeginSession = null;
        _xrEndSession = null;
        _xrCreateReferenceSpace = null;
        _xrDestroySpace = null;
        _xrWaitFrame = null;
        _xrBeginFrame = null;
        _xrEndFrame = null;
        _xrEnumerateViewConfigurationViews = null;
        _xrEnumerateSwapchainFormats = null;
        _xrCreateSwapchain = null;
        _xrDestroySwapchain = null;
        _xrEnumerateSwapchainImages = null;
        _xrAcquireSwapchainImage = null;
        _xrWaitSwapchainImage = null;
        _xrReleaseSwapchainImage = null;
        _xrLocateViews = null;
        _xrGetInstanceProcAddrAddress =
            IntPtr.Zero;

        if (_loaderHandle != IntPtr.Zero)
        {
            NativeLibrary.Free(
                _loaderHandle);

            _loaderHandle =
                IntPtr.Zero;
        }

        State =
            XrBackendState.Disposed;

        StatusMessage =
            "Backend disposed.";
    }

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrGetInstanceProcAddrDelegate(
            ulong instance,
            [MarshalAs(UnmanagedType.LPStr)]
            string name,
            out IntPtr function);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrCreateInstanceDelegate(
            ref XrInstanceCreateInfo createInfo,
            out ulong instance);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrDestroyInstanceDelegate(
            ulong instance);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrCreateSessionDelegate(
            ulong instance,
            ref XrSessionCreateInfo createInfo,
            out ulong session);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrDestroySessionDelegate(
            ulong session);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrPollEventDelegate(
            ulong instance,
            IntPtr eventData);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrBeginSessionDelegate(
            ulong session,
            ref XrSessionBeginInfo beginInfo);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrEndSessionDelegate(
            ulong session);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrCreateReferenceSpaceDelegate(
            ulong session,
            ref XrReferenceSpaceCreateInfo createInfo,
            out ulong space);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrDestroySpaceDelegate(
            ulong space);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrWaitFrameDelegate(
            ulong session,
            ref XrFrameWaitInfo frameWaitInfo,
            ref XrFrameState frameState);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrBeginFrameDelegate(
            ulong session,
            ref XrFrameBeginInfo frameBeginInfo);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrEndFrameDelegate(
            ulong session,
            ref XrFrameEndInfo frameEndInfo);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrEnumerateViewConfigurationViewsDelegate(
            ulong instance,
            ulong systemId,
            int viewConfigurationType,
            uint viewCapacityInput,
            out uint viewCountOutput,
            IntPtr views);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrEnumerateSwapchainFormatsDelegate(
            ulong session,
            uint formatCapacityInput,
            out uint formatCountOutput,
            IntPtr formats);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrCreateSwapchainDelegate(
            ulong session,
            ref XrSwapchainCreateInfo createInfo,
            out ulong swapchain);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrDestroySwapchainDelegate(
            ulong swapchain);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrEnumerateSwapchainImagesDelegate(
            ulong swapchain,
            uint imageCapacityInput,
            out uint imageCountOutput,
            IntPtr images);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrAcquireSwapchainImageDelegate(
            ulong swapchain,
            ref XrSwapchainImageAcquireInfo acquireInfo,
            out uint index);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrWaitSwapchainImageDelegate(
            ulong swapchain,
            ref XrSwapchainImageWaitInfo waitInfo);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrReleaseSwapchainImageDelegate(
            ulong swapchain,
            ref XrSwapchainImageReleaseInfo releaseInfo);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrLocateViewsDelegate(
            ulong session,
            ref XrViewLocateInfo viewLocateInfo,
            ref XrViewState viewState,
            uint viewCapacityInput,
            out uint viewCountOutput,
            IntPtr views);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrGetSystemDelegate(
            ulong instance,
            ref XrSystemGetInfo getInfo,
            out ulong systemId);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int
        XrGetD3D11GraphicsRequirementsKhrDelegate(
            ulong instance,
            ulong systemId,
            ref XrGraphicsRequirementsD3D11Khr
                graphicsRequirements);

    [StructLayout(LayoutKind.Sequential)]
    private struct XrApplicationInfo
    {
        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst =
                XrMaxApplicationNameSize)]
        public byte[] ApplicationName;

        public uint ApplicationVersion;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst =
                XrMaxEngineNameSize)]
        public byte[] EngineName;

        public uint EngineVersion;
        public ulong ApiVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrInstanceCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public ulong CreateFlags;
        public XrApplicationInfo ApplicationInfo;
        public uint EnabledApiLayerCount;
        public IntPtr EnabledApiLayerNames;
        public uint EnabledExtensionCount;
        public IntPtr EnabledExtensionNames;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSystemGetInfo
    {
        public int Type;
        public IntPtr Next;
        public int FormFactor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrEventDataSessionStateChanged
    {
        public int Type;
        public IntPtr Next;
        public ulong Session;
        public int State;
        public long Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSessionBeginInfo
    {
        public int Type;
        public IntPtr Next;
        public int PrimaryViewConfigurationType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrReferenceSpaceCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public int ReferenceSpaceType;
        public XrPosef PoseInReferenceSpace;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrPosef
    {
        public XrQuaternionf Orientation;
        public XrVector3f Position;

        public static XrPosef Identity =>
            new()
            {
                Orientation =
                    new XrQuaternionf
                    {
                        X = 0,
                        Y = 0,
                        Z = 0,
                        W = 1
                    },

                Position =
                    new XrVector3f
                    {
                        X = 0,
                        Y = 0,
                        Z = 0
                    }
            };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrQuaternionf
    {
        public float X;
        public float Y;
        public float Z;
        public float W;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrVector3f
    {
        public float X;
        public float Y;
        public float Z;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrFrameWaitInfo
    {
        public int Type;
        public IntPtr Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrFrameState
    {
        public int Type;
        public IntPtr Next;
        public long PredictedDisplayTime;
        public long PredictedDisplayPeriod;
        public uint ShouldRender;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrFrameBeginInfo
    {
        public int Type;
        public IntPtr Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrFrameEndInfo
    {
        public int Type;
        public IntPtr Next;
        public long DisplayTime;
        public int EnvironmentBlendMode;
        public uint LayerCount;
        public IntPtr Layers;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrViewLocateInfo
    {
        public int Type;
        public IntPtr Next;
        public int ViewConfigurationType;
        public long DisplayTime;
        public ulong Space;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrViewState
    {
        public int Type;
        public IntPtr Next;
        public ulong ViewStateFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrView
    {
        public int Type;
        public IntPtr Next;
        public XrPosef Pose;
        public XrFovf Fov;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrFovf
    {
        public float AngleLeft;
        public float AngleRight;
        public float AngleUp;
        public float AngleDown;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainImageAcquireInfo
    {
        public int Type;
        public IntPtr Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainImageWaitInfo
    {
        public int Type;
        public IntPtr Next;
        public long Timeout;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainImageReleaseInfo
    {
        public int Type;
        public IntPtr Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrCompositionLayerQuad
    {
        public int Type;
        public IntPtr Next;
        public ulong LayerFlags;
        public ulong Space;
        public int EyeVisibility;
        public XrSwapchainSubImage SubImage;
        public XrPosef Pose;
        public XrExtent2Df Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrExtent2Df
    {
        public float Width;
        public float Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrCompositionLayerProjection
    {
        public int Type;
        public IntPtr Next;
        public ulong LayerFlags;
        public ulong Space;
        public uint ViewCount;
        public IntPtr Views;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrCompositionLayerProjectionView
    {
        public int Type;
        public IntPtr Next;
        public XrPosef Pose;
        public XrFovf Fov;
        public XrSwapchainSubImage SubImage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainSubImage
    {
        public ulong Swapchain;
        public XrRect2Di ImageRect;
        public uint ImageArrayIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrRect2Di
    {
        public XrOffset2Di Offset;
        public XrExtent2Di Extent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrOffset2Di
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrExtent2Di
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrViewConfigurationView
    {
        public int Type;
        public IntPtr Next;
        public uint RecommendedImageRectWidth;
        public uint MaxImageRectWidth;
        public uint RecommendedImageRectHeight;
        public uint MaxImageRectHeight;
        public uint RecommendedSwapchainSampleCount;
        public uint MaxSwapchainSampleCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public ulong CreateFlags;
        public ulong UsageFlags;
        public long Format;
        public uint SampleCount;
        public uint Width;
        public uint Height;
        public uint FaceCount;
        public uint ArraySize;
        public uint MipCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainImageD3D11Khr
    {
        public int Type;
        public IntPtr Next;
        public IntPtr Texture;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSessionCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public ulong CreateFlags;
        public ulong SystemId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrGraphicsBindingD3D11Khr
    {
        public int Type;
        public IntPtr Next;
        public IntPtr Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrGraphicsRequirementsD3D11Khr
    {
        public int Type;
        public IntPtr Next;
        public Luid AdapterLuid;
        public int MinFeatureLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }
}

using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;

namespace MortuaryAssistantVR.XR;

internal sealed class OpenXrNativeBackend : IXrBackend
{
    private const int XrSuccess = 0;

    private const int XrTypeInstanceCreateInfo = 3;
    private const int XrTypeSystemGetInfo = 4;
    private const int XrTypeGraphicsRequirementsD3D11Khr = 1000027002;

    private const int XrFormFactorHeadMountedDisplay = 1;

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
    private XrGetD3D11GraphicsRequirementsKhrDelegate?
        _xrGetD3D11GraphicsRequirementsKhr;

    private ulong _instance;
    private ulong _systemId;
    private XrGraphicsRequirementsD3D11Khr _graphicsRequirements;
    private bool _disposed;

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

        return ValidateUnityD3D11Device();
    }

    [HideFromIl2Cpp]
    private bool ValidateUnityD3D11Device()
    {
        _logger.LogInfo(
            "[XRBackend] Querying Unity's active D3D11 device " +
            "through the native bridge.");

        if (!UnityD3D11Bridge.TryGetDeviceInfo(
                _logger,
                out var deviceInfo))
        {
            return Fail(
                "Unity D3D11 bridge could not provide the active device.");
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

        var runtimeFeatureLevel =
            _graphicsRequirements.MinFeatureLevel;

        var unityFeatureLevel =
            deviceInfo.FeatureLevel;

        var adapterMatches =
            _graphicsRequirements.AdapterLuid.LowPart ==
                deviceInfo.AdapterLuidLowPart &&
            _graphicsRequirements.AdapterLuid.HighPart ==
                deviceInfo.AdapterLuidHighPart;

        var featureLevelMatches =
            unityFeatureLevel >= runtimeFeatureLevel;

        _logger.LogInfo(
            $"[XRBackend] Unity D3D11 device=0x" +
            $"{deviceInfo.DevicePointer.ToInt64():X}, " +
            $"adapterLuid={unityLuid}, " +
            $"featureLevel=" +
            $"{FormatD3DFeatureLevel(unityFeatureLevel)}");

        _logger.LogInfo(
            $"[XRBackend] D3D11 comparison: " +
            $"adapterMatches={adapterMatches}, " +
            $"featureLevelMatches={featureLevelMatches}, " +
            $"runtimeAdapter={runtimeLuid}, " +
            $"unityAdapter={unityLuid}");

        if (!adapterMatches)
        {
            return Fail(
                "Unity is rendering on a different GPU adapter " +
                "than the OpenXR runtime requires.");
        }

        if (!featureLevelMatches)
        {
            return Fail(
                "Unity's D3D11 feature level is below the " +
                "minimum required by the OpenXR runtime.");
        }

        State =
            XrBackendState.UnityGraphicsDeviceReady;

        StatusMessage =
            "Unity's active D3D11 device matches the " +
            "OpenXR graphics requirements.";

        _logger.LogInfo(
            $"[XRBackend] {StatusMessage}");

        return true;
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

        _xrGetD3D11GraphicsRequirementsKhr = null;
        _xrGetSystem = null;

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

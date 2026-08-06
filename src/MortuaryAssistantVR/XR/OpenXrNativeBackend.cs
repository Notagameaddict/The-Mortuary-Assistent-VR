using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;

namespace MortuaryAssistantVR.XR;

internal sealed class OpenXrNativeBackend : IXrBackend
{
    private const int XrSuccess = 0;
    private const int XrTypeInstanceCreateInfo = 3;
    private const int XrMaxApplicationNameSize = 128;
    private const int XrMaxEngineNameSize = 128;

    private readonly ManualLogSource _logger;
    private IntPtr _loaderHandle;
    private IntPtr _xrGetInstanceProcAddr;
    private XrCreateInstanceDelegate? _xrCreateInstance;
    private XrDestroyInstanceDelegate? _xrDestroyInstance;
    private ulong _instance;
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
            StatusMessage = "No application-local openxr_loader.dll was found.";
            _logger.LogWarning($"[XRBackend] {StatusMessage}");
            return false;
        }

        _logger.LogInfo($"[XRBackend] Loading OpenXR loader from '{loaderPath}'.");

        if (!NativeLibrary.TryLoad(loaderPath, out _loaderHandle))
        {
            State = XrBackendState.Failed;
            StatusMessage = "NativeLibrary.TryLoad failed for openxr_loader.dll.";
            return false;
        }

        State = XrBackendState.LoaderLoaded;
        StatusMessage = "OpenXR loader loaded.";

        if (!TryResolveExports())
        {
            return false;
        }

        State = XrBackendState.EntryPointResolved;
        StatusMessage = "OpenXR core entry points resolved.";

        if (!attemptStartup)
        {
            State = XrBackendState.StartupDisabled;
            StatusMessage =
                "OpenXR loader is ready; instance creation is disabled by config.";
            return true;
        }

        return CreateInstance();
    }

    [HideFromIl2Cpp]
    private bool TryResolveExports()
    {
        if (!NativeLibrary.TryGetExport(
                _loaderHandle,
                "xrGetInstanceProcAddr",
                out _xrGetInstanceProcAddr))
        {
            State = XrBackendState.Failed;
            StatusMessage = "xrGetInstanceProcAddr was not exported by the loader.";
            return false;
        }

        if (!NativeLibrary.TryGetExport(
                _loaderHandle,
                "xrCreateInstance",
                out var createInstanceAddress))
        {
            State = XrBackendState.Failed;
            StatusMessage = "xrCreateInstance was not exported by the loader.";
            return false;
        }

        if (!NativeLibrary.TryGetExport(
                _loaderHandle,
                "xrDestroyInstance",
                out var destroyInstanceAddress))
        {
            State = XrBackendState.Failed;
            StatusMessage = "xrDestroyInstance was not exported by the loader.";
            return false;
        }

        _xrCreateInstance =
            Marshal.GetDelegateForFunctionPointer<XrCreateInstanceDelegate>(
                createInstanceAddress);

        _xrDestroyInstance =
            Marshal.GetDelegateForFunctionPointer<XrDestroyInstanceDelegate>(
                destroyInstanceAddress);

        _logger.LogInfo(
            $"[XRBackend] xrGetInstanceProcAddr=0x{_xrGetInstanceProcAddr.ToInt64():X}");
        _logger.LogInfo(
            $"[XRBackend] xrCreateInstance=0x{createInstanceAddress.ToInt64():X}");
        _logger.LogInfo(
            $"[XRBackend] xrDestroyInstance=0x{destroyInstanceAddress.ToInt64():X}");

        return true;
    }

    [HideFromIl2Cpp]
    private bool CreateInstance()
    {
        if (_xrCreateInstance is null)
        {
            State = XrBackendState.Failed;
            StatusMessage = "xrCreateInstance delegate is unavailable.";
            return false;
        }

        var createInfo = new XrInstanceCreateInfo
        {
            Type = XrTypeInstanceCreateInfo,
            Next = IntPtr.Zero,
            CreateFlags = 0,
            ApplicationInfo = new XrApplicationInfo
            {
                ApplicationName =
                    CreateFixedUtf8("The Mortuary Assistant VR",
                        XrMaxApplicationNameSize),
                ApplicationVersion = PackVersion(0, 8, 0),
                EngineName =
                    CreateFixedUtf8("Unity/BepInEx",
                        XrMaxEngineNameSize),
                EngineVersion = PackVersion(2021, 2, 4),
                ApiVersion = MakeXrVersion(1, 0, 0)
            },
            EnabledApiLayerCount = 0,
            EnabledApiLayerNames = IntPtr.Zero,
            EnabledExtensionCount = 0,
            EnabledExtensionNames = IntPtr.Zero
        };

        _logger.LogInfo(
            "[XRBackend] Calling xrCreateInstance with OpenXR 1.0 " +
            "and no API layers or extensions.");

        var result = _xrCreateInstance(ref createInfo, out _instance);

        _logger.LogInfo(
            $"[XRBackend] xrCreateInstance result={result}, instance=0x{_instance:X}");

        if (result != XrSuccess || _instance == 0)
        {
            State = XrBackendState.InstanceCreationFailed;
            StatusMessage = $"xrCreateInstance failed with XrResult {result}.";
            _instance = 0;
            return false;
        }

        State = XrBackendState.InstanceCreated;
        StatusMessage = "OpenXR instance created successfully.";
        return true;
    }

    private static byte[] CreateFixedUtf8(string value, int capacity)
    {
        var result = new byte[capacity];
        var source = System.Text.Encoding.UTF8.GetBytes(value);
        var length = Math.Min(source.Length, capacity - 1);
        Array.Copy(source, result, length);
        result[length] = 0;
        return result;
    }

    private static uint PackVersion(int major, int minor, int patch)
    {
        return
            ((uint)(major & 0x3FF) << 22) |
            ((uint)(minor & 0x3FF) << 12) |
            (uint)(patch & 0xFFF);
    }

    private static ulong MakeXrVersion(ulong major, ulong minor, ulong patch)
    {
        return (major << 48) | (minor << 32) | patch;
    }

    private static string? FindLoader()
    {
        var candidates = new[]
        {
            Path.Combine(Paths.PluginPath, "MortuaryAssistantVR", "openxr_loader.dll"),
            Path.Combine(Paths.GameRootPath, "openxr_loader.dll"),
            Path.Combine(Paths.BepInExRootPath, "core", "openxr_loader.dll")
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
            throw new ObjectDisposedException(nameof(OpenXrNativeBackend));
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

        if (_instance != 0 && _xrDestroyInstance is not null)
        {
            try
            {
                var result = _xrDestroyInstance(_instance);
                _logger.LogInfo(
                    $"[XRBackend] xrDestroyInstance result={result}.");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    $"[XRBackend] xrDestroyInstance threw: {exception.Message}");
            }

            _instance = 0;
        }

        _xrCreateInstance = null;
        _xrDestroyInstance = null;
        _xrGetInstanceProcAddr = IntPtr.Zero;

        if (_loaderHandle != IntPtr.Zero)
        {
            NativeLibrary.Free(_loaderHandle);
            _loaderHandle = IntPtr.Zero;
        }

        State = XrBackendState.Disposed;
        StatusMessage = "Backend disposed.";
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XrCreateInstanceDelegate(
        ref XrInstanceCreateInfo createInfo,
        out ulong instance);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XrDestroyInstanceDelegate(ulong instance);

    [StructLayout(LayoutKind.Sequential)]
    private struct XrApplicationInfo
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = XrMaxApplicationNameSize)]
        public byte[] ApplicationName;

        public uint ApplicationVersion;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = XrMaxEngineNameSize)]
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
}

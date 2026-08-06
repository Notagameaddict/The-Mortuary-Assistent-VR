using System.Runtime.InteropServices;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;

namespace MortuaryAssistantVR.XR;

internal static class UnityD3D11Bridge
{
    private const string LibraryName =
        "MortuaryAssistantVR.UnityD3D11Bridge";

    [HideFromIl2Cpp]
    internal static bool TryGetDeviceInfo(
        ManualLogSource logger,
        out UnityD3D11DeviceInfo deviceInfo)
    {
        deviceInfo = default;

        try
        {
            var result =
                MavrGetD3D11DeviceInfo(
                    out var devicePointer,
                    out var luidLowPart,
                    out var luidHighPart,
                    out var featureLevel);

            logger.LogInfo(
                $"[UnityD3D11Bridge] Native result={result}, " +
                $"device=0x{devicePointer.ToInt64():X}, " +
                $"luidHigh=0x{unchecked((uint)luidHighPart):X8}, " +
                $"luidLow=0x{luidLowPart:X8}, " +
                $"featureLevel=0x{featureLevel:X}");

            if (result != 0 ||
                devicePointer == IntPtr.Zero)
            {
                logger.LogError(
                    "[UnityD3D11Bridge] The native bridge " +
                    "did not return a valid Unity D3D11 device.");

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
        catch (DllNotFoundException exception)
        {
            logger.LogError(
                "[UnityD3D11Bridge] Native bridge DLL was not found: " +
                exception.Message);

            return false;
        }
        catch (EntryPointNotFoundException exception)
        {
            logger.LogError(
                "[UnityD3D11Bridge] Native bridge export was not found: " +
                exception.Message);

            return false;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "[UnityD3D11Bridge] Device query failed: " +
                exception);

            return false;
        }
    }

    [DllImport(
        LibraryName,
        EntryPoint = "MAVR_GetD3D11DeviceInfo",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int MavrGetD3D11DeviceInfo(
        out IntPtr devicePointer,
        out uint adapterLuidLowPart,
        out int adapterLuidHighPart,
        out int featureLevel);
}

internal readonly record struct UnityD3D11DeviceInfo(
    IntPtr DevicePointer,
    uint AdapterLuidLowPart,
    int AdapterLuidHighPart,
    int FeatureLevel);

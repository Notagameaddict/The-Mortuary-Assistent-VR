using System.Runtime.InteropServices;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;

namespace MortuaryAssistantVR.XR;

internal static class D3D11PresentHookProbe
{
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
        out UnityD3D11DeviceInfo deviceInfo)
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

            logger.LogInfo(
                $"[PresentHookProbe] Query result={result}, " +
                $"device=0x{devicePointer.ToInt64():X}, " +
                $"luidHigh=0x{unchecked((uint)luidHighPart):X8}, " +
                $"luidLow=0x{luidLowPart:X8}, " +
                $"featureLevel=0x{featureLevel:X}.");

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
}

#include <cstdint>
#include <d3d11.h>
#include <dxgi.h>

#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsD3D11.h"

static IUnityInterfaces* g_unityInterfaces = nullptr;
static IUnityGraphics* g_unityGraphics = nullptr;
static IUnityGraphicsD3D11* g_unityD3D11 = nullptr;

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
UnityPluginLoad(IUnityInterfaces* unityInterfaces)
{
    g_unityInterfaces = unityInterfaces;

    if (g_unityInterfaces == nullptr)
    {
        return;
    }

    g_unityGraphics =
        g_unityInterfaces->Get<IUnityGraphics>();

    g_unityD3D11 =
        g_unityInterfaces->Get<IUnityGraphicsD3D11>();
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
UnityPluginUnload()
{
    g_unityD3D11 = nullptr;
    g_unityGraphics = nullptr;
    g_unityInterfaces = nullptr;
}

extern "C" __declspec(dllexport) int __cdecl
MAVR_GetD3D11DeviceInfo(
    void** devicePointer,
    std::uint32_t* adapterLuidLowPart,
    std::int32_t* adapterLuidHighPart,
    std::int32_t* featureLevel)
{
    if (devicePointer == nullptr ||
        adapterLuidLowPart == nullptr ||
        adapterLuidHighPart == nullptr ||
        featureLevel == nullptr)
    {
        return 1;
    }

    *devicePointer = nullptr;
    *adapterLuidLowPart = 0;
    *adapterLuidHighPart = 0;
    *featureLevel = 0;

    if (g_unityD3D11 == nullptr)
    {
        return 2;
    }

    ID3D11Device* device =
        g_unityD3D11->GetDevice();

    if (device == nullptr)
    {
        return 3;
    }

    IDXGIDevice* dxgiDevice = nullptr;
    HRESULT result =
        device->QueryInterface(
            __uuidof(IDXGIDevice),
            reinterpret_cast<void**>(&dxgiDevice));

    if (FAILED(result) || dxgiDevice == nullptr)
    {
        return 4;
    }

    IDXGIAdapter* adapter = nullptr;
    result = dxgiDevice->GetAdapter(&adapter);
    dxgiDevice->Release();

    if (FAILED(result) || adapter == nullptr)
    {
        return 5;
    }

    DXGI_ADAPTER_DESC adapterDescription = {};
    result =
        adapter->GetDesc(&adapterDescription);

    adapter->Release();

    if (FAILED(result))
    {
        return 6;
    }

    *devicePointer = device;
    *adapterLuidLowPart =
        adapterDescription.AdapterLuid.LowPart;
    *adapterLuidHighPart =
        adapterDescription.AdapterLuid.HighPart;
    *featureLevel =
        static_cast<std::int32_t>(
            device->GetFeatureLevel());

    return 0;
}

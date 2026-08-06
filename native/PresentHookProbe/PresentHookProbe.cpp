#include <Windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <cstdint>
#include <mutex>

#include "MinHook.h"

using PresentFunction =
    HRESULT(__stdcall*)(IDXGISwapChain*, UINT, UINT);

static PresentFunction g_originalPresent = nullptr;
static std::mutex g_mutex;

static ID3D11Device* g_capturedDevice = nullptr;
static LUID g_adapterLuid = {};
static D3D_FEATURE_LEVEL g_featureLevel =
    static_cast<D3D_FEATURE_LEVEL>(0);

static HRESULT __stdcall HookedPresent(
    IDXGISwapChain* swapChain,
    UINT syncInterval,
    UINT flags)
{
    if (swapChain != nullptr &&
        g_capturedDevice == nullptr)
    {
        ID3D11Device* device = nullptr;

        if (SUCCEEDED(
                swapChain->GetDevice(
                    __uuidof(ID3D11Device),
                    reinterpret_cast<void**>(&device))) &&
            device != nullptr)
        {
            IDXGIDevice* dxgiDevice = nullptr;
            IDXGIAdapter* adapter = nullptr;

            if (SUCCEEDED(
                    device->QueryInterface(
                        __uuidof(IDXGIDevice),
                        reinterpret_cast<void**>(&dxgiDevice))) &&
                dxgiDevice != nullptr &&
                SUCCEEDED(dxgiDevice->GetAdapter(&adapter)) &&
                adapter != nullptr)
            {
                DXGI_ADAPTER_DESC description = {};

                if (SUCCEEDED(adapter->GetDesc(&description)))
                {
                    std::lock_guard<std::mutex> guard(g_mutex);

                    if (g_capturedDevice == nullptr)
                    {
                        g_capturedDevice = device;
                        g_capturedDevice->AddRef();
                        g_adapterLuid =
                            description.AdapterLuid;
                        g_featureLevel =
                            device->GetFeatureLevel();
                    }
                }

                adapter->Release();
            }

            if (dxgiDevice != nullptr)
            {
                dxgiDevice->Release();
            }

            device->Release();
        }
    }

    return g_originalPresent(
        swapChain,
        syncInterval,
        flags);
}

static LRESULT CALLBACK ProbeWindowProcedure(
    HWND window,
    UINT message,
    WPARAM wParam,
    LPARAM lParam)
{
    return DefWindowProc(
        window,
        message,
        wParam,
        lParam);
}

static bool GetPresentAddress(
    void** presentAddress)
{
    if (presentAddress == nullptr)
    {
        return false;
    }

    *presentAddress = nullptr;

    HINSTANCE instance =
        GetModuleHandle(nullptr);

    const wchar_t* className =
        L"MortuaryAssistantVRPresentProbe";

    WNDCLASSW windowClass = {};
    windowClass.lpfnWndProc =
        ProbeWindowProcedure;
    windowClass.hInstance =
        instance;
    windowClass.lpszClassName =
        className;

    RegisterClassW(&windowClass);

    HWND window =
        CreateWindowExW(
            0,
            className,
            L"Probe",
            WS_OVERLAPPEDWINDOW,
            0,
            0,
            100,
            100,
            nullptr,
            nullptr,
            instance,
            nullptr);

    if (window == nullptr)
    {
        return false;
    }

    DXGI_SWAP_CHAIN_DESC description = {};
    description.BufferCount = 1;
    description.BufferDesc.Width = 100;
    description.BufferDesc.Height = 100;
    description.BufferDesc.Format =
        DXGI_FORMAT_R8G8B8A8_UNORM;
    description.BufferUsage =
        DXGI_USAGE_RENDER_TARGET_OUTPUT;
    description.OutputWindow =
        window;
    description.SampleDesc.Count = 1;
    description.Windowed = TRUE;
    description.SwapEffect =
        DXGI_SWAP_EFFECT_DISCARD;

    D3D_FEATURE_LEVEL requestedLevels[] =
    {
        D3D_FEATURE_LEVEL_11_0
    };

    IDXGISwapChain* swapChain = nullptr;
    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* context = nullptr;
    D3D_FEATURE_LEVEL createdLevel = {};

    HRESULT result =
        D3D11CreateDeviceAndSwapChain(
            nullptr,
            D3D_DRIVER_TYPE_HARDWARE,
            nullptr,
            0,
            requestedLevels,
            1,
            D3D11_SDK_VERSION,
            &description,
            &swapChain,
            &device,
            &createdLevel,
            &context);

    if (FAILED(result) ||
        swapChain == nullptr)
    {
        if (context != nullptr)
        {
            context->Release();
        }

        if (device != nullptr)
        {
            device->Release();
        }

        DestroyWindow(window);
        UnregisterClassW(className, instance);
        return false;
    }

    void** virtualTable =
        *reinterpret_cast<void***>(swapChain);

    *presentAddress = virtualTable[8];

    context->Release();
    device->Release();
    swapChain->Release();

    DestroyWindow(window);
    UnregisterClassW(className, instance);

    return *presentAddress != nullptr;
}

extern "C" __declspec(dllexport)
int __cdecl MAVR_InstallPresentHook()
{
    if (g_originalPresent != nullptr)
    {
        return 0;
    }

    void* presentAddress = nullptr;

    if (!GetPresentAddress(&presentAddress))
    {
        return 1;
    }

    if (MH_Initialize() != MH_OK &&
        MH_Initialize() != MH_ERROR_ALREADY_INITIALIZED)
    {
        return 2;
    }

    const MH_STATUS createStatus =
        MH_CreateHook(
            presentAddress,
            reinterpret_cast<void*>(&HookedPresent),
            reinterpret_cast<void**>(&g_originalPresent));

    if (createStatus != MH_OK &&
        createStatus != MH_ERROR_ALREADY_CREATED)
    {
        return 3;
    }

    const MH_STATUS enableStatus =
        MH_EnableHook(presentAddress);

    if (enableStatus != MH_OK &&
        enableStatus != MH_ERROR_ENABLED)
    {
        return 4;
    }

    return 0;
}

extern "C" __declspec(dllexport)
int __cdecl MAVR_GetCapturedD3D11DeviceInfo(
    void** devicePointer,
    std::uint32_t* luidLowPart,
    std::int32_t* luidHighPart,
    std::int32_t* featureLevel)
{
    if (devicePointer == nullptr ||
        luidLowPart == nullptr ||
        luidHighPart == nullptr ||
        featureLevel == nullptr)
    {
        return 1;
    }

    std::lock_guard<std::mutex> guard(g_mutex);

    if (g_capturedDevice == nullptr)
    {
        *devicePointer = nullptr;
        *luidLowPart = 0;
        *luidHighPart = 0;
        *featureLevel = 0;
        return 2;
    }

    *devicePointer = g_capturedDevice;
    *luidLowPart = g_adapterLuid.LowPart;
    *luidHighPart = g_adapterLuid.HighPart;
    *featureLevel =
        static_cast<std::int32_t>(g_featureLevel);

    return 0;
}


extern "C" __declspec(dllexport) int __cdecl
MAVR_ClearD3D11Texture(
    void* texturePointer,
    std::int64_t dxgiFormat,
    float red,
    float green,
    float blue,
    float alpha,
    std::int32_t* createViewHResult)
{
    if (createViewHResult != nullptr)
    {
        *createViewHResult = S_OK;
    }

    if (texturePointer == nullptr)
    {
        return 1;
    }

    ID3D11Texture2D* texture =
        reinterpret_cast<ID3D11Texture2D*>(texturePointer);

    D3D11_TEXTURE2D_DESC textureDescription = {};
    texture->GetDesc(&textureDescription);

    ID3D11Device* device = nullptr;
    texture->GetDevice(&device);

    if (device == nullptr)
    {
        return 2;
    }

    ID3D11DeviceContext* context = nullptr;
    device->GetImmediateContext(&context);

    if (context == nullptr)
    {
        device->Release();
        return 3;
    }

    D3D11_RENDER_TARGET_VIEW_DESC viewDescription = {};
    viewDescription.Format =
        static_cast<DXGI_FORMAT>(dxgiFormat);

    if (textureDescription.ArraySize > 1)
    {
        viewDescription.ViewDimension =
            D3D11_RTV_DIMENSION_TEXTURE2DARRAY;

        viewDescription.Texture2DArray.MipSlice = 0;
        viewDescription.Texture2DArray.FirstArraySlice = 0;
        viewDescription.Texture2DArray.ArraySize = 1;
    }
    else if (textureDescription.SampleDesc.Count > 1)
    {
        viewDescription.ViewDimension =
            D3D11_RTV_DIMENSION_TEXTURE2DMS;
    }
    else
    {
        viewDescription.ViewDimension =
            D3D11_RTV_DIMENSION_TEXTURE2D;

        viewDescription.Texture2D.MipSlice = 0;
    }

    ID3D11RenderTargetView* renderTargetView = nullptr;

    HRESULT viewResult =
        device->CreateRenderTargetView(
            texture,
            &viewDescription,
            &renderTargetView);

    if (createViewHResult != nullptr)
    {
        *createViewHResult =
            static_cast<std::int32_t>(viewResult);
    }

    if (FAILED(viewResult) ||
        renderTargetView == nullptr)
    {
        context->Release();
        device->Release();
        return 4;
    }

    const float color[4] =
    {
        red,
        green,
        blue,
        alpha
    };

    context->ClearRenderTargetView(
        renderTargetView,
        color);

    context->Flush();

    renderTargetView->Release();
    context->Release();
    device->Release();

    return 0;
}

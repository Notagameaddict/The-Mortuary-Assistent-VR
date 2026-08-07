#include <Windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <dxgi.h>
#include <atomic>
#include <cstdint>
#include <mutex>

#include "MinHook.h"

using PresentFunction =
    HRESULT(__stdcall*)(IDXGISwapChain*, UINT, UINT);

using PresentFrameCallback =
    void(__cdecl*)();

static PresentFunction g_originalPresent = nullptr;

static std::atomic<PresentFrameCallback>
    g_presentFrameCallback = nullptr;

static thread_local bool
    g_insidePresentFrameCallback = false;
static std::mutex g_mutex;

static ID3D11Device* g_capturedDevice = nullptr;
static IDXGISwapChain* g_capturedSwapChain = nullptr;
static HWND g_capturedWindow = nullptr;

static std::mutex g_stereoSourceMutex;
static ID3D11Texture2D* g_leftEyeSourceTexture = nullptr;
static ID3D11Texture2D* g_rightEyeSourceTexture = nullptr;

static ID3D11Device* g_blitDevice = nullptr;
static ID3D11VertexShader* g_blitVertexShader = nullptr;
static ID3D11PixelShader* g_blitPixelShader = nullptr;
static ID3D11PixelShader* g_blitPixelShaderFlipped = nullptr;
static ID3D11SamplerState* g_blitSampler = nullptr;
static ID3D11Buffer* g_cursorConstantBuffer = nullptr;
static std::atomic<int> g_interactionPromptVisible = 0;

struct CursorConstants
{
    float CursorX;
    float CursorY;
    float CursorVisible;
    float InteractionVisible;
};
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

                        g_capturedSwapChain = swapChain;
                        g_capturedSwapChain->AddRef();

                        DXGI_SWAP_CHAIN_DESC swapChainDescription = {};

                        if (SUCCEEDED(
                                swapChain->GetDesc(
                                    &swapChainDescription)))
                        {
                            g_capturedWindow =
                                swapChainDescription.OutputWindow;
                        }

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

    PresentFrameCallback frameCallback =
        g_presentFrameCallback.load(
            std::memory_order_acquire);

    if (frameCallback != nullptr &&
        !g_insidePresentFrameCallback)
    {
        g_insidePresentFrameCallback =
            true;

        frameCallback();

        g_insidePresentFrameCallback =
            false;
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


static DXGI_FORMAT NormalizeShaderResourceFormat(
    DXGI_FORMAT format)
{
    switch (format)
    {
        case DXGI_FORMAT_R8G8B8A8_TYPELESS:
            return DXGI_FORMAT_R8G8B8A8_UNORM;

        case DXGI_FORMAT_B8G8R8A8_TYPELESS:
            return DXGI_FORMAT_B8G8R8A8_UNORM;

        case DXGI_FORMAT_B8G8R8X8_TYPELESS:
            return DXGI_FORMAT_B8G8R8X8_UNORM;

        default:
            return format;
    }
}

static void ReleaseBlitResources()
{
    if (g_cursorConstantBuffer != nullptr)
    {
        g_cursorConstantBuffer->Release();
        g_cursorConstantBuffer = nullptr;
    }

    if (g_blitSampler != nullptr)
    {
        g_blitSampler->Release();
        g_blitSampler = nullptr;
    }

    if (g_blitPixelShaderFlipped != nullptr)
    {
        g_blitPixelShaderFlipped->Release();
        g_blitPixelShaderFlipped = nullptr;
    }

    if (g_blitPixelShader != nullptr)
    {
        g_blitPixelShader->Release();
        g_blitPixelShader = nullptr;
    }

    if (g_blitVertexShader != nullptr)
    {
        g_blitVertexShader->Release();
        g_blitVertexShader = nullptr;
    }

    if (g_blitDevice != nullptr)
    {
        g_blitDevice->Release();
        g_blitDevice = nullptr;
    }
}

static HRESULT EnsureBlitResources(
    ID3D11Device* device)
{
    if (device == nullptr)
    {
        return E_INVALIDARG;
    }

    if (g_blitDevice == device &&
        g_blitVertexShader != nullptr &&
        g_blitPixelShader != nullptr &&
        g_blitPixelShaderFlipped != nullptr &&
        g_blitSampler != nullptr &&
        g_cursorConstantBuffer != nullptr)
    {
        return S_OK;
    }

    ReleaseBlitResources();

    static const char* vertexShaderSource =
        "struct VSOut { float4 position : SV_POSITION; "
        "float2 uv : TEXCOORD0; };"
        "VSOut main(uint id : SV_VertexID) {"
        "VSOut o;"
        "float2 p = float2((id << 1) & 2, id & 2);"
        "o.uv = p;"
        "o.position = float4(p * float2(2,-2) + float2(-1,1), 0, 1);"
        "return o;"
        "}";

    static const char* pixelShaderSource = R"HLSL(
Texture2D sourceTexture : register(t0);
SamplerState sourceSampler : register(s0);

cbuffer CursorBuffer : register(b0)
{
    float2 cursorUv;
    float cursorVisible;
    float interactionVisible;
};

float4 DrawInteraction(
    float4 color,
    float2 uv)
{
    if (interactionVisible < 0.5)
    {
        return color;
    }

    float2 p =
        (uv - float2(0.5, 0.5)) *
        float2(1.0, 1.7778);

    float radius =
        length(p);

    float outerRing =
        1.0 -
        smoothstep(
            0.020,
            0.023,
            radius);

    float innerCutout =
        1.0 -
        smoothstep(
            0.012,
            0.015,
            radius);

    float ring =
        saturate(
            outerRing -
            innerCutout);

    float centreDot =
        1.0 -
        smoothstep(
            0.0025,
            0.0045,
            radius);

    float horizontal =
        (1.0 -
         smoothstep(
             0.0015,
             0.0030,
             abs(p.y))) *
        (1.0 -
         smoothstep(
             0.010,
             0.020,
             abs(p.x)));

    float vertical =
        (1.0 -
         smoothstep(
             0.0015,
             0.0030,
             abs(p.x))) *
        (1.0 -
         smoothstep(
             0.010,
             0.020,
             abs(p.y)));

    float reticle =
        saturate(
            ring +
            centreDot +
            horizontal +
            vertical);

    float outline =
        1.0 -
        smoothstep(
            0.024,
            0.028,
            radius);

    color.rgb =
        lerp(
            color.rgb,
            float3(0.0, 0.0, 0.0),
            outline * 0.85);

    color.rgb =
        lerp(
            color.rgb,
            float3(1.0, 1.0, 1.0),
            reticle);

    return color;
}

float4 DrawCursor(
    float4 color,
    float2 uv)
{
    if (cursorVisible < 0.5)
    {
        return color;
    }

    float2 delta =
        abs(uv - cursorUv);

    float cursorCross =
        step(delta.x, 0.0025) *
        step(delta.y, 0.018) +
        step(delta.y, 0.0025) *
        step(delta.x, 0.018);

    float cursorOutline =
        step(delta.x, 0.0045) *
        step(delta.y, 0.021) +
        step(delta.y, 0.0045) *
        step(delta.x, 0.021);

    color.rgb =
        lerp(
            color.rgb,
            float3(0.0, 0.0, 0.0),
            saturate(cursorOutline));

    color.rgb =
        lerp(
            color.rgb,
            float3(1.0, 1.0, 1.0),
            saturate(cursorCross));

    return color;
}

float4 main(
    float4 position : SV_POSITION,
    float2 uv : TEXCOORD0) : SV_TARGET
{
    float4 color =
        sourceTexture.Sample(
            sourceSampler,
            uv);

    return DrawInteraction(
        DrawCursor(
            color,
            uv),
        uv);
}
)HLSL";

    static const char* flippedPixelShaderSource = R"HLSL(
Texture2D sourceTexture : register(t0);
SamplerState sourceSampler : register(s0);

cbuffer CursorBuffer : register(b0)
{
    float2 cursorUv;
    float cursorVisible;
    float interactionVisible;
};

float4 DrawInteraction(
    float4 color,
    float2 uv)
{
    if (interactionVisible < 0.5)
    {
        return color;
    }

    float2 p =
        (uv - float2(0.5, 0.5)) *
        float2(1.0, 1.7778);

    float radius =
        length(p);

    float outerRing =
        1.0 -
        smoothstep(
            0.020,
            0.023,
            radius);

    float innerCutout =
        1.0 -
        smoothstep(
            0.012,
            0.015,
            radius);

    float ring =
        saturate(
            outerRing -
            innerCutout);

    float centreDot =
        1.0 -
        smoothstep(
            0.0025,
            0.0045,
            radius);

    float horizontal =
        (1.0 -
         smoothstep(
             0.0015,
             0.0030,
             abs(p.y))) *
        (1.0 -
         smoothstep(
             0.010,
             0.020,
             abs(p.x)));

    float vertical =
        (1.0 -
         smoothstep(
             0.0015,
             0.0030,
             abs(p.x))) *
        (1.0 -
         smoothstep(
             0.010,
             0.020,
             abs(p.y)));

    float reticle =
        saturate(
            ring +
            centreDot +
            horizontal +
            vertical);

    float outline =
        1.0 -
        smoothstep(
            0.024,
            0.028,
            radius);

    color.rgb =
        lerp(
            color.rgb,
            float3(0.0, 0.0, 0.0),
            outline * 0.85);

    color.rgb =
        lerp(
            color.rgb,
            float3(1.0, 1.0, 1.0),
            reticle);

    return color;
}

float4 DrawCursor(
    float4 color,
    float2 uv)
{
    if (cursorVisible < 0.5)
    {
        return color;
    }

    float2 delta =
        abs(uv - cursorUv);

    float cursorCross =
        step(delta.x, 0.0025) *
        step(delta.y, 0.018) +
        step(delta.y, 0.0025) *
        step(delta.x, 0.018);

    float cursorOutline =
        step(delta.x, 0.0045) *
        step(delta.y, 0.021) +
        step(delta.y, 0.0045) *
        step(delta.x, 0.021);

    color.rgb =
        lerp(
            color.rgb,
            float3(0.0, 0.0, 0.0),
            saturate(cursorOutline));

    color.rgb =
        lerp(
            color.rgb,
            float3(1.0, 1.0, 1.0),
            saturate(cursorCross));

    return color;
}

float4 main(
    float4 position : SV_POSITION,
    float2 uv : TEXCOORD0) : SV_TARGET
{
    float2 sampleUv =
        float2(
            uv.x,
            1.0 - uv.y);

    float4 color =
        sourceTexture.Sample(
            sourceSampler,
            sampleUv);

    return DrawInteraction(
        DrawCursor(
            color,
            uv),
        uv);
}
)HLSL";

    ID3DBlob* vertexBlob = nullptr;
    ID3DBlob* pixelBlob = nullptr;
    ID3DBlob* flippedPixelBlob = nullptr;
    ID3DBlob* errors = nullptr;

    HRESULT result =
        D3DCompile(
            vertexShaderSource,
            strlen(vertexShaderSource),
            "MAVRDesktopMirrorVS",
            nullptr,
            nullptr,
            "main",
            "vs_4_0",
            0,
            0,
            &vertexBlob,
            &errors);

    if (errors != nullptr)
    {
        errors->Release();
        errors = nullptr;
    }

    if (FAILED(result))
    {
        return result;
    }

    result =
        D3DCompile(
            pixelShaderSource,
            strlen(pixelShaderSource),
            "MAVRDesktopMirrorPS",
            nullptr,
            nullptr,
            "main",
            "ps_4_0",
            0,
            0,
            &pixelBlob,
            &errors);

    if (errors != nullptr)
    {
        errors->Release();
        errors = nullptr;
    }

    if (FAILED(result))
    {
        vertexBlob->Release();
        return result;
    }

    result =
        D3DCompile(
            flippedPixelShaderSource,
            strlen(flippedPixelShaderSource),
            "MAVRStereoFlipPS",
            nullptr,
            nullptr,
            "main",
            "ps_4_0",
            0,
            0,
            &flippedPixelBlob,
            &errors);

    if (errors != nullptr)
    {
        errors->Release();
        errors = nullptr;
    }

    if (FAILED(result))
    {
        vertexBlob->Release();
        pixelBlob->Release();
        return result;
    }

    result =
        device->CreateVertexShader(
            vertexBlob->GetBufferPointer(),
            vertexBlob->GetBufferSize(),
            nullptr,
            &g_blitVertexShader);

    if (SUCCEEDED(result))
    {
        result =
            device->CreatePixelShader(
                pixelBlob->GetBufferPointer(),
                pixelBlob->GetBufferSize(),
                nullptr,
                &g_blitPixelShader);
    }

    if (SUCCEEDED(result))
    {
        result =
            device->CreatePixelShader(
                flippedPixelBlob->GetBufferPointer(),
                flippedPixelBlob->GetBufferSize(),
                nullptr,
                &g_blitPixelShaderFlipped);
    }

    vertexBlob->Release();
    pixelBlob->Release();
    flippedPixelBlob->Release();

    if (FAILED(result))
    {
        ReleaseBlitResources();
        return result;
    }

    D3D11_SAMPLER_DESC samplerDescription = {};
    samplerDescription.Filter =
        D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    samplerDescription.AddressU =
        D3D11_TEXTURE_ADDRESS_CLAMP;
    samplerDescription.AddressV =
        D3D11_TEXTURE_ADDRESS_CLAMP;
    samplerDescription.AddressW =
        D3D11_TEXTURE_ADDRESS_CLAMP;
    samplerDescription.MaxLOD =
        D3D11_FLOAT32_MAX;

    result =
        device->CreateSamplerState(
            &samplerDescription,
            &g_blitSampler);

    if (FAILED(result))
    {
        ReleaseBlitResources();
        return result;
    }

    D3D11_BUFFER_DESC cursorBufferDescription = {};
    cursorBufferDescription.ByteWidth =
        sizeof(CursorConstants);
    cursorBufferDescription.Usage =
        D3D11_USAGE_DYNAMIC;
    cursorBufferDescription.BindFlags =
        D3D11_BIND_CONSTANT_BUFFER;
    cursorBufferDescription.CPUAccessFlags =
        D3D11_CPU_ACCESS_WRITE;

    result =
        device->CreateBuffer(
            &cursorBufferDescription,
            nullptr,
            &g_cursorConstantBuffer);

    if (FAILED(result))
    {
        ReleaseBlitResources();
        return result;
    }

    g_blitDevice = device;
    g_blitDevice->AddRef();

    return S_OK;
}

static CursorConstants GetCursorConstants()
{
    CursorConstants constants = {};
    constants.CursorX = 0.5f;
    constants.CursorY = 0.5f;
    constants.CursorVisible = 0.0f;
    constants.InteractionVisible =
        g_interactionPromptVisible.load(
            std::memory_order_acquire)
            != 0
                ? 1.0f
                : 0.0f;

    CURSORINFO cursorInfo = {};
    cursorInfo.cbSize = sizeof(CURSORINFO);

    if (!GetCursorInfo(&cursorInfo) ||
        (cursorInfo.flags & CURSOR_SHOWING) == 0 ||
        g_capturedWindow == nullptr)
    {
        return constants;
    }

    POINT point =
        cursorInfo.ptScreenPos;

    if (!ScreenToClient(
            g_capturedWindow,
            &point))
    {
        return constants;
    }

    RECT clientRectangle = {};

    if (!GetClientRect(
            g_capturedWindow,
            &clientRectangle))
    {
        return constants;
    }

    const int width =
        clientRectangle.right -
        clientRectangle.left;

    const int height =
        clientRectangle.bottom -
        clientRectangle.top;

    if (width <= 0 ||
        height <= 0)
    {
        return constants;
    }

    constants.CursorX =
        static_cast<float>(point.x) /
        static_cast<float>(width);

    constants.CursorY =
        static_cast<float>(point.y) /
        static_cast<float>(height);

    constants.CursorVisible =
        constants.CursorX >= 0.0f &&
        constants.CursorX <= 1.0f &&
        constants.CursorY >= 0.0f &&
        constants.CursorY <= 1.0f
            ? 1.0f
            : 0.0f;

    constants.InteractionVisible =
        g_interactionPromptVisible.load(
            std::memory_order_acquire)
            != 0
                ? 1.0f
                : 0.0f;

    return constants;
}

static int BlitTextureToDestination(
    ID3D11Texture2D* sourceTexture,
    void* destinationTexturePointer,
    std::int64_t destinationDxgiFormat,
    bool flipVertically,
    std::int32_t* nativeHResult)
{
    if (nativeHResult != nullptr)
    {
        *nativeHResult = S_OK;
    }

    if (sourceTexture == nullptr ||
        destinationTexturePointer == nullptr)
    {
        return 1;
    }

    sourceTexture->AddRef();

    D3D11_TEXTURE2D_DESC sourceDescription = {};
    sourceTexture->GetDesc(&sourceDescription);

    if (sourceDescription.SampleDesc.Count != 1)
    {
        sourceTexture->Release();
        return 2;
    }

    ID3D11Texture2D* destinationTexture =
        reinterpret_cast<ID3D11Texture2D*>(
            destinationTexturePointer);

    D3D11_TEXTURE2D_DESC destinationDescription = {};
    destinationTexture->GetDesc(&destinationDescription);

    ID3D11Device* device = nullptr;
    destinationTexture->GetDevice(&device);

    if (device == nullptr)
    {
        sourceTexture->Release();
        return 3;
    }

    HRESULT result =
        EnsureBlitResources(
            device);

    if (FAILED(result))
    {
        if (nativeHResult != nullptr)
        {
            *nativeHResult =
                static_cast<std::int32_t>(result);
        }

        device->Release();
        sourceTexture->Release();
        return 4;
    }

    ID3D11DeviceContext* context = nullptr;
    device->GetImmediateContext(&context);

    if (context == nullptr)
    {
        device->Release();
        sourceTexture->Release();
        return 5;
    }

    D3D11_SHADER_RESOURCE_VIEW_DESC sourceViewDescription = {};
    sourceViewDescription.Format =
        NormalizeShaderResourceFormat(
            sourceDescription.Format);
    sourceViewDescription.ViewDimension =
        D3D11_SRV_DIMENSION_TEXTURE2D;
    sourceViewDescription.Texture2D.MostDetailedMip = 0;
    sourceViewDescription.Texture2D.MipLevels = 1;

    ID3D11ShaderResourceView* sourceView = nullptr;

    result =
        device->CreateShaderResourceView(
            sourceTexture,
            &sourceViewDescription,
            &sourceView);

    if (FAILED(result) ||
        sourceView == nullptr)
    {
        if (nativeHResult != nullptr)
        {
            *nativeHResult =
                static_cast<std::int32_t>(result);
        }

        context->Release();
        device->Release();
        sourceTexture->Release();
        return 6;
    }

    D3D11_RENDER_TARGET_VIEW_DESC destinationViewDescription = {};
    destinationViewDescription.Format =
        static_cast<DXGI_FORMAT>(
            destinationDxgiFormat);
    destinationViewDescription.ViewDimension =
        D3D11_RTV_DIMENSION_TEXTURE2D;
    destinationViewDescription.Texture2D.MipSlice = 0;

    ID3D11RenderTargetView* destinationView = nullptr;

    result =
        device->CreateRenderTargetView(
            destinationTexture,
            &destinationViewDescription,
            &destinationView);

    if (FAILED(result) ||
        destinationView == nullptr)
    {
        if (nativeHResult != nullptr)
        {
            *nativeHResult =
                static_cast<std::int32_t>(result);
        }

        sourceView->Release();
        context->Release();
        device->Release();
        sourceTexture->Release();
        return 7;
    }

    ID3D11RenderTargetView* previousRenderTarget = nullptr;
    ID3D11DepthStencilView* previousDepthStencil = nullptr;
    context->OMGetRenderTargets(
        1,
        &previousRenderTarget,
        &previousDepthStencil);

    D3D11_VIEWPORT previousViewport = {};
    UINT previousViewportCount = 1;
    context->RSGetViewports(
        &previousViewportCount,
        &previousViewport);

    D3D11_VIEWPORT viewport = {};
    viewport.Width =
        static_cast<float>(
            destinationDescription.Width);
    viewport.Height =
        static_cast<float>(
            destinationDescription.Height);
    viewport.MinDepth = 0.0f;
    viewport.MaxDepth = 1.0f;

    context->OMSetRenderTargets(
        1,
        &destinationView,
        nullptr);

    context->RSSetViewports(
        1,
        &viewport);

    context->IASetInputLayout(
        nullptr);

    context->IASetPrimitiveTopology(
        D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

    context->VSSetShader(
        g_blitVertexShader,
        nullptr,
        0);

    context->PSSetShader(
        flipVertically
            ? g_blitPixelShaderFlipped
            : g_blitPixelShader,
        nullptr,
        0);

    context->PSSetShaderResources(
        0,
        1,
        &sourceView);

    context->PSSetSamplers(
        0,
        1,
        &g_blitSampler);

    const CursorConstants cursorConstants =
        GetCursorConstants();

    D3D11_MAPPED_SUBRESOURCE mappedCursorBuffer = {};

    if (SUCCEEDED(
            context->Map(
                g_cursorConstantBuffer,
                0,
                D3D11_MAP_WRITE_DISCARD,
                0,
                &mappedCursorBuffer)))
    {
        memcpy(
            mappedCursorBuffer.pData,
            &cursorConstants,
            sizeof(CursorConstants));

        context->Unmap(
            g_cursorConstantBuffer,
            0);
    }

    context->PSSetConstantBuffers(
        0,
        1,
        &g_cursorConstantBuffer);

    context->Draw(
        3,
        0);

    ID3D11ShaderResourceView* nullView = nullptr;
    context->PSSetShaderResources(
        0,
        1,
        &nullView);

    context->OMSetRenderTargets(
        1,
        &previousRenderTarget,
        previousDepthStencil);

    if (previousViewportCount > 0)
    {
        context->RSSetViewports(
            1,
            &previousViewport);
    }

    context->Flush();

    if (previousDepthStencil != nullptr)
    {
        previousDepthStencil->Release();
    }

    if (previousRenderTarget != nullptr)
    {
        previousRenderTarget->Release();
    }

    destinationView->Release();
    sourceView->Release();
    context->Release();
    device->Release();
    sourceTexture->Release();

    return 0;
}

extern "C" __declspec(dllexport) int __cdecl
MAVR_SetInteractionPromptVisible(
    int visible)
{
    g_interactionPromptVisible.store(
        visible != 0
            ? 1
            : 0,
        std::memory_order_release);

    return 0;
}

extern "C" __declspec(dllexport) int __cdecl
MAVR_SetStereoSourceTextures(
    void* leftTexture,
    void* rightTexture)
{
    ID3D11Texture2D* newLeft =
        reinterpret_cast<ID3D11Texture2D*>(
            leftTexture);

    ID3D11Texture2D* newRight =
        reinterpret_cast<ID3D11Texture2D*>(
            rightTexture);

    if (newLeft != nullptr)
    {
        newLeft->AddRef();
    }

    if (newRight != nullptr)
    {
        newRight->AddRef();
    }

    ID3D11Texture2D* oldLeft = nullptr;
    ID3D11Texture2D* oldRight = nullptr;

    {
        std::lock_guard<std::mutex> guard(
            g_stereoSourceMutex);

        oldLeft =
            g_leftEyeSourceTexture;

        oldRight =
            g_rightEyeSourceTexture;

        g_leftEyeSourceTexture =
            newLeft;

        g_rightEyeSourceTexture =
            newRight;
    }

    if (oldLeft != nullptr)
    {
        oldLeft->Release();
    }

    if (oldRight != nullptr)
    {
        oldRight->Release();
    }

    return 0;
}

extern "C" __declspec(dllexport) int __cdecl
MAVR_BlitStereoSourceTexture(
    int eyeIndex,
    void* destinationTexturePointer,
    std::int64_t destinationDxgiFormat,
    std::int32_t* nativeHResult)
{
    ID3D11Texture2D* sourceTexture = nullptr;

    {
        std::lock_guard<std::mutex> guard(
            g_stereoSourceMutex);

        sourceTexture =
            eyeIndex == 0
                ? g_leftEyeSourceTexture
                : g_rightEyeSourceTexture;

        if (sourceTexture != nullptr)
        {
            sourceTexture->AddRef();
        }
    }

    if (sourceTexture == nullptr)
    {
        return 20;
    }

    const int result =
        BlitTextureToDestination(
            sourceTexture,
            destinationTexturePointer,
            destinationDxgiFormat,
            true,
            nativeHResult);

    sourceTexture->Release();

    return result;
}

extern "C" __declspec(dllexport) int __cdecl
MAVR_BlitCapturedBackBuffer(
    void* destinationTexturePointer,
    std::int64_t destinationDxgiFormat,
    std::int32_t* nativeHResult)
{
    IDXGISwapChain* swapChain = nullptr;

    {
        std::lock_guard<std::mutex> guard(g_mutex);

        if (g_capturedSwapChain != nullptr)
        {
            swapChain = g_capturedSwapChain;
            swapChain->AddRef();
        }
    }

    if (swapChain == nullptr)
    {
        return 10;
    }

    ID3D11Texture2D* sourceTexture = nullptr;

    HRESULT result =
        swapChain->GetBuffer(
            0,
            __uuidof(ID3D11Texture2D),
            reinterpret_cast<void**>(&sourceTexture));

    swapChain->Release();

    if (FAILED(result) ||
        sourceTexture == nullptr)
    {
        if (nativeHResult != nullptr)
        {
            *nativeHResult =
                static_cast<std::int32_t>(result);
        }

        return 11;
    }

    const int resultCode =
        BlitTextureToDestination(
            sourceTexture,
            destinationTexturePointer,
            destinationDxgiFormat,
            false,
            nativeHResult);

    sourceTexture->Release();

    return resultCode;
}

extern "C" __declspec(dllexport)
int __cdecl MAVR_SetPresentFrameCallback(
    void* callback)
{
    g_presentFrameCallback.store(
        reinterpret_cast<PresentFrameCallback>(callback),
        std::memory_order_release);

    return 0;
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

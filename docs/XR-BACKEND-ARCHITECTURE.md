# XR backend architecture

The XR integration is split into four responsibilities:

## `IXrBackend`

Defines the minimal lifecycle contract for any XR implementation.

## `OpenXrNativeBackend`

Owns the native OpenXR loader handle and resolves `xrGetInstanceProcAddr`.

In v0.7 it stops before `xrCreateInstance`.

## `XrBackendManager`

Owns the active backend instance and centralizes initialization, state reporting, and shutdown.

## Future milestones

- v0.8: controlled Khronos loader distribution and `xrCreateInstance`
- v0.9: system discovery and graphics requirements
- v0.10: D3D11 session and swapchains
- v0.11: frame loop and HMD pose
- v0.12: apply HMD pose to `MortuaryAssistantVR_Head`

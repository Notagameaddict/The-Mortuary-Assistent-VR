namespace MortuaryAssistantVR.XR;

internal enum XrBackendState
{
    NotInitialized = 0,
    LoaderUnavailable = 1,
    LoaderLoaded = 2,
    EntryPointResolved = 3,
    StartupDisabled = 4,
    InstanceCreated = 5,
    GraphicsRequirementsReady = 6,
    WaitingForUnityGraphicsDevice = 7,
    UnityGraphicsDeviceReady = 8,
    SessionCreated = 9,
    ReferenceSpaceCreated = 10,
    SwapchainsCreated = 11,
    SwapchainImagesReady = 12,
    WaitingForSessionReady = 13,
    SessionRunning = 14,
    CinemaQuadRendering = 15,
    StereoPrototypeRendering = 16,
    InstanceCreationFailed = 17,
    SessionCreationFailed = 19,
    Failed = 19,
    Disposed = 20
}

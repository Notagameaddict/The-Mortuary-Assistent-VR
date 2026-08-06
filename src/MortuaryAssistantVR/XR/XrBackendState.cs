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
    TestPatternRendering = 15,
    InstanceCreationFailed = 16,
    SessionCreationFailed = 17,
    Failed = 18,
    Disposed = 19
}

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
    UnityGraphicsDeviceReady = 7,
    InstanceCreationFailed = 8,
    Failed = 9,
    Disposed = 10
}

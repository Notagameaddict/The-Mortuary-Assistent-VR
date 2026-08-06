namespace MortuaryAssistantVR.XR;

internal enum XrBackendState
{
    NotInitialized = 0,
    LoaderUnavailable = 1,
    LoaderLoaded = 2,
    EntryPointResolved = 3,
    StartupDisabled = 4,
    InstanceCreated = 5,
    InstanceCreationFailed = 6,
    Failed = 7,
    Disposed = 8
}

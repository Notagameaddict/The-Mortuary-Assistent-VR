namespace MortuaryAssistantVR.XR;

internal interface IXrBackend : IDisposable
{
    string Name { get; }
    XrBackendState State { get; }
    string StatusMessage { get; }

    bool Initialize(bool attemptStartup);
}

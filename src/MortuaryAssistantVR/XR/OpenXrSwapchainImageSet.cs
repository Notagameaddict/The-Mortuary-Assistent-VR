namespace MortuaryAssistantVR.XR;

internal sealed class OpenXrSwapchainImageSet
{
    internal OpenXrSwapchainImageSet(
        int viewIndex,
        ulong swapchain,
        uint width,
        uint height,
        long format,
        IReadOnlyList<IntPtr> textures)
    {
        ViewIndex = viewIndex;
        Swapchain = swapchain;
        Width = width;
        Height = height;
        Format = format;
        Textures = textures;
    }

    internal int ViewIndex { get; }
    internal ulong Swapchain { get; }
    internal uint Width { get; }
    internal uint Height { get; }
    internal long Format { get; }
    internal IReadOnlyList<IntPtr> Textures { get; }
}

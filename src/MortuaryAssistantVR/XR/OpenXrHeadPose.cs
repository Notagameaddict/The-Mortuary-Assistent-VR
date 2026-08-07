namespace MortuaryAssistantVR.XR;

internal readonly record struct OpenXrHeadPose(
    float PositionX,
    float PositionY,
    float PositionZ,
    float OrientationX,
    float OrientationY,
    float OrientationZ,
    float OrientationW,
    long Sequence);

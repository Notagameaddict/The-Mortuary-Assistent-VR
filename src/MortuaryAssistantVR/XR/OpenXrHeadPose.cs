namespace MortuaryAssistantVR.XR;

internal readonly record struct OpenXrHeadPose(
    float PositionX,
    float PositionY,
    float PositionZ,
    float OrientationX,
    float OrientationY,
    float OrientationZ,
    float OrientationW,
    float LeftAngleLeft,
    float LeftAngleRight,
    float LeftAngleUp,
    float LeftAngleDown,
    float RightAngleLeft,
    float RightAngleRight,
    float RightAngleUp,
    float RightAngleDown,
    long Sequence);

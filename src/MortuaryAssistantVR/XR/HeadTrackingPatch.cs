using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace MortuaryAssistantVR.XR;

internal static class HeadTrackingPatch
{
    private const string HarmonyId =
        "com.notagameaddict.mortuaryassistantvr.headtracking";

    private static ManualLogSource? _logger;
    private static Transform? _headTransform;
    private static Vector3 _headBaseLocalPosition;
    private static Vector3 _openXrBasePosition;
    private static bool _positionBaselineCaptured;
    private static long _lastAppliedSequence;
    private static int _appliedCount;
    private static bool _installed;

    internal static void Install(
        ManualLogSource logger)
    {
        if (_installed)
        {
            return;
        }

        _logger =
            logger;

        var playerType =
            FindLoadedType(
                "MFPP.Player");

        if (playerType is null)
        {
            logger.LogWarning(
                "[HeadTracking] Type 'MFPP.Player' was not found.");

            return;
        }

        var updateMethod =
            playerType.GetMethod(
                "Update",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);

        if (updateMethod is null)
        {
            logger.LogWarning(
                "[HeadTracking] MFPP.Player.Update was not found.");

            return;
        }

        var postfixMethod =
            typeof(HeadTrackingPatch).GetMethod(
                nameof(Postfix),
                BindingFlags.Static |
                BindingFlags.NonPublic);

        if (postfixMethod is null)
        {
            logger.LogError(
                "[HeadTracking] Postfix method was not found.");

            return;
        }

        var harmonyType =
            FindLoadedType(
                "HarmonyLib.Harmony");

        var harmonyMethodType =
            FindLoadedType(
                "HarmonyLib.HarmonyMethod");

        if (harmonyType is null ||
            harmonyMethodType is null)
        {
            logger.LogError(
                "[HeadTracking] Harmony runtime types were not found. " +
                "Ensure 0Harmony.dll is present in BepInEx.");

            return;
        }

        try
        {
            var harmony =
                Activator.CreateInstance(
                    harmonyType,
                    HarmonyId);

            var harmonyPostfix =
                Activator.CreateInstance(
                    harmonyMethodType,
                    postfixMethod);

            var patchMethod =
                harmonyType.GetMethods(
                    BindingFlags.Instance |
                    BindingFlags.Public)
                .FirstOrDefault(
                    method =>
                    {
                        if (method.Name != "Patch")
                        {
                            return false;
                        }

                        var parameters =
                            method.GetParameters();

                        return parameters.Length >= 3 &&
                               typeof(MethodBase).IsAssignableFrom(
                                   parameters[0].ParameterType);
                    });

            if (harmony is null ||
                harmonyPostfix is null ||
                patchMethod is null)
            {
                logger.LogError(
                    "[HeadTracking] Harmony patch API could not be resolved.");

                return;
            }

            var parameters =
                patchMethod.GetParameters();

            var arguments =
                new object?[parameters.Length];

            arguments[0] =
                updateMethod;

            // Harmony.Patch(original, prefix, postfix, transpiler, finalizer, ...)
            if (parameters.Length > 1)
            {
                arguments[1] = null;
            }

            if (parameters.Length > 2)
            {
                arguments[2] = harmonyPostfix;
            }

            for (var index = 3;
                 index < arguments.Length;
                 index++)
            {
                arguments[index] = null;
            }

            patchMethod.Invoke(
                harmony,
                arguments);

            _installed =
                true;

            logger.LogInfo(
                "[HeadTracking] Patched MFPP.Player.Update " +
                "through the runtime Harmony assembly.");
        }
        catch (Exception exception)
        {
            logger.LogError(
                $"[HeadTracking] Harmony patch failed: {exception}");
        }
    }

    private static Type? FindLoadedType(
        string fullName)
    {
        foreach (var assembly in
                 AppDomain.CurrentDomain.GetAssemblies())
        {
            var type =
                assembly.GetType(
                    fullName,
                    throwOnError: false,
                    ignoreCase: false);

            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }

    private static void Postfix()
    {
        StereoCameraRig.UpdatePresentationMode();

        InteractionPromptDetector.Update(
            _logger);

        if (!XrBackendManager.TryGetLatestHeadPose(
                out var pose))
        {
            return;
        }

        if (pose.Sequence ==
            _lastAppliedSequence)
        {
            return;
        }

        _lastAppliedSequence =
            pose.Sequence;

        if (_headTransform is null)
        {
            var headObject =
                GameObject.Find(
                    "MortuaryAssistantVR_Head");

            if (headObject is null)
            {
                return;
            }

            _headTransform =
                headObject.transform;

            _headBaseLocalPosition =
                _headTransform.localPosition;

            _positionBaselineCaptured =
                false;

            _logger?.LogInfo(
                "[HeadTracking] XR head transform found.");
        }

        var unityRotation =
            new Quaternion(
                -pose.OrientationX,
                -pose.OrientationY,
                pose.OrientationZ,
                pose.OrientationW);

        var openXrPosition =
            new Vector3(
                pose.PositionX,
                pose.PositionY,
                -pose.PositionZ);

        if (!_positionBaselineCaptured)
        {
            _openXrBasePosition =
                openXrPosition;

            _headBaseLocalPosition =
                _headTransform.localPosition;

            _positionBaselineCaptured =
                true;

            _logger?.LogInfo(
                $"[HeadTracking] Position baseline captured: " +
                $"openXr=({_openXrBasePosition.x:F3}, " +
                $"{_openXrBasePosition.y:F3}, " +
                $"{_openXrBasePosition.z:F3}), " +
                $"unityBase=({_headBaseLocalPosition.x:F3}, " +
                $"{_headBaseLocalPosition.y:F3}, " +
                $"{_headBaseLocalPosition.z:F3}).");
        }

        var localPositionDelta =
            openXrPosition -
            _openXrBasePosition;

        _headTransform.localRotation =
            unityRotation;

        _headTransform.localPosition =
            _headBaseLocalPosition +
            localPositionDelta;

        StereoCameraRig.ApplyOpenXrProjection(
            pose);

        _appliedCount++;

        if (_appliedCount <= 5 ||
            _appliedCount % 600 == 0)
        {
            var euler =
                unityRotation.eulerAngles;

            var localPosition =
                _headTransform.localPosition;

            _logger?.LogInfo(
                $"[HeadTracking] Applied pose {_appliedCount}: " +
                $"sequence={pose.Sequence}, " +
                $"euler=({euler.x:F1}, {euler.y:F1}, {euler.z:F1}), " +
                $"localPosition=({localPosition.x:F3}, " +
                $"{localPosition.y:F3}, {localPosition.z:F3}).");
        }
    }
}

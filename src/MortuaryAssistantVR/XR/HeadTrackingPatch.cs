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
    private static bool _lateUpdateInstalled;

    internal static void ResetSceneState()
    {
        _headTransform =
            null;

        _headBaseLocalPosition =
            Vector3.zero;

        _openXrBasePosition =
            Vector3.zero;

        _positionBaselineCaptured =
            false;

        _lastAppliedSequence =
            0;

        _appliedCount =
            0;

        _logger?.LogInfo(
            "[HeadTracking] Scene tracking state reset.");
    }

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

            if (harmony is null)
            {
                logger.LogError(
                    "[HeadTracking] Harmony instance could not be created.");

                return;
            }

            var patchMethod =
                ResolveHarmonyPatchMethod(
                    harmonyType);

            if (patchMethod is null)
            {
                logger.LogError(
                    "[HeadTracking] Harmony patch API could not be resolved.");

                return;
            }

            var updateMethod =
                playerType.GetMethod(
                    "Update",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            if (updateMethod is not null)
            {
                PatchPostfix(
                    harmony,
                    harmonyMethodType,
                    patchMethod,
                    updateMethod,
                    nameof(UpdatePostfix));

                logger.LogInfo(
                    "[HeadTracking] Patched MFPP.Player.Update for " +
                    "interaction/presentation maintenance.");
            }

            var lateUpdateMethod =
                playerType.GetMethod(
                    "LateUpdate",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            if (lateUpdateMethod is not null)
            {
                PatchPostfix(
                    harmony,
                    harmonyMethodType,
                    patchMethod,
                    lateUpdateMethod,
                    nameof(LateUpdatePostfix));

                _lateUpdateInstalled =
                    true;

                logger.LogInfo(
                    "[HeadTracking] Patched MFPP.Player.LateUpdate for " +
                    "late head-pose application.");
            }
            else if (updateMethod is not null)
            {
                logger.LogWarning(
                    "[HeadTracking] MFPP.Player.LateUpdate was not found; " +
                    "falling back to Update timing.");
            }
            else
            {
                logger.LogError(
                    "[HeadTracking] Neither MFPP.Player.Update nor " +
                    "LateUpdate was found.");

                return;
            }

            _installed =
                true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                $"[HeadTracking] Harmony patch failed: {exception}");
        }
    }

    private static MethodInfo? ResolveHarmonyPatchMethod(
        Type harmonyType)
    {
        return harmonyType.GetMethods(
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
    }

    private static void PatchPostfix(
        object harmony,
        Type harmonyMethodType,
        MethodInfo patchMethod,
        MethodInfo targetMethod,
        string postfixName)
    {
        var postfixMethod =
            typeof(HeadTrackingPatch).GetMethod(
                postfixName,
                BindingFlags.Static |
                BindingFlags.NonPublic);

        if (postfixMethod is null)
        {
            throw new MissingMethodException(
                nameof(HeadTrackingPatch),
                postfixName);
        }

        var harmonyPostfix =
            Activator.CreateInstance(
                harmonyMethodType,
                postfixMethod);

        if (harmonyPostfix is null)
        {
            throw new InvalidOperationException(
                $"Could not create HarmonyMethod for {postfixName}.");
        }

        var parameters =
            patchMethod.GetParameters();

        var arguments =
            new object?[parameters.Length];

        arguments[0] =
            targetMethod;

        if (parameters.Length > 1)
        {
            arguments[1] =
                null;
        }

        if (parameters.Length > 2)
        {
            arguments[2] =
                harmonyPostfix;
        }

        for (var index = 3;
             index < arguments.Length;
             index++)
        {
            arguments[index] =
                null;
        }

        patchMethod.Invoke(
            harmony,
            arguments);
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

    private static void UpdatePostfix()
    {
        InteractionPromptDetector.Update(
            _logger);

        ToolMenuUiProbe.Update(
            _logger);

        StereoCameraRig.UpdatePresentationMode();

        // Fallback for game builds that do not expose LateUpdate.
        if (!_lateUpdateInstalled)
        {
            ApplyLatestHeadPose();
        }
    }

    private static void LateUpdatePostfix()
    {
        ApplyLatestHeadPose();
    }

    private static void ApplyLatestHeadPose()
    {
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

        if (_headTransform == null)
        {
            var headObject =
                GameObject.Find(
                    "MortuaryAssistantVR_Head");

            if (headObject == null)
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

        // The eye RenderTextures rendered after this LateUpdate correspond
        // to this exact OpenXR pose. Tag it so the compositor projection
        // layer uses the matching pose rather than the newer pose located
        // during Present.
        XrBackendManager.MarkRenderedHeadPose(
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
                $"[HeadTracking] Late pose {_appliedCount}: " +
                $"sequence={pose.Sequence}, " +
                $"euler=({euler.x:F1}, {euler.y:F1}, {euler.z:F1}), " +
                $"localPosition=({localPosition.x:F3}, " +
                $"{localPosition.y:F3}, {localPosition.z:F3}).");
        }
    }
}

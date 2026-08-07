using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using UnityEngine;

namespace MortuaryAssistantVR.XR;

internal static class InteractionPromptDetector
{
    private const float InteractionDistance = 3.0f;
    private const int VirtualKeyLeftMouseButton = 0x01;

    private static readonly string[] FallbackMethodNames =
    {
        "Pickup",
        "PickUp",
        "Take",
        "Use",
        "Interact"
    };

    private static ManualLogSource? _logger;
    private static Component? _playerInteraction;
    private static Camera? _gameplayCamera;

    private static Type? _physicsType;
    private static Type? _raycastHitType;
    private static MethodInfo? _raycastMethod;
    private static PropertyInfo? _hitColliderProperty;
    private static PropertyInfo? _hitDistanceProperty;
    private static PropertyInfo? _colliderGameObjectProperty;

    private static bool _physicsReflectionInitialized;
    private static bool _lastVisible;
    private static bool _computerTargetActive;
    private static bool _leftMouseWasDown;
    private static int _logCounter;

    internal static bool ComputerTargetActive =>
        _computerTargetActive;

    [DllImport(
        "user32.dll",
        EntryPoint = "GetAsyncKeyState")]
    private static extern short GetAsyncKeyState(
        int virtualKey);

    internal static void Update(
        ManualLogSource? logger)
    {
        if (logger is not null)
        {
            _logger =
                logger;
        }

        if (ToolMenuUiProbe.CircleRetVisible)
        {
            _computerTargetActive =
                false;

            _leftMouseWasDown =
                (GetAsyncKeyState(
                     VirtualKeyLeftMouseButton) &
                 0x8000) != 0;

            SetVisible(
                false);

            return;
        }

        ResolvePlayerInteraction();
        ResolveGameplayCamera();
        InitializePhysicsReflection();

        var hitSomething =
            TryFindInteractionTarget(
                out var target,
                out var distance);

        _computerTargetActive =
            hitSomething &&
            target != null &&
            IsComputerTarget(
                target);

        SetVisible(
            hitSomething);

        var leftMouseDown =
            (GetAsyncKeyState(
                 VirtualKeyLeftMouseButton) &
             0x8000) != 0;

        var leftMousePressed =
            leftMouseDown &&
            !_leftMouseWasDown;

        _leftMouseWasDown =
            leftMouseDown;

        if (hitSomething &&
            target != null)
        {
            _logCounter++;

            if (_logCounter <= 5 ||
                _logCounter % 600 == 0)
            {
                _logger?.LogInfo(
                    $"[InteractionPrompt] Ray target: " +
                    $"name='{target.name}', " +
                    $"distance={distance:F2}, " +
                    $"layer={target.layer}.");
            }

            if (leftMousePressed &&
                !StereoCameraRig.IsUiFallbackActive)
            {
                TryInvokePickupFallback(
                    target);
            }
        }
    }

    internal static void Reset()
    {
        _playerInteraction =
            null;

        _gameplayCamera =
            null;

        _physicsReflectionInitialized =
            false;

        _physicsType =
            null;

        _raycastHitType =
            null;

        _raycastMethod =
            null;

        _hitColliderProperty =
            null;

        _hitDistanceProperty =
            null;

        _colliderGameObjectProperty =
            null;

        _computerTargetActive =
            false;

        _leftMouseWasDown =
            false;

        _logCounter =
            0;

        SetVisible(
            false);
    }

    private static void ResolvePlayerInteraction()
    {
        if (_playerInteraction != null)
        {
            return;
        }

        var player =
            GameObject.Find(
                "Player");

        if (player == null)
        {
            return;
        }

        foreach (var component in
                 player.GetComponents<Component>())
        {
            if (component == null)
            {
                continue;
            }

            var type =
                component.GetType();

            if (type.Name == "PlayerInteraction" ||
                type.FullName?.EndsWith(
                    ".PlayerInteraction",
                    StringComparison.Ordinal) == true)
            {
                _playerInteraction =
                    component;

                _logger?.LogInfo(
                    $"[InteractionPrompt] PlayerInteraction resolved: " +
                    $"type='{type.FullName}'.");

                return;
            }
        }
    }

    private static void ResolveGameplayCamera()
    {
        if (_gameplayCamera != null)
        {
            return;
        }

        var head =
            GameObject.Find(
                "MortuaryAssistantVR_Head");

        if (head != null)
        {
            foreach (var camera in
                     head.GetComponentsInChildren<Camera>(
                         true))
            {
                if (camera == null)
                {
                    continue;
                }

                if (camera.gameObject.name ==
                        "MortuaryAssistantVR_LeftEyeCamera" ||
                    camera.gameObject.name ==
                        "MortuaryAssistantVR_RightEyeCamera")
                {
                    continue;
                }

                _gameplayCamera =
                    camera;

                break;
            }
        }

        _gameplayCamera ??=
            Camera.main;

        if (_gameplayCamera != null)
        {
            _logger?.LogInfo(
                $"[InteractionPrompt] Ray camera resolved: " +
                $"'{_gameplayCamera.gameObject.name}'.");
        }
    }

    private static void InitializePhysicsReflection()
    {
        if (_physicsReflectionInitialized)
        {
            return;
        }

        _physicsReflectionInitialized =
            true;

        _physicsType =
            FindLoadedType(
                "UnityEngine.Physics");

        _raycastHitType =
            FindLoadedType(
                "UnityEngine.RaycastHit");

        var colliderType =
            FindLoadedType(
                "UnityEngine.Collider");

        if (_physicsType is null ||
            _raycastHitType is null ||
            colliderType is null)
        {
            _logger?.LogError(
                "[InteractionPrompt] Unity physics runtime types " +
                "could not be resolved.");

            return;
        }

        foreach (var method in
                 _physicsType.GetMethods(
                     BindingFlags.Static |
                     BindingFlags.Public))
        {
            if (method.Name != "Raycast")
            {
                continue;
            }

            var parameters =
                method.GetParameters();

            if (parameters.Length != 3)
            {
                continue;
            }

            if (parameters[0].ParameterType !=
                    typeof(Ray) ||
                !parameters[1].ParameterType.IsByRef ||
                parameters[1].ParameterType.GetElementType() !=
                    _raycastHitType ||
                parameters[2].ParameterType !=
                    typeof(float))
            {
                continue;
            }

            _raycastMethod =
                method;

            break;
        }

        _hitColliderProperty =
            _raycastHitType.GetProperty(
                "collider",
                BindingFlags.Instance |
                BindingFlags.Public);

        _hitDistanceProperty =
            _raycastHitType.GetProperty(
                "distance",
                BindingFlags.Instance |
                BindingFlags.Public);

        _colliderGameObjectProperty =
            colliderType.GetProperty(
                "gameObject",
                BindingFlags.Instance |
                BindingFlags.Public);

        if (_raycastMethod is null ||
            _hitColliderProperty is null ||
            _hitDistanceProperty is null ||
            _colliderGameObjectProperty is null)
        {
            _logger?.LogError(
                "[InteractionPrompt] Physics reflection API " +
                "could not be resolved.");

            return;
        }

        _logger?.LogInfo(
            "[InteractionPrompt] Physics raycast bridge resolved " +
            "through reflection.");
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

    private static bool TryFindInteractionTarget(
        out GameObject? target,
        out float distance)
    {
        target =
            null;

        distance =
            0;

        if (_gameplayCamera == null ||
            _raycastHitType is null ||
            _raycastMethod is null ||
            _hitColliderProperty is null ||
            _hitDistanceProperty is null ||
            _colliderGameObjectProperty is null)
        {
            return false;
        }

        var ray =
            new Ray(
                _gameplayCamera.transform.position,
                _gameplayCamera.transform.forward);

        var boxedHit =
            Activator.CreateInstance(
                _raycastHitType);

        var arguments =
            new object?[]
            {
                ray,
                boxedHit,
                InteractionDistance
            };

        bool didHit;

        try
        {
            didHit =
                _raycastMethod.Invoke(
                    null,
                    arguments) is true;
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(
                $"[InteractionPrompt] Reflected raycast failed: " +
                $"{exception.Message}");

            return false;
        }

        if (!didHit)
        {
            return false;
        }

        boxedHit =
            arguments[1];

        if (boxedHit is null)
        {
            return false;
        }

        var collider =
            _hitColliderProperty.GetValue(
                boxedHit);

        if (collider is null)
        {
            return false;
        }

        target =
            _colliderGameObjectProperty.GetValue(
                collider) as GameObject;

        if (target == null)
        {
            return false;
        }

        var distanceValue =
            _hitDistanceProperty.GetValue(
                boxedHit);

        if (distanceValue is float floatDistance)
        {
            distance =
                floatDistance;
        }

        if (target.transform.IsChildOf(
                _gameplayCamera.transform.root))
        {
            target =
                null;

            return false;
        }

        return true;
    }

    private static bool IsComputerTarget(
        GameObject target)
    {
        var transform =
            target.transform;

        for (var depth = 0;
             transform != null &&
             depth < 5;
             depth++)
        {
            var lower =
                transform.gameObject.name.ToLowerInvariant();

            if (lower.Contains(
                    "compwall") ||
                lower.Contains(
                    "computer") ||
                lower.Contains(
                    "pcscreen") ||
                lower.Contains(
                    "monitor"))
            {
                return true;
            }

            transform =
                transform.parent;
        }

        return false;
    }

    private static void TryInvokePickupFallback(
        GameObject target)
    {
        // Let the game's normal code own doors and the mortuary computer.
        // The fallback is only meant to rescue missed pickup/use clicks.
        if (IsComputerTarget(
                target))
        {
            return;
        }

        var lowerName =
            target.name.ToLowerInvariant();

        if (lowerName.Contains(
                "door"))
        {
            return;
        }

        foreach (var component in
                 target.GetComponentsInParent<Component>(
                     true))
        {
            if (component == null)
            {
                continue;
            }

            var type =
                component.GetType();

            foreach (var methodName in
                     FallbackMethodNames)
            {
                var method =
                    type.GetMethod(
                        methodName,
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic,
                        binder: null,
                        types: Type.EmptyTypes,
                        modifiers: null);

                if (method is null)
                {
                    continue;
                }

                try
                {
                    method.Invoke(
                        component,
                        null);

                    _logger?.LogInfo(
                        $"[InteractionPrompt] Click fallback invoked " +
                        $"'{type.FullName}.{method.Name}()' on " +
                        $"'{target.name}'.");

                    return;
                }
                catch (Exception exception)
                {
                    _logger?.LogWarning(
                        $"[InteractionPrompt] Click fallback failed for " +
                        $"'{type.FullName}.{method.Name}()': " +
                        $"{exception.Message}");
                }
            }
        }
    }

    private static void SetVisible(
        bool visible)
    {
        var leftU =
            0.5f;

        var leftV =
            0.5f;

        var rightU =
            0.5f;

        var rightV =
            0.5f;

        if (XrBackendManager.TryGetLatestHeadPose(
                out var pose))
        {
            CalculateForwardRayUv(
                pose.LeftAngleLeft,
                pose.LeftAngleRight,
                pose.LeftAngleDown,
                pose.LeftAngleUp,
                out leftU,
                out leftV);

            CalculateForwardRayUv(
                pose.RightAngleLeft,
                pose.RightAngleRight,
                pose.RightAngleDown,
                pose.RightAngleUp,
                out rightU,
                out rightV);
        }

        D3D11PresentHookProbe.SetInteractionPromptState(
            _logger,
            visible,
            leftU,
            leftV,
            rightU,
            rightV);

        if (_lastVisible ==
            visible)
        {
            return;
        }

        _lastVisible =
            visible;

        _logger?.LogInfo(
            $"[InteractionPrompt] VR reticle " +
            $"{(visible ? "shown" : "hidden")}: " +
            $"leftUv=({leftU:F3}, {leftV:F3}), " +
            $"rightUv=({rightU:F3}, {rightV:F3}).");
    }

    private static void CalculateForwardRayUv(
        float angleLeft,
        float angleRight,
        float angleDown,
        float angleUp,
        out float u,
        out float v)
    {
        var tangentLeft =
            MathF.Tan(
                angleLeft);

        var tangentRight =
            MathF.Tan(
                angleRight);

        var tangentDown =
            MathF.Tan(
                angleDown);

        var tangentUp =
            MathF.Tan(
                angleUp);

        var horizontalRange =
            tangentRight -
            tangentLeft;

        var verticalRange =
            tangentUp -
            tangentDown;

        u =
            MathF.Abs(horizontalRange) > 0.00001f
                ? -tangentLeft /
                  horizontalRange
                : 0.5f;

        // Shader UV has Y=0 at the top of the submitted eye image.
        v =
            MathF.Abs(verticalRange) > 0.00001f
                ? tangentUp /
                  verticalRange
                : 0.5f;

        u =
            Math.Clamp(
                u,
                0.05f,
                0.95f);

        v =
            Math.Clamp(
                v,
                0.05f,
                0.95f);
    }

}

using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MortuaryAssistantVR.Diagnostics;

internal static class SceneExplorer
{
    [HideFromIl2Cpp]
    internal static void LogScene(
        ManualLogSource logger,
        Scene scene,
        int maxDepth,
        int maxObjects,
        bool logComponents)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            logger.LogWarning(
                $"Scene Explorer skipped invalid or unloaded scene '{scene.name}'.");
            return;
        }

        var safeDepth = Math.Max(0, maxDepth);
        var safeObjectLimit = Math.Max(1, maxObjects);
        var count = 0;

        logger.LogInfo(
            $"=== Scene Explorer begin: '{scene.name}' " +
            $"(maxDepth={safeDepth}, maxObjects={safeObjectLimit}) ===");

        try
        {
            var roots = scene.GetRootGameObjects();

            for (var index = 0; index < roots.Length; index++)
            {
                if (count >= safeObjectLimit)
                {
                    break;
                }

                var root = roots[index];
                if (root is null)
                {
                    continue;
                }

                LogTransform(
                    logger,
                    root.transform,
                    depth: 0,
                    safeDepth,
                    safeObjectLimit,
                    logComponents,
                    ref count);
            }

            if (count >= safeObjectLimit)
            {
                logger.LogWarning(
                    $"Scene Explorer stopped after {count} objects because MaxObjects was reached.");
            }

            logger.LogInfo(
                $"=== Scene Explorer end: '{scene.name}', objectsLogged={count} ===");
        }
        catch (Exception exception)
        {
            logger.LogError(
                $"Scene Explorer failed for '{scene.name}': {exception}");
        }
    }

    [HideFromIl2Cpp]
    private static void LogTransform(
        ManualLogSource logger,
        Transform transform,
        int depth,
        int maxDepth,
        int maxObjects,
        bool logComponents,
        ref int count)
    {
        if (transform is null || count >= maxObjects)
        {
            return;
        }

        var gameObject = transform.gameObject;
        var indent = new string(' ', depth * 2);
        var activeText =
            $"activeSelf={gameObject.activeSelf}, activeInHierarchy={gameObject.activeInHierarchy}";

        logger.LogInfo(
            $"[SceneExplorer] {indent}{gameObject.name} " +
            $"({activeText}, layer={gameObject.layer}, tag='{SafeTag(gameObject)}')");

        count++;

        if (logComponents)
        {
            LogComponents(logger, gameObject, indent + "  ");
        }

        if (depth >= maxDepth)
        {
            if (transform.childCount > 0)
            {
                logger.LogInfo(
                    $"[SceneExplorer] {indent}  ... {transform.childCount} child object(s) hidden by MaxDepth");
            }

            return;
        }

        for (var childIndex = 0; childIndex < transform.childCount; childIndex++)
        {
            if (count >= maxObjects)
            {
                return;
            }

            var child = transform.GetChild(childIndex);
            LogTransform(
                logger,
                child,
                depth + 1,
                maxDepth,
                maxObjects,
                logComponents,
                ref count);
        }
    }

    [HideFromIl2Cpp]
    private static void LogComponents(
        ManualLogSource logger,
        GameObject gameObject,
        string indent)
    {
        try
        {
            var components = gameObject.GetComponents<Component>();

            for (var index = 0; index < components.Length; index++)
            {
                var component = components[index];

                if (component is null)
                {
                    logger.LogInfo(
                        $"[SceneExplorer] {indent}- <missing component>");
                    continue;
                }

                var typeName = component.GetIl2CppType()?.FullName
                    ?? component.GetType().FullName
                    ?? component.GetType().Name;

                logger.LogInfo(
                    $"[SceneExplorer] {indent}- {typeName}");
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                $"[SceneExplorer] {indent}- component enumeration failed: {exception.Message}");
        }
    }

    [HideFromIl2Cpp]
    private static string SafeTag(GameObject gameObject)
    {
        try
        {
            return gameObject.tag;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    [HideFromIl2Cpp]
    internal static string GetTransformPath(Transform? transform)
    {
        if (transform is null)
        {
            return "<null>";
        }

        try
        {
            var parts = new List<string>();
            var current = transform;

            while (current is not null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }
        catch
        {
            return transform.name;
        }
    }
}

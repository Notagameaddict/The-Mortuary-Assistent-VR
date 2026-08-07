using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MortuaryAssistantVR.XR;

internal static class ToolMenuUiProbe
{
    private static ManualLogSource? _logger;
    private static GameObject? _circleRet;
    private static bool _lastVisible;
    private static bool _resolvedLogged;

    internal static bool CircleRetVisible =>
        _circleRet != null &&
        _circleRet.activeInHierarchy;

    internal static void Update(
        ManualLogSource? logger)
    {
        if (logger is not null)
        {
            _logger =
                logger;
        }

        var scene =
            SceneManager.GetActiveScene();

        if (!scene.IsValid() ||
            !string.Equals(
                scene.name,
                "Mortuary",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_circleRet == null)
        {
            _circleRet =
                GameObject.Find(
                    "InGameUI/Rsystem/CircleRet");

            if (_circleRet == null)
            {
                return;
            }

            if (!_resolvedLogged)
            {
                _resolvedLogged =
                    true;

                _logger?.LogInfo(
                    "[ToolMenuUI] CircleRet runtime object resolved.");
            }
        }

        var visible =
            _circleRet.activeInHierarchy;

        if (visible ==
            _lastVisible)
        {
            return;
        }

        _lastVisible =
            visible;

        _logger?.LogInfo(
            $"[ToolMenuUI] CircleRet " +
            $"{(visible ? "visible" : "hidden")}.");
    }

    internal static void Reset()
    {
        _circleRet =
            null;

        _lastVisible =
            false;

        _resolvedLogged =
            false;
    }
}

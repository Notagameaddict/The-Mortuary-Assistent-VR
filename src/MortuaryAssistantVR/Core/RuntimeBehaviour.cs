using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;
using MortuaryAssistantVR.Config;
using MortuaryAssistantVR.Diagnostics;
using MortuaryAssistantVR.XR;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MortuaryAssistantVR.Core;

public sealed class RuntimeBehaviour : MonoBehaviour
{
    private ManualLogSource? _logger;
    private bool _initialized;

    public RuntimeBehaviour(IntPtr pointer)
        : base(pointer)
    {
    }

    [HideFromIl2Cpp]
    public void Initialize(ManualLogSource logger)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _logger = logger;

        SceneManager.add_sceneLoaded(
            (Action<Scene, LoadSceneMode>)OnSceneLoaded);

        _logger.LogInfo("Runtime initialized.");
        RuntimeDiagnostics.LogEnvironment(_logger);

        if (ModConfig.EnableOpenXrRuntimeProbe.Value)
        {
            OpenXrRuntimeProbe.LogStatus(_logger);
        }

        if (ModConfig.EnableXrBackend.Value)
        {
            XrBackendManager.Initialize(
                _logger,
                ModConfig.AttemptXrStartup.Value);
        }

        var activeScene = SceneManager.GetActiveScene();

        if (activeScene.IsValid())
        {
            InspectScene(activeScene);
            HandleGameplayScene(activeScene.name);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_logger is null)
        {
            return;
        }

        _logger.LogInfo($"Scene loaded: '{scene.name}' ({mode}).");
        InspectScene(scene);
        HandleGameplayScene(scene.name);
    }

    [HideFromIl2Cpp]
    private void HandleGameplayScene(string sceneName)
    {
        if (_logger is null)
        {
            return;
        }

        var isGameplayScene =
            string.Equals(sceneName, "Mortuary", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sceneName, "Apartment", StringComparison.OrdinalIgnoreCase);

        if (!isGameplayScene)
        {
            _logger.LogInfo(
                $"Gameplay camera work skipped scene '{sceneName}'.");
            return;
        }

        if (ModConfig.EnableCameraProbe.Value)
        {
            CameraProbe.LogGameplayCamera(_logger, sceneName);
        }

        if (ModConfig.EnableRenderPipelineProbe.Value)
        {
            RenderPipelineProbe.LogStatus(_logger, sceneName);
        }

        if (ModConfig.EnableXrRigBootstrap.Value)
        {
            XrRigBootstrap.TryCreate(_logger, sceneName);
        }

        XrBackendManager.LogState(_logger);
    }

    [HideFromIl2Cpp]
    private void InspectScene(Scene scene)
    {
        if (_logger is null || !ModConfig.EnableDiagnostics.Value)
        {
            return;
        }

        RuntimeDiagnostics.LogSceneSummary(_logger, scene);

        if (ModConfig.LogCamerasOnSceneLoad.Value)
        {
            RuntimeDiagnostics.LogCameras(_logger);
        }

        RuntimeDiagnostics.LogXrStatus(_logger);

        if (ModConfig.EnableSceneExplorer.Value)
        {
            SceneExplorer.LogScene(
                _logger,
                scene,
                ModConfig.SceneExplorerMaxDepth.Value,
                ModConfig.SceneExplorerMaxObjects.Value,
                ModConfig.LogComponents.Value);
        }
    }
}

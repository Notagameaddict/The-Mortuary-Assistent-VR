using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using MortuaryAssistantVR.Config;
using MortuaryAssistantVR.Core;
using UnityEngine;

namespace MortuaryAssistantVR;

[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
public sealed class Plugin : BasePlugin
{
    public override void Load()
    {
        Log.LogInfo($"Loading {PluginInfo.Name} v{PluginInfo.Version}.");

        try
        {
            ModConfig.Bind(Config);
            ClassInjector.RegisterTypeInIl2Cpp<RuntimeBehaviour>();

            var host = new GameObject($"{PluginInfo.Name}.Runtime");
            UnityEngine.Object.DontDestroyOnLoad(host);

            var runtime = host.AddComponent<RuntimeBehaviour>();
            runtime.Initialize(Log);

            Log.LogInfo("Runtime behaviour created.");
            Log.LogWarning("This bootstrap build does not enable stereoscopic VR yet.");
        }
        catch (Exception exception)
        {
            Log.LogError($"Plugin startup failed: {exception}");
            throw;
        }
    }
}

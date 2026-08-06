# Building

## Prerequisites

- Visual Studio 2022 with the .NET desktop development workload, or .NET 6 SDK
- The Mortuary Assistant installed through Steam
- BepInEx Unity IL2CPP x64 installed and run at least once

The expected game installation contains:

```text
BepInEx\core\BepInEx.Core.dll
BepInEx\core\BepInEx.Unity.IL2CPP.dll
BepInEx\core\Il2CppInterop.Runtime.dll
BepInEx\interop\UnityEngine.CoreModule.dll
BepInEx\interop\UnityEngine.SceneManagementModule.dll
BepInEx\interop\UnityEngine.XRModule.dll
```

## Configure the game path

Copy:

```text
Directory.Build.props.example
```

to:

```text
Directory.Build.props
```

Edit `MortuaryAssistantDir` so that it points to your installation.

`Directory.Build.props` is intentionally ignored by Git because each contributor may use a different path.

## Build in Visual Studio

1. Open `MortuaryAssistantVR.sln`.
2. Select `Debug`.
3. Build the solution.
4. Find the plugin at:

   ```text
   src\MortuaryAssistantVR\bin\Debug\net6.0\MortuaryAssistantVR.dll
   ```

## Build with PowerShell

From the repository root:

```powershell
.\tools\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\The Mortuary Assistant"
```

To copy the result into the game automatically:

```powershell
.\tools\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\The Mortuary Assistant" -Deploy
```

## Manual installation

Create:

```text
<game>\BepInEx\plugins\MortuaryAssistantVR\
```

Copy `MortuaryAssistantVR.dll` into that folder.

## Verify the plugin

Start the game and open:

```text
<game>\BepInEx\LogOutput.log
```

A successful load should include messages similar to:

```text
[MortuaryAssistantVR] Loading MortuaryAssistantVR ...
[MortuaryAssistantVR] Runtime behaviour created.
[MortuaryAssistantVR] Scene loaded: ...
```

The first run also creates:

```text
<game>\BepInEx\config\com.notagameaddict.mortuaryassistantvr.cfg
```

## Important

The v0.1 bootstrap does not enable stereoscopic rendering. It only verifies that our plugin can load safely and inspect the game's runtime.

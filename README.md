# The Mortuary Assistant VR

An experimental, open-source VR mod for **The Mortuary Assistant**.

> [!IMPORTANT]
> This project is in very early development. The current milestone is a BepInEx IL2CPP bootstrap plugin with logging, configuration, and basic XR runtime diagnostics. It does **not** yet render the game in stereoscopic VR or provide motion controls.

## Confirmed game environment

- Unity 2021.2.4f1
- Windows x64
- Unity IL2CPP
- BepInEx 6 IL2CPP
- Tested mod loader build: `6.0.0-be.785+6abdba4`

## Current milestone: v0.1 bootstrap

- [x] BepInEx plugin entry point
- [x] Configuration file
- [x] Persistent runtime component
- [x] Scene and camera diagnostics
- [x] Basic Unity XR status logging
- [ ] Verified build on a contributor machine
- [ ] First in-game test
- [ ] OpenXR loader integration
- [ ] Stereoscopic VR camera

## Repository layout

```text
.
├── docs/
├── src/MortuaryAssistantVR/
├── tools/
├── CHANGELOG.md
├── LICENSE
└── README.md
```

## Requirements

- A legitimate Steam installation of **The Mortuary Assistant**
- BepInEx Unity IL2CPP x64 6.x
- .NET 6 SDK
- Visual Studio 2022 or another C# IDE supporting .NET 6

## Quick start for developers

1. Clone this repository.
2. Copy `Directory.Build.props.example` to `Directory.Build.props`.
3. Edit `MortuaryAssistantDir` in that file so it points to the game folder.
4. Open `MortuaryAssistantVR.sln`.
5. Build the solution in `Debug`.
6. Copy `MortuaryAssistantVR.dll` into:

   ```text
   <game>\BepInEx\plugins\MortuaryAssistantVR\
   ```

See [docs/BUILDING.md](docs/BUILDING.md) for detailed instructions.

## First test

After installing the plugin, start the game and inspect:

```text
<game>\BepInEx\LogOutput.log
```

Search for `[MortuaryAssistantVR]`. A successful bootstrap should report the mod version, active scene, detected cameras, and Unity XR status.

## Documentation

- [Roadmap](docs/ROADMAP.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Building](docs/BUILDING.md)
- [Contributing](docs/CONTRIBUTING.md)

## Legal notice

This project is an unofficial fan-made modification. It is not affiliated with or endorsed by DarkStone Digital, DreadXP, or the game's rights holders. No game files are distributed by this repository.

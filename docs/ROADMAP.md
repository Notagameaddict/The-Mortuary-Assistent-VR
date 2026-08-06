# Roadmap

The roadmap is intentionally incremental. Each milestone must remain testable before more invasive features are added.

## v0.1 — Bootstrap

- BepInEx IL2CPP plugin loads.
- Config file is generated.
- Persistent runtime component survives scene changes.
- Scene and camera information is logged.
- Unity XR state is reported.
- First build is tested in the retail game.

## v0.2 — XR initialization proof of concept

- Determine whether Unity's bundled XR module can initialize a usable provider.
- If not, select and integrate an appropriate OpenXR bootstrap approach.
- Confirm HMD pose tracking.
- Confirm left-eye and right-eye rendering.
- Preserve scripted game camera effects where practical.

## v0.3 — Player camera integration

- Identify the active gameplay camera and player root.
- Attach a VR origin without causing camera feedback loops.
- Separate head rotation from body yaw.
- Handle cutscenes, menus, loading scenes, and scripted camera changes.
- Add configurable standing and seated offsets.

## v0.4 — Controller tracking and locomotion

- Track left and right controllers.
- Add placeholder controller/hand visualization.
- Smooth locomotion.
- Snap and smooth turning.
- Crouch and height calibration.
- Comfort vignette options.

## v0.5 — Basic interaction

- Ray-based fallback interaction.
- Direct hand proximity interaction.
- Pick up and release common items.
- Preserve original gameplay events and inventory state.

## v0.6 — World interaction

- Doors.
- Drawers and cabinets.
- Buttons and switches.
- Context-sensitive tools.

## v0.7 — UI and inventory

- World-space menus.
- VR-readable prompts.
- Inventory and clipboard/tablet adaptation.
- Save/load and settings verification.

## v0.8 — Procedure-specific interactions

- Embalming tools and machines.
- Body inspection and manipulation.
- Ritual and identification mechanics.
- Two-handed interactions where appropriate.

## v0.9 — Compatibility and polish

- Performance optimization.
- Comfort settings.
- Virtual Desktop, SteamVR, and Meta OpenXR testing.
- Game update compatibility.
- Installation and troubleshooting documentation.

## v1.0 — Stable release

- Full campaign can be completed in VR.
- Known blockers are resolved or documented.
- Installer/package and release notes are available.

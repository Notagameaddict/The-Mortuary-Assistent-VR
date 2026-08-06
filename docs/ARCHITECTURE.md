# Architecture

## Goals

The mod should remain modular, observable, and easy to disable when a game update breaks one subsystem.

## Modules

### Core

Owns plugin startup, configuration, lifetime, and logging.

### Diagnostics

Reports active scenes, cameras, XR state, and future game-component discovery. Diagnostic code must not alter gameplay.

### XR

Will own OpenXR initialization, XR origin management, pose acquisition, and rendering configuration.

### Camera

Will discover the game's active camera, preserve camera effects, and synchronize the VR rig with scripted camera state.

### Input

Will translate OpenXR controller input into mod actions and carefully bridge selected actions into the original input system.

### Locomotion

Will manage movement, turning, crouching, body yaw, and comfort settings.

### Interaction

Will contain grabbing, ray interaction, doors, drawers, tools, and physics-hand behavior.

### UI

Will adapt screen-space canvases and prompts for comfortable viewing in VR.

## Design principles

1. **Do not patch blindly.** Every Harmony patch must name the game type and method it targets and include a fallback when that target is absent.
2. **Prefer observation first.** Log and inspect scene objects before modifying them.
3. **Keep gameplay authoritative.** Whenever possible, invoke the game's existing interaction and inventory logic rather than replacing it.
4. **Fail safely.** If XR initialization fails, the plugin should log the problem and avoid breaking ordinary desktop startup.
5. **Avoid distributing game assemblies.** Local game and BepInEx DLLs are compile-time references only.
6. **Test one milestone at a time.** Camera work begins only after the bootstrap build is confirmed.

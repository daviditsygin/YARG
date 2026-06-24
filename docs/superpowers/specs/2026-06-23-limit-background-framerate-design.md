# Limit Background Framerate — Design

## Problem

When the YARG window loses focus, the engine keeps running the full player loop
at the configured FPS cap, causing unnecessary CPU/GPU load. ProjectSettings has
`runInBackground: 1`, so Unity does not throttle on its own. Even with "Pause on
Focus Loss" enabled (which sets `Time.timeScale = 0` and pauses song audio), the
engine continues **rendering** at full rate — that rendering is the wasted load.

## Goal

When the window is unfocused, drop the engine to a low framerate to cut CPU/GPU
load, and restore the user's normal framerate when focus returns. Behavior is
opt-out via a setting.

## Non-goals

- Hard-halting the player loop (`runInBackground = false`). Rejected: would also
  suspend audio threads, controller hot-plug detection, and networking.
- Configurable background FPS value or per-context (menu vs song) behavior.
- Any change to "Pause on Focus Loss". The two features are independent.

## Design

### Setting

- `LimitBackgroundFramerate` — `ToggleSetting`, default **on**.
- Declared in `SettingsManager.Settings.cs` (no callback needed — read on focus
  change).
- Displayed in the **Graphics** tab under the **Display** header, after
  `FpsCap` / `VenueFpsCap` (where framerate controls live), in
  `SettingsManager.cs` `DisplayedSettingsTabs`.
- Label "Limit Background Framerate" + description added to
  `Assets/StreamingAssets/lang/en-US.json`.

### Behavior

App-wide (covers menus and gameplay) via the existing
`GlobalVariables.OnApplicationFocus(bool hasFocus)` handler — the persistent
`MonoSingleton` that already hosts the mute-on-focus-loss logic.

A `const int BackgroundFrameRate = 10;` defines the throttled rate.
A private `bool _framerateLimitedFromFocusLoss;` tracks whether we throttled, so
restore only runs when we actually changed the values.

On **focus loss**, if `LimitBackgroundFramerate` is enabled and not already
throttled:
- `QualitySettings.vSyncCount = 0` — VSync otherwise pins the rate to the monitor
  refresh and ignores `targetFrameRate`.
- `Application.targetFrameRate = BackgroundFrameRate`.
- Set the flag.

On **focus regain**, if the flag is set:
- Restore `QualitySettings.vSyncCount = SettingsManager.Settings.VSync.Value ? 1 : 0`.
- Restore `Application.targetFrameRate = SettingsManager.Settings.FpsCap.Value`.
- Clear the flag.

Restoring from the live setting values (not cached values) means the state
self-corrects if the user changed VSync or FPS Cap while the window was away.

### Interaction with Pause on Focus Loss

Independent. If both are enabled mid-song: game logic is paused (`timeScale = 0`)
and rendering drops to 10 FPS. If only this setting is enabled: the song keeps
playing but renders at 10 FPS while unfocused.

## Edge cases

- **Changing FPS Cap / VSync while unfocused:** their setting callbacks write
  `Application.targetFrameRate` / `vSyncCount` directly, overriding the throttle
  until the next focus loss. Low impact, accepted; not guarded.
- **Setting toggled off while unfocused:** the flag is still set, so the next
  focus regain restores normal framerate (correct — we restore what we changed).
- **Editor:** the throttle still applies via `targetFrameRate`; acceptable.

## Testing

The logic is a small state transition over `targetFrameRate` / `vSyncCount` keyed
on (hasFocus, setting enabled, flag). The effect is on Unity globals that are not
unit-testable in isolation, so verification is manual: build, tab out, and confirm
reduced framerate (FPS counter) and lower CPU/GPU usage (Activity Monitor), then
confirm it restores to the configured cap on refocus.

## Affected files

- `Assets/Script/Settings/SettingsManager.Settings.cs` — new setting.
- `Assets/Script/Settings/SettingsManager.cs` — Graphics/Display tab entry.
- `Assets/Script/Persistent/GlobalVariables.cs` — throttle/restore in
  `OnApplicationFocus`.
- `Assets/StreamingAssets/lang/en-US.json` — localized name/description.

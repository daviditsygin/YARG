# Limit Background Framerate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Drop the engine to ~10 FPS while the YARG window is unfocused to cut CPU/GPU load, restoring the user's framerate on refocus, behind an opt-out Graphics setting.

**Architecture:** A new `LimitBackgroundFramerate` toggle (default on) is read by the persistent `GlobalVariables.OnApplicationFocus` handler — which already hosts the mute-on-focus-loss logic. On focus loss it sets `targetFrameRate = 10` (and disables VSync so the cap applies); on refocus it restores both from the live VSync/FpsCap settings.

**Tech Stack:** Unity (C#), YARG settings system (`ToggleSetting`), JSON localization.

**Branch:** `pause-engine-on-focus-loss`, based on latest `origin/dev` (which already contains the merged mute-on-focus-loss feature).

**Testing note:** This feature manipulates Unity engine globals (`Application.targetFrameRate`, `QualitySettings.vSyncCount`) that are not unit-testable in isolation, and YARG has no play-mode test harness for focus events. Verification is a manual build-and-observe procedure (Task 3), consistent with the design spec.

---

### Task 1: Add the `LimitBackgroundFramerate` setting and wire it into the UI

This task makes the toggle exist and appear in Settings → Graphics → Display. It has no runtime effect yet (read in Task 2).

**Files:**
- Modify: `Assets/Script/Settings/SettingsManager.Settings.cs` (Graphics region, near `VenueFpsCap`)
- Modify: `Assets/Script/Settings/SettingsManager.cs` (Graphics tab, Display header)
- Modify: `Assets/StreamingAssets/lang/en-US.json` (settings localization block)

- [ ] **Step 1: Declare the setting**

In `Assets/Script/Settings/SettingsManager.Settings.cs`, find the Graphics region:

```csharp
            #region Graphics

            public ToggleSetting VSync       { get; } = new(true, VSyncCallback);
            public IntSetting    FpsCap      { get; } = new(60, 0, onChange: FpsCapCallback);
            public IntSetting    VenueFpsCap { get; } = new(60, 0);
```

Add the new setting directly after `VenueFpsCap`:

```csharp
            #region Graphics

            public ToggleSetting VSync       { get; } = new(true, VSyncCallback);
            public IntSetting    FpsCap      { get; } = new(60, 0, onChange: FpsCapCallback);
            public IntSetting    VenueFpsCap { get; } = new(60, 0);

            public ToggleSetting LimitBackgroundFramerate { get; } = new(true);
```

- [ ] **Step 2: Add the setting to the Graphics → Display tab**

In `Assets/Script/Settings/SettingsManager.cs`, find the Display header in the Graphics tab:

```csharp
                new HeaderMetadata("Display"),
                nameof(Settings.VSync),
                new FieldMetadata(nameof(Settings.FpsCap)),
                new FieldMetadata(nameof(Settings.VenueFpsCap), isAdvanced: true),
                nameof(Settings.FullscreenMode),
```

Add the entry after the `VenueFpsCap` line:

```csharp
                new HeaderMetadata("Display"),
                nameof(Settings.VSync),
                new FieldMetadata(nameof(Settings.FpsCap)),
                new FieldMetadata(nameof(Settings.VenueFpsCap), isAdvanced: true),
                nameof(Settings.LimitBackgroundFramerate),
                nameof(Settings.FullscreenMode),
```

- [ ] **Step 3: Add the localized name and description**

In `Assets/StreamingAssets/lang/en-US.json`, find the `FpsCap` entry:

```json
            "FpsCap": {
                "Name": "FPS Cap",
                "Description": "The framerate cap. <color=white>YOU DO NOT NEED TO PLAY YARG ON 1000FPS.</color> VSync recommended."
            },
```

Add a new entry directly after that closing `},`:

```json
            "FpsCap": {
                "Name": "FPS Cap",
                "Description": "The framerate cap. <color=white>YOU DO NOT NEED TO PLAY YARG ON 1000FPS.</color> VSync recommended."
            },
            "LimitBackgroundFramerate": {
                "Name": "Limit Background Framerate",
                "Description": "Reduces the framerate while YARG is unfocused (tabbed out) to lower CPU/GPU usage. The framerate is restored when focus returns."
            },
```

- [ ] **Step 4: Verify the JSON is still valid**

Run: `python3 -c "import json; json.load(open('Assets/StreamingAssets/lang/en-US.json', encoding='utf-8-sig')); print('valid')"`
Expected: prints `valid`

- [ ] **Step 5: Commit**

```bash
git add Assets/Script/Settings/SettingsManager.Settings.cs Assets/Script/Settings/SettingsManager.cs Assets/StreamingAssets/lang/en-US.json
git commit -m "feat: add Limit Background Framerate setting"
```

---

### Task 2: Throttle and restore the framerate on focus change

This task adds the runtime behavior to the existing `OnApplicationFocus` handler, alongside the mute logic.

**Files:**
- Modify: `Assets/Script/Persistent/GlobalVariables.cs` (the `OnApplicationFocus` method and the fields just above it)

- [ ] **Step 1: Replace the focus handler with one that also throttles the framerate**

In `Assets/Script/Persistent/GlobalVariables.cs`, find the existing block (the fields and method that currently handle only muting):

```csharp
        // Tracks whether audio was muted because the window lost focus,
        // so it can be restored when focus returns.
        private bool _mutedFromFocusLoss;

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                if (SettingsManager.Settings.MuteOnFocusLoss.Value && !_mutedFromFocusLoss)
                {
                    GlobalAudioHandler.SetMasterVolume(0);
                    _mutedFromFocusLoss = true;
                }
            }
            else if (_mutedFromFocusLoss)
            {
                GlobalAudioHandler.SetMasterVolume(SettingsManager.Settings.MasterMusicVolume.Value);
                _mutedFromFocusLoss = false;
            }
        }
```

Replace that entire block with:

```csharp
        // Tracks whether audio was muted because the window lost focus,
        // so it can be restored when focus returns.
        private bool _mutedFromFocusLoss;

        // Framerate the engine is limited to while the window is unfocused.
        private const int BackgroundFrameRate = 10;

        // Tracks whether the framerate was limited because the window lost focus,
        // so it can be restored when focus returns.
        private bool _framerateLimitedFromFocusLoss;

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                if (SettingsManager.Settings.MuteOnFocusLoss.Value && !_mutedFromFocusLoss)
                {
                    GlobalAudioHandler.SetMasterVolume(0);
                    _mutedFromFocusLoss = true;
                }

                if (SettingsManager.Settings.LimitBackgroundFramerate.Value && !_framerateLimitedFromFocusLoss)
                {
                    // VSync would otherwise pin the framerate to the monitor's refresh
                    // rate, ignoring targetFrameRate, so disable it while unfocused.
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = BackgroundFrameRate;
                    _framerateLimitedFromFocusLoss = true;
                }
            }
            else
            {
                if (_mutedFromFocusLoss)
                {
                    GlobalAudioHandler.SetMasterVolume(SettingsManager.Settings.MasterMusicVolume.Value);
                    _mutedFromFocusLoss = false;
                }

                if (_framerateLimitedFromFocusLoss)
                {
                    // Restore from the live settings so changes made while unfocused are respected.
                    QualitySettings.vSyncCount = SettingsManager.Settings.VSync.Value ? 1 : 0;
                    Application.targetFrameRate = SettingsManager.Settings.FpsCap.Value;
                    _framerateLimitedFromFocusLoss = false;
                }
            }
        }
```

- [ ] **Step 2: Confirm the required types are already in scope**

`Application` and `QualitySettings` come from `UnityEngine`, which `GlobalVariables.cs` already imports (`using UnityEngine;` at the top). No new `using` is required.

Run: `grep -n "using UnityEngine;" Assets/Script/Persistent/GlobalVariables.cs`
Expected: prints the `using UnityEngine;` line (confirming it is present)

- [ ] **Step 3: Commit**

```bash
git add Assets/Script/Persistent/GlobalVariables.cs
git commit -m "feat: limit framerate while window is unfocused"
```

---

### Task 3: Build and manually verify

No automated test covers Unity focus events; verify in a real build.

**Files:** none (build + observe)

- [ ] **Step 1: Ensure the YARG.Core submodule matches dev**

Run: `git submodule update --init --recursive`
Expected: completes with no error; `YARG.Core/YARG.Core/package.json` exists.

- [ ] **Step 2: Build the standalone player**

Run:
```bash
/Applications/Unity/Hub/Editor/6000.2.9f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit \
  -projectPath "$HOME/src/YARG" \
  -logFile "$HOME/src/YARG/build.log" \
  -buildTarget OSXUniversal \
  -executeMethod Editor.HeadlessBuild.BuildOSX
```
Expected: `build.log` ends with `HEADLESS BUILD RESULT: Succeeded` and `build/StandaloneOSX/YARG.app` exists.

(If `Editor.HeadlessBuild.BuildOSX` is absent — it is an untracked local helper — recreate `Assets/Editor/HeadlessBuild.cs` with the `BuildOSX` method that calls `BuildPipeline.BuildPlayer` for `BuildTarget.StandaloneOSX` to `build/StandaloneOSX/YARG.app`, or build via the Unity Editor's File → Build menu.)

- [ ] **Step 3: Verify the throttle with the setting ON (default)**

1. Run: `open build/StandaloneOSX/YARG.app`
2. Settings → Graphics → Display: confirm "Limit Background Framerate" is present and **on**. Enable the FPS counter (Settings → Graphics → "Show FPS Counter").
3. With YARG focused, note the FPS (near the cap, e.g. 60).
4. Click another window so YARG loses focus. Open Activity Monitor and confirm YARG's CPU/GPU usage drops sharply; the FPS counter should read ~10 when YARG repaints.
5. Click back onto YARG. Confirm the framerate returns to the configured cap.

Expected: load drops while unfocused, restores on refocus.

- [ ] **Step 4: Verify the setting OFF disables the behavior**

1. In Settings → Graphics → Display, turn "Limit Background Framerate" **off**.
2. Unfocus YARG again.

Expected: framerate and CPU/GPU usage stay at the normal full rate (no throttle).

- [ ] **Step 5: Verify VSync restores correctly**

1. Turn "Limit Background Framerate" back **on** and ensure VSync is **on**.
2. Unfocus YARG (throttles, VSync internally disabled), then refocus.

Expected: after refocus the framerate is capped by VSync again (no uncapped/torn rendering), confirming `vSyncCount` was restored.

---

## Integration note

The merged mute-on-focus-loss feature and this feature both live in
`GlobalVariables.OnApplicationFocus`. This plan extends that single method, so no
conflict exists as long as the branch is built on the `dev` that already contains
the mute feature (it is). If rebasing later, keep both the mute and framerate
branches in the one method.

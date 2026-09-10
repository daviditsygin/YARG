#if YARG_TEST_BUILD
using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Gameplay;
using YARG.Player;
using YARG.Song;

namespace YARG.Helpers
{
    /// <summary>
    ///     Test-build harness for cross-platform render comparisons. Drop an
    ///     <c>autoplay.txt</c> into the persistent data folder with
    ///     <c>song=&lt;title fragment&gt;</c> and <c>time=&lt;song seconds&gt;</c>:
    ///     once the main menu is ready the song starts with a bot on guitar,
    ///     <c>autoplay.png</c> is written next to the trigger at that song
    ///     time, and desktop players quit. Every platform renders the same
    ///     frame, so the screenshots can be compared pixel for pixel.
    /// </summary>
    public class AutoPlayHarness : MonoBehaviour
    {
        private const string TRIGGER_FILE = "autoplay.txt";
        private const float QUIT_DELAY = 2f;

        private string _songQuery;
        private double _captureTime;
        private string _outputName = "autoplay.png";

        private bool  _started;
        private bool  _captured;
        private float _capturedAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string trigger = Path.Combine(PathHelper.PersistentDataPath, TRIGGER_FILE);
            if (!File.Exists(trigger))
            {
                return;
            }

            var go = new GameObject("AutoPlayHarness");
            DontDestroyOnLoad(go);
            var harness = go.AddComponent<AutoPlayHarness>();
            harness.Parse(File.ReadAllLines(trigger));

            // One shot: the next launch behaves normally
            File.Delete(trigger);
            YargLogger.LogInfo($"[AUTOPLAY] armed: song '{harness._songQuery}' at {harness._captureTime:F2} s");
        }

        private void Parse(string[] lines)
        {
            foreach (string line in lines)
            {
                int split = line.IndexOf('=');
                if (split < 0)
                {
                    continue;
                }

                string key = line[..split].Trim();
                string value = line[(split + 1)..].Trim();
                switch (key)
                {
                    case "song":
                        _songQuery = value;
                        break;
                    case "time":
                        double.TryParse(value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out _captureTime);
                        break;
                    case "out":
                        _outputName = value;
                        break;
                }
            }
        }

        private void Update()
        {
            if (!_started)
            {
                // GameManager.Awake looks up the song's source name, so the
                // source table must have finished loading (or fallen back)
                if (SceneManager.GetActiveScene().buildIndex != (int) SceneIndex.Menu ||
                    SongContainer.Count == 0 || LoadingScreen.IsActive || SongSources.Default == null)
                {
                    return;
                }

                _started = true;
                StartSong();
                return;
            }

            if (_captured)
            {
                if (Time.realtimeSinceStartup - _capturedAt > QUIT_DELAY)
                {
                    if (!Application.isMobilePlatform)
                    {
                        Application.Quit();
                    }

                    Destroy(gameObject);
                }

                return;
            }

            var gameManager = FindAnyObjectByType<GameManager>();
            if (gameManager == null || !gameManager.IsSongStarted || gameManager.Paused ||
                gameManager.SongTime < _captureTime)
            {
                return;
            }

            _captured = true;
            _capturedAt = Time.realtimeSinceStartup;
            StartCoroutine(Capture(Path.Combine(PathHelper.PersistentDataPath, _outputName), gameManager.SongTime));
        }

        // ScreenCapture.CaptureScreenshot resolves paths differently per
        // platform (relative to persistentDataPath on phones), so read the
        // frame back and write the file ourselves
        private System.Collections.IEnumerator Capture(string outputPath, double songTime)
        {
            yield return new WaitForEndOfFrame();

            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(outputPath, texture.EncodeToPNG());
            Destroy(texture);

            YargLogger.LogInfo($"[AUTOPLAY] captured {outputPath} at song time {songTime:F3} s, " +
                $"{Screen.width}x{Screen.height}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            DumpRenderState();
        }

        // Everything that could make the same venue render differently per platform
        private static void DumpRenderState()
        {
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            YargLogger.LogInfo($"[RENDER] {SystemInfo.graphicsDeviceType} shaderLevel={SystemInfo.graphicsShaderLevel} " +
                $"pipeline={(pipeline != null ? pipeline.name : "?")} hdr={pipeline?.supportsHDR} " +
                $"lightsMode={pipeline?.additionalLightsRenderingMode} maxLights={pipeline?.maxAdditionalLightsCount} " +
                $"colorSpace={QualitySettings.activeColorSpace} mipLimit={QualitySettings.globalTextureMipmapLimit}");
            YargLogger.LogInfo($"[RENDER] ambient mode={RenderSettings.ambientMode} intensity={RenderSettings.ambientIntensity} " +
                $"light={RenderSettings.ambientLight} reflection={RenderSettings.defaultReflectionMode}/{RenderSettings.reflectionIntensity} " +
                $"fog={RenderSettings.fog} sun={(RenderSettings.sun != null ? RenderSettings.sun.name : "none")}");

            var unitProperty = typeof(Light).GetProperty("lightUnit");
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string unit = unitProperty != null ? unitProperty.GetValue(light)?.ToString() : "n/a";
                YargLogger.LogInfo($"[RENDER] light '{light.name}' {light.type} on={light.isActiveAndEnabled} " +
                    $"intensity={light.intensity} unit={unit} range={light.range} spot={light.spotAngle} " +
                    $"color={light.color} mask={light.cullingMask} shadows={light.shadows} scene={light.gameObject.scene.name}");
            }

            foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var data = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                var target = camera.targetTexture;
                YargLogger.LogInfo($"[RENDER] camera '{camera.name}' on={camera.isActiveAndEnabled} hdr={camera.allowHDR} " +
                    $"post={data?.renderPostProcessing} volumes={data?.volumeLayerMask.value} aa={data?.antialiasing} " +
                    $"depth={data?.requiresDepthTexture} mask={camera.cullingMask} " +
                    $"target={(target != null ? $"{target.width}x{target.height} {target.graphicsFormat}" : "screen")}");
            }

            foreach (var volume in UnityEngine.Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                YargLogger.LogInfo($"[RENDER] volume '{volume.name}' on={volume.isActiveAndEnabled} global={volume.isGlobal} " +
                    $"weight={volume.weight} layer={volume.gameObject.layer} profile={(volume.sharedProfile != null ? volume.sharedProfile.name : "none")}");
            }
        }

        private void StartSong()
        {
            SongEntry song = null;
            foreach (var entry in SongContainer.UnfilteredSongs)
            {
                if (entry.Name.ToString().Contains(_songQuery, StringComparison.OrdinalIgnoreCase))
                {
                    song = entry;
                    break;
                }
            }

            if (song == null)
            {
                YargLogger.LogError($"[AUTOPLAY] no song matches '{_songQuery}'");
                Destroy(gameObject);
                return;
            }

            // Play with a bot on expert guitar; everyone else sits out
            YargProfile botProfile = null;
            foreach (var profile in PlayerContainer.Profiles)
            {
                if (profile.IsBot && profile.GameMode == GameMode.FiveFretGuitar)
                {
                    botProfile = profile;
                    break;
                }
            }

            if (botProfile == null)
            {
                botProfile = new YargProfile
                {
                    Name = "AutoPlay Bot",
                    NoteSpeed = 5,
                    HighwayLength = 1,
                    GameMode = GameMode.FiveFretGuitar,
                    IsBot = true,
                };
                PlayerContainer.AddProfile(botProfile);
            }

            botProfile.CurrentInstrument = Instrument.FiveFretGuitar;
            botProfile.CurrentDifficulty = Difficulty.Expert;

            var botPlayer = PlayerContainer.GetPlayerFromProfile(botProfile) ??
                PlayerContainer.CreatePlayerFromProfile(botProfile, false);
            foreach (var player in PlayerContainer.Players)
            {
                player.SittingOut = player != botPlayer;
            }

            var state = PersistentState.Default;
            state.CurrentSong = song;
            state.SongSpeed = 1f;
            GlobalVariables.State = state;

            YargLogger.LogInfo($"[AUTOPLAY] starting '{song.Name}' by {song.Artist}");
            GlobalVariables.Instance.LoadScene(SceneIndex.Gameplay);
        }
    }
}
#endif

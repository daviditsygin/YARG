#if YARG_TEST_BUILD && !UNITY_EDITOR
using UnityEngine;
using YARG.Core.Logging;
using YARG.Gameplay;

namespace YARG.Helpers
{
    /// <summary>
    ///     Logs every frame that takes far longer than its neighbours, with
    ///     the song position, so a stutter reported from a device can be
    ///     matched to what was happening in the chart at that moment.
    /// </summary>
    internal static class FrameHitchLogger
    {
        private const float HITCH_SECONDS = 0.12f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if (!Application.isMobilePlatform)
            {
                return;
            }

            YargLogger.LogInfo($"[GFX] {SystemInfo.graphicsDeviceName} / {SystemInfo.graphicsDeviceType} " +
                $"{SystemInfo.graphicsDeviceVersion}; screen {Screen.width}x{Screen.height} @ {Screen.dpi} dpi; " +
                $"quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}; " +
                $"parallel PSO creation {SystemInfo.supportsParallelPSOCreation}");

            var logger = new GameObject("Frame Hitch Logger")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Object.DontDestroyOnLoad(logger);
            logger.AddComponent<Watcher>();
        }

        private sealed class Watcher : MonoBehaviour
        {
            private void Update()
            {
                float frame = Time.unscaledDeltaTime;
                if (frame < HITCH_SECONDS)
                {
                    return;
                }

                var manager = Object.FindAnyObjectByType<GameManager>();
                string where = manager != null && manager.IsSongStarted
                    ? $"song {manager.SongTime:0.00}s"
                    : "outside gameplay";
                YargLogger.LogInfo($"[HITCH] {frame * 1000f:0} ms frame ({where}, frame {Time.frameCount})");
            }
        }
    }
}
#endif

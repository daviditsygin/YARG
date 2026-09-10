using System.Diagnostics;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;
using YARG.Core.Logging;

namespace YARG.Helpers
{
    /// <summary>
    ///     Pre-creates the GPU pipeline states a play session needs so they
    ///     are not compiled mid-song, which stutters on phones. Mobile players
    ///     load a state collection recorded on the same platform and graphics
    ///     API from StreamingAssets and warm it up once, under the first song's
    ///     loading screen. Test builds keep tracing the states they use and save
    ///     the collection to the data folder for the next recording.
    /// </summary>
    public static class GraphicsStateWarmup
    {
        private const string FOLDER = "graphicsstate";

        private static bool _warmupStarted;
        private static GraphicsStateCollection _warmup;
        private static GraphicsStateCollection _trace;

        private static string FileName => $"{Application.platform}-{SystemInfo.graphicsDeviceType}.graphicsstate";
        private static string ShippedPath => Path.Combine(Application.streamingAssetsPath, FOLDER, FileName);
        private static string TracePath => Path.Combine(PathHelper.PersistentDataPath, FOLDER, FileName);

        /// <summary>
        ///     Warms up the shipped collection the first time it is called and
        ///     waits for it to finish; later calls return immediately.
        /// </summary>
        public static async UniTask WarmUpAsync()
        {
            if (_warmupStarted || !Application.isMobilePlatform)
            {
                return;
            }

            _warmupStarted = true;

            if (!File.Exists(ShippedPath))
            {
                YargLogger.LogFormatInfo("No graphics state collection shipped at {0}", ShippedPath);
                return;
            }

            _warmup = new GraphicsStateCollection();
            if (!_warmup.LoadFromFile(ShippedPath))
            {
                YargLogger.LogFormatWarning("Failed to load the graphics state collection at {0}", ShippedPath);
                return;
            }

            if (_warmup.runtimePlatform != Application.platform ||
                _warmup.graphicsDeviceType != SystemInfo.graphicsDeviceType)
            {
                YargLogger.LogFormatWarning("Graphics state collection is for {0}/{1}, not {2}/{3}",
                    _warmup.runtimePlatform, _warmup.graphicsDeviceType,
                    Application.platform, SystemInfo.graphicsDeviceType);
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            await _warmup.WarmUp();

            YargLogger.LogInfo($"Warmed up {_warmup.totalGraphicsStateCount} graphics states across " +
                $"{_warmup.variantCount} shader variants in {stopwatch.ElapsedMilliseconds} ms");
        }

#if YARG_TEST_BUILD
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartTrace()
        {
            if (!Application.isMobilePlatform)
            {
                return;
            }

            _trace = new GraphicsStateCollection();

            // Keep accumulating on top of the previous session's recording
            if (File.Exists(TracePath) && !_trace.LoadFromFile(TracePath))
            {
                _trace = new GraphicsStateCollection();
            }

            _trace.BeginTrace();
            YargLogger.LogInfo($"Graphics state tracing {(_trace.isTracing ? "started" : "NOT started")} " +
                $"(parallel PSO creation supported: {SystemInfo.supportsParallelPSOCreation})");

            SceneManager.sceneUnloaded += _ => SaveTrace();
            Application.quitting += SaveTrace;

            var saver = new GameObject("Graphics State Trace")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Object.DontDestroyOnLoad(saver);
            saver.AddComponent<TraceSaver>();
        }

        private static void SaveTrace()
        {
            if (_trace == null || !_trace.isTracing)
            {
                return;
            }

            _trace.EndTrace();
            Directory.CreateDirectory(Path.GetDirectoryName(TracePath));
            _trace.SaveToFile(TracePath);
            YargLogger.LogInfo($"Saved {_trace.totalGraphicsStateCount} graphics states across " +
                $"{_trace.variantCount} shader variants to {TracePath}");
            _trace.BeginTrace();
        }

        private class TraceSaver : MonoBehaviour
        {
            private void OnApplicationPause(bool paused)
            {
                if (paused)
                {
                    SaveTrace();
                }
            }

            private void OnApplicationQuit()
            {
                SaveTrace();
            }
        }
#endif
    }
}

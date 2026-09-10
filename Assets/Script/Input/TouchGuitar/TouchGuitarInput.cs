using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using YARG.Gameplay;
using YARG.Gameplay.Player;
using YARG.Gameplay.Visuals;

namespace YARG.Input
{
    /// <summary>
    ///     Feeds <see cref="TouchGuitarDevice"/> from touches over the highway
    ///     of the player who bound it: the lanes at the frets press the frets
    ///     and strum on touch-down, the top of the screen above the highway
    ///     fires star power. Runs only while a song is playing — every other
    ///     screen is driven by the touchscreen itself — and ignores touches
    ///     that land on UI such as the pause button.
    /// </summary>
    public class TouchGuitarInput : MonoBehaviour
    {
        // Fractions of the screen height: lanes accept touches this far above
        // the frets; star power lives above this line
        private const float LANE_ZONE_HEIGHT = 0.45f;
        private const float STAR_POWER_ZONE_BOTTOM = 0.72f;

        // Touches this far beyond the outer lanes, in lane widths, still count
        private const float LANE_REACH = 0.75f;

        private const int LAYOUT_REFRESH_FRAMES = 30;

        private static TouchGuitarInput _instance;

        private TouchGuitarDevice _device;
        private GameManager _gameManager;
        private FiveFretGuitarPlayer _player;

        private float[] _laneCentersX = Array.Empty<float>();
        private float _laneWidth;
        private float _fretsY;
        private int _layoutFrame = -1;

        private byte _lastButtons;

        private readonly List<RaycastResult> _raycastResults = new();
        private PointerEventData _pointerData;

        public static void Start()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("Touch Controls")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<TouchGuitarInput>();
        }

        private void Awake()
        {
            _device = TouchGuitarDevice.Add();
        }

        private void Update()
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null || _device == null || !_device.added)
            {
                return;
            }

            if (!TryGetPlayingGuitarist(out var player))
            {
                Publish(0);
                return;
            }

            if (Time.frameCount - _layoutFrame >= LAYOUT_REFRESH_FRAMES)
            {
                RefreshLayout(player);
            }

            if (_laneCentersX.Length == 0)
            {
                Publish(0);
                return;
            }

            byte buttons = 0;
            foreach (var touch in touchscreen.touches)
            {
                if (!touch.press.isPressed)
                {
                    continue;
                }

                var position = touch.position.ReadValue();
                if (IsOverControl(position))
                {
                    continue;
                }

                if (position.y >= STAR_POWER_ZONE_BOTTOM * Screen.height)
                {
                    if (IsOverHighway(position.x))
                    {
                        buttons |= TouchGuitarState.STAR_POWER;
                    }
                    continue;
                }

                if (position.y > _fretsY + LANE_ZONE_HEIGHT * Screen.height)
                {
                    continue;
                }

                int lane = LaneAt(position.x);
                if (lane < 0)
                {
                    continue;
                }

                buttons |= (byte) (1 << lane);
                if (touch.press.wasPressedThisFrame)
                {
                    buttons |= TouchGuitarState.STRUM;
                }
            }

            Publish(buttons);
        }

        private void Publish(byte buttons)
        {
            if (buttons == _lastButtons)
            {
                return;
            }

            _lastButtons = buttons;
            InputSystem.QueueStateEvent(_device, new TouchGuitarState { buttons = buttons });
#if YARG_TEST_BUILD
            if (buttons != 0)
            {
                YARG.Core.Logging.YargLogger.LogInfo($"[TOUCH] buttons {System.Convert.ToString(buttons, 2).PadLeft(7, '0')}");
            }
#endif
        }

        private bool TryGetPlayingGuitarist(out FiveFretGuitarPlayer player)
        {
            player = null;

            if (_gameManager == null)
            {
                _gameManager = FindAnyObjectByType<GameManager>();
                _player = null;
                if (_gameManager == null)
                {
                    return false;
                }
            }

            if (!_gameManager.IsSongStarted || _gameManager.Paused)
            {
                return false;
            }

            if (_player == null)
            {
                foreach (var candidate in FindObjectsByType<FiveFretGuitarPlayer>(FindObjectsSortMode.None))
                {
                    if (candidate.Player != null && candidate.Player.Bindings.ContainsDevice(_device))
                    {
                        _player = candidate;
                        _layoutFrame = -1;
                        break;
                    }
                }
            }

            player = _player;
            return player != null;
        }

        private void RefreshLayout(FiveFretGuitarPlayer player)
        {
            _layoutFrame = Time.frameCount;

            var fretArray = player.GetComponentInChildren<FretArray>();
            var frets = fretArray != null ? fretArray.GetComponentsInChildren<Fret>() : Array.Empty<Fret>();
            if (frets.Length < 2)
            {
                _laneCentersX = Array.Empty<float>();
                return;
            }

            // The highway matrices are GPU-space, so on top-origin APIs the
            // viewport y runs from the top
            bool flipY = SystemInfo.graphicsUVStartsAtTop;

            var centers = new float[frets.Length];
            float fretsY = 0f;
            for (int i = 0; i < frets.Length; i++)
            {
                var viewport = player.WorldToViewport(frets[i].transform.position);
                centers[i] = viewport.x * Screen.width;
                fretsY += (flipY ? 1f - viewport.y : viewport.y) * Screen.height;
            }

            Array.Sort(centers);
            float laneWidth = (centers[^1] - centers[0]) / (centers.Length - 1);
            if (!(laneWidth > 1f) || float.IsNaN(fretsY))
            {
                // The renderer has not laid the highway out yet
                _laneCentersX = Array.Empty<float>();
                return;
            }

            _laneCentersX = centers;
            _laneWidth = laneWidth;
            _fretsY = fretsY / frets.Length;
#if YARG_TEST_BUILD
            if (!_layoutLogged)
            {
                _layoutLogged = true;
                YARG.Core.Logging.YargLogger.LogInfo($"[TOUCH] lanes at x {string.Join(", ", Array.ConvertAll(centers, c => c.ToString("0")))} " +
                    $"(width {_laneWidth:0}), frets y {_fretsY:0} of {Screen.width}x{Screen.height}");
            }
#endif
        }

#if YARG_TEST_BUILD
        private bool _layoutLogged;
#endif

        private int LaneAt(float x)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _laneCentersX.Length; i++)
            {
                float distance = Mathf.Abs(x - _laneCentersX[i]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return bestDistance <= _laneWidth * LANE_REACH ? best : -1;
        }

        private bool IsOverHighway(float x)
        {
            return x >= _laneCentersX[0] - _laneWidth && x <= _laneCentersX[^1] + _laneWidth;
        }

        // A touch on an actual control (the pause button, a dialog) belongs
        // to the UI; the full-screen highway and venue images beneath every
        // touch do not count
        private bool IsOverControl(Vector2 position)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            _pointerData ??= new PointerEventData(eventSystem);
            _pointerData.position = position;
            _raycastResults.Clear();
            eventSystem.RaycastAll(_pointerData, _raycastResults);

            foreach (var result in _raycastResults)
            {
                if (result.gameObject.GetComponentInParent<Selectable>() != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

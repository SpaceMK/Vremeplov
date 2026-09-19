using System;
using BinanceTheme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TalesTensor.Map
{
    /// <summary>
    /// Full-screen overlay shown on the map until we have a GPS fix. It reflects the
    /// <see cref="GpsLocationProvider.Status"/>: a "waiting for signal" spinner while a
    /// fix is being acquired, and a clear "permission needed / location off" message
    /// (with an "Open settings" shortcut on Android) when the user has to act. It hides
    /// itself the moment the provider reports <see cref="GpsStatus.Running"/>.
    ///
    /// Built entirely in code by <see cref="MapController"/>, matching the rest of the
    /// self-assembling map UI — no prefab or scene wiring required.
    /// </summary>
    [DisallowMultipleComponent]
    public class GpsStatusOverlay : MonoBehaviour
    {
        GpsLocationProvider _gps;
        GameObject _panel;
        TextMeshProUGUI _title;
        TextMeshProUGUI _body;
        TextMeshProUGUI _spinner;
        Button _allowButton;
        Button _settingsButton;
        float _spinPhase;

        public static GpsStatusOverlay Create(GpsLocationProvider gps)
        {
            var go = new GameObject("GpsStatusOverlay");
            go.SetActive(false);
            var overlay = go.AddComponent<GpsStatusOverlay>();
            overlay._gps = gps;
            go.SetActive(true);
            return overlay;
        }

        void Awake()
        {
            EnsureEventSystem();
            Build();
        }

        void OnEnable()
        {
            if (_gps != null) _gps.OnStatusChanged += Apply;
        }

        void OnDisable()
        {
            if (_gps != null) _gps.OnStatusChanged -= Apply;
        }

        void Start()
        {
            // Reflect whatever state the provider is already in.
            if (_gps != null) Apply(_gps.Status);
        }

        void Build()
        {
            var canvasGo = new GameObject("GpsStatusCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200; // above the energy HUD / menu
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            // Dim backdrop covering the (currently black) map, also swallows taps.
            var backdrop = UiFactory.Panel("Backdrop", canvasGo.transform,
                Alpha(BinancePalette.Background, 0.92f));
            UiFactory.Stretch(backdrop.rectTransform);

            _panel = backdrop.gameObject;

            // Animated spinner dots above the title.
            _spinner = UiFactory.Text("Spinner", backdrop.transform, "", 64f,
                BinancePalette.Yellow, TextAlignmentOptions.Center);
            UiFactory.Anchor(_spinner.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 150f), new Vector2(900f, 90f));

            _title = UiFactory.Text("Title", backdrop.transform, "", 52f,
                BinancePalette.TextPrimary, TextAlignmentOptions.Center);
            UiFactory.Anchor(_title.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 40f), new Vector2(900f, 80f));

            _body = UiFactory.Text("Body", backdrop.transform, "", 34f,
                BinancePalette.TextSecondary, TextAlignmentOptions.Center);
            UiFactory.Anchor(_body.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -90f), new Vector2(820f, 180f));

            // Primary "Allow" button — used when permission was revoked mid-session.
            _allowButton = BuildButton(backdrop.transform, "AllowButton", "Allow GPS access",
                BinancePalette.Yellow, BinancePalette.OnYellow, RequestPermission);
            UiFactory.Anchor((RectTransform)_allowButton.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -240f), new Vector2(420f, 100f));

            // Secondary "Open settings" shortcut for the permanently-denied / off cases.
            _settingsButton = BuildButton(backdrop.transform, "OpenSettingsButton", "Open settings",
                BinancePalette.Line, BinancePalette.TextPrimary, OpenAppSettings);
            UiFactory.Anchor((RectTransform)_settingsButton.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -360f), new Vector2(360f, 92f));
        }

        Button BuildButton(Transform parent, string name, string label,
            Color fill, Color textColor, Action onClick)
        {
            var img = UiFactory.Panel(name, parent, fill);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());

            var text = UiFactory.Text("Label", img.transform, label, 34f,
                textColor, TextAlignmentOptions.Center);
            UiFactory.Stretch(text.rectTransform);
            return button;
        }

        void RequestPermission()
        {
            if (_gps != null) _gps.RequestPermissionAndResume();
        }

        void Apply(GpsStatus status)
        {
            bool running = status == GpsStatus.Running;
            if (_panel != null) _panel.SetActive(!running);
            if (running) return;

            bool showSpinner = status == GpsStatus.WaitingForSignal ||
                               status == GpsStatus.Initializing ||
                               status == GpsStatus.RequestingPermission;
            bool onAndroid = Application.platform == RuntimePlatform.Android;
            // The "Allow" button re-prompts for the runtime permission (Android only).
            bool showAllow = status == GpsStatus.PermissionRevoked && onAndroid;
            // The "Open settings" intent only does anything on an Android device.
            bool showSettings = (status == GpsStatus.PermissionDenied ||
                                 status == GpsStatus.Disabled ||
                                 status == GpsStatus.PermissionRevoked) && onAndroid;

            if (_spinner != null) _spinner.gameObject.SetActive(showSpinner);
            if (_allowButton != null) _allowButton.gameObject.SetActive(showAllow);
            if (_settingsButton != null) _settingsButton.gameObject.SetActive(showSettings);

            switch (status)
            {
                case GpsStatus.RequestingPermission:
                    SetText("Requesting location…",
                        "Please allow location access so we can place you on the map.");
                    break;
                case GpsStatus.PermissionDenied:
                    SetText("Location permission needed",
                        "TalesTensor needs your location to show you on the map. " +
                        "Please allow location access — we'll keep asking, or you can " +
                        "enable it in settings.");
                    break;
                case GpsStatus.PermissionRevoked:
                    SetText("GPS permission required",
                        "TalesTensor needs access to your location to play this experience. " +
                        "Tap \"Allow GPS access\" to grant permission and continue.");
                    break;
                case GpsStatus.Disabled:
                    SetText("Location is turned off",
                        "Turn on location services on your device to start exploring the map.");
                    break;
                case GpsStatus.WaitingForSignal:
                case GpsStatus.Initializing:
                default:
                    SetText("Waiting for GPS signal", "Acquiring your location…");
                    break;
            }
        }

        void SetText(string title, string body)
        {
            if (_title != null) _title.text = title;
            if (_body != null) _body.text = body;
        }

        void Update()
        {
            // Animate the spinner dots while visible.
            if (_spinner == null || !_spinner.gameObject.activeSelf) return;
            _spinPhase += Time.unscaledDeltaTime;
            int dots = 1 + (Mathf.FloorToInt(_spinPhase * 2f) % 3); // 1..3, ~2 changes/sec
            _spinner.text = new string('•', dots);
        }

        static void OpenAppSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
                using var uriClass = new AndroidJavaClass("android.net.Uri");
                using var intent = new AndroidJavaObject(
                    "android.content.Intent", "android.settings.APPLICATION_DETAILS_SETTINGS");
                string pkg = activity.Call<string>("getPackageName");
                using var uri = uriClass.CallStatic<AndroidJavaObject>("fromParts", "package", pkg, null);
                intent.Call<AndroidJavaObject>("setData", uri);
                activity.Call("startActivity", intent);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GPS] Could not open app settings: {e.Message}");
            }
#endif
        }

        static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            if (FindAnyObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}

using BinanceTheme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TalesTensor.Map
{
    /// <summary>
    /// Map-scene screen chrome built entirely in code (the MapScene has no authored
    /// canvas): a top-left profile button plus the shared "Coming soon" popup — the
    /// same <see cref="PopupController"/> used on the login screen — styled to the
    /// Binance palette / styleguide. Reachable via <see cref="Instance"/> so the pin
    /// Spend buttons can raise the popup too.
    /// </summary>
    [DisallowMultipleComponent]
    public class MapMenu : MonoBehaviour
    {
        public static MapMenu Instance { get; private set; }

        const string WelcomeShownKey = "mapWelcomeShown";

        [Tooltip("Optional override for the generated profile icon.")]
        public Sprite profileSprite;

        PopupController _popup;
        RectTransform _safeArea;   // keeps the profile button clear of the notch / status bar
        Rect _appliedSafeArea;

        public static MapMenu Create(Sprite profileOverride = null)
        {
            // Inactive first so Awake (which builds the UI) runs after the override is set.
            var go = new GameObject("MapMenu");
            go.SetActive(false);
            var menu = go.AddComponent<MapMenu>();
            if (profileOverride != null) menu.profileSprite = profileOverride;
            go.SetActive(true);
            return menu;
        }

        void Awake()
        {
            Instance = this;
            EnsureEventSystem();
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            // First arrival on the map gets a one-time welcome. The flag is cleared by
            // the editor's PlayerPrefs reset, so a fresh player sees it again.
            if (PlayerPrefs.GetInt(WelcomeShownKey, 0) != 0) return;
            PlayerPrefs.SetInt(WelcomeShownKey, 1);
            PlayerPrefs.Save();
            ShowWelcome();
        }

        void Build()
        {
            var canvasGo = new GameObject("MapMenuCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200; // above the energy HUD; the popup is modal
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            // Screen chrome (the profile button) sits inside the safe area so it drops
            // below a notch / status bar. The popup stays full-screen — its dim should
            // cover the whole display, including the notch region.
            _safeArea = UiFactory.Rect("SafeArea", canvasGo.transform);
            ApplySafeArea();

            BuildProfileButton(_safeArea);
            BuildPopup(canvasGo.transform);
        }

        /// <summary>Shrink the chrome container to the device safe area (re-applied on rotation).</summary>
        void ApplySafeArea()
        {
            if (_safeArea == null) return;
            _appliedSafeArea = Screen.safeArea;
            float w = Screen.width, h = Screen.height;
            if (w <= 0f || h <= 0f) return;

            _safeArea.anchorMin = new Vector2(_appliedSafeArea.xMin / w, _appliedSafeArea.yMin / h);
            _safeArea.anchorMax = new Vector2(_appliedSafeArea.xMax / w, _appliedSafeArea.yMax / h);
            _safeArea.offsetMin = Vector2.zero;
            _safeArea.offsetMax = Vector2.zero;
        }

        void Update()
        {
            // Follow late safe-area changes (e.g. device rotation).
            if (_appliedSafeArea != Screen.safeArea) ApplySafeArea();
        }

        void BuildProfileButton(Transform parent)
        {
            var bg = UiFactory.Panel("ProfileButton", parent, BinancePalette.SurfaceRaised);
            UiFactory.Anchor(bg.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, -28f), new Vector2(84f, 84f));
            var button = bg.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(ShowProfile);

            var icon = UiFactory.Icon("ProfileIcon", bg.transform,
                profileSprite != null ? profileSprite : ProceduralIcons.Profile(), BinancePalette.Yellow);
            UiFactory.Anchor(icon.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(52f, 52f));
        }

        void BuildPopup(Transform parent)
        {
            var root = UiFactory.Rect("ComingSoonPopup", parent);
            UiFactory.Stretch(root);

            // Full-screen dim that also blocks input behind the modal.
            var dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.7f);
            dim.raycastTarget = true;

            var card = UiFactory.Panel("Card", root, BinancePalette.Surface);
            UiFactory.Anchor(card.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(760f, 440f));

            var title = UiFactory.Text("PopupTitle", card.transform, "Coming soon", 46f,
                BinancePalette.TextPrimary, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            UiFactory.Anchor(title.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -44f), new Vector2(-60f, 70f));

            var msg = UiFactory.Text("PopupMessage", card.transform, "", 30f,
                BinancePalette.TextSecondary, TextAlignmentOptions.Center);
            UiFactory.Anchor(msg.rectTransform,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            msg.rectTransform.offsetMin = new Vector2(50f, 130f);
            msg.rectTransform.offsetMax = new Vector2(-50f, -130f);

            var closeBg = UiFactory.Panel("CloseButton", card.transform, BinancePalette.Yellow);
            UiFactory.Anchor(closeBg.rectTransform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 40f), new Vector2(280f, 84f));
            var closeBtn = closeBg.gameObject.AddComponent<Button>();
            closeBtn.targetGraphic = closeBg;
            var closeLabel = UiFactory.Text("Label", closeBg.transform, "Got it", 32f,
                BinancePalette.OnYellow, TextAlignmentOptions.Center);
            closeLabel.fontStyle = FontStyles.Bold;
            UiFactory.Stretch(closeLabel.rectTransform);

            // Reuse the login screen's PopupController so the message + behaviour match.
            _popup = root.gameObject.AddComponent<PopupController>();
            _popup.root = root.gameObject;
            _popup.titleLabel = title;
            _popup.messageLabel = msg;
            closeBtn.onClick.AddListener(_popup.Close);

            root.gameObject.SetActive(false);
        }

        /// <summary>Open the player's profile sheet (inventory, level, persona).</summary>
        public void ShowProfile()
        {
            if (ProfileView.Instance != null) ProfileView.Instance.Open();
            else ShowComingSoon(); // profile view not present — fall back to the shared popup
        }

        /// <summary>Raise the shared "Coming soon" popup.</summary>
        public void ShowComingSoon()
        {
            if (_popup != null) _popup.ShowComingSoon();
        }

        /// <summary>Raise the shared popup with a custom title + message (e.g. claim results).</summary>
        public void ShowMessage(string title, string message)
        {
            if (_popup != null) _popup.Show(title, message);
        }

        /// <summary>Raise the one-time welcome popup (shown on first map-scene arrival).</summary>
        public void ShowWelcome()
        {
            if (_popup == null) return;
            _popup.Show("Welcome to Tales Tensor",
                "Keep an eye out for points of interest dotted around the map. Get close " +
                "and tap one to join an experience and unlock real-world rewards at your " +
                "new favourite places.");
        }

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

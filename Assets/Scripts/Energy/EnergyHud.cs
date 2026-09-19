using System;
using BinanceTheme;
using TalesTensor.Map;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Self-building energy readout pinned to the top of the screen: a lightning icon
/// and the player's current energy, backed by <see cref="EnergyController"/>.
/// Hovering (mouse) or pressing-and-holding (touch) the icon shows a tooltip with
/// the cap, the regen rate, and a live "next energy in M:SS" countdown.
///
/// Created in code by <see cref="TalesTensor.Map.MapController"/> on the map scene,
/// so it needs no prefab. Assign <see cref="energySprite"/> to override the
/// generated lightning glyph with real art.
/// </summary>
[DisallowMultipleComponent]
public class EnergyHud : MonoBehaviour
{
    [Tooltip("Optional override for the generated lightning icon.")]
    public Sprite energySprite;
    [Tooltip("Tint for the energy icon.")]
    public Color iconColor = new Color32(0xFC, 0xD5, 0x35, 0xFF); // BinancePalette.Yellow

    EnergyController _energy;
    TextMeshProUGUI _valueLabel;
    GameObject _tooltip;
    TextMeshProUGUI _tooltipLabel;
    GameObject _blocker;          // full-screen catcher that closes a tapped-open tooltip
    RectTransform _safeArea;      // keeps the HUD clear of the notch / status bar
    Rect _appliedSafeArea;
    bool _pinned;                 // tooltip held open by a tap (vs. transient mouse hover)

    public static EnergyHud Create(Sprite iconOverride = null)
    {
        // Start inactive so Awake (which builds the UI) doesn't run until the
        // optional sprite override is in place.
        var go = new GameObject("EnergyHud");
        go.SetActive(false);
        var hud = go.AddComponent<EnergyHud>();
        if (iconOverride != null) hud.energySprite = iconOverride;
        go.SetActive(true);
        return hud;
    }

    void Awake()
    {
        EnsureEventSystem();
        Build();
        _energy = EnergyController.Instance;
        _energy.OnChanged += Refresh;
    }

    void OnDestroy()
    {
        if (_energy != null) _energy.OnChanged -= Refresh;
    }

    void Build()
    {
        // Own overlay canvas, drawn above the map. High sort order keeps it on top.
        var canvasGo = new GameObject("EnergyHudCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        // Invisible full-screen catcher (behind the HUD) so a tap anywhere off the
        // pill closes an open tooltip. Created first → drawn/raycast below the pill.
        BuildBlocker(canvasGo.transform);

        // Everything visible lives inside a container shrunk to the screen's safe area,
        // so the pill drops below a notch / status bar instead of hiding under it.
        _safeArea = UiFactory.Rect("SafeArea", canvasGo.transform);
        ApplySafeArea();

        // Top-centre pill: [icon] [value]
        var pill = UiFactory.Panel("EnergyPill", _safeArea, Alpha(BinancePalette.Surface, 0.9f));
        UiFactory.Anchor(pill.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -24f), new Vector2(190f, 76f));

        var icon = UiFactory.Icon("EnergyIcon", pill.transform,
            energySprite != null ? energySprite : ProceduralIcons.Lightning(), iconColor);
        UiFactory.Anchor(icon.rectTransform,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(20f, 0f), new Vector2(46f, 46f));

        _valueLabel = UiFactory.Text("EnergyValue", pill.transform, "0", 40f,
            BinancePalette.TextPrimary, TextAlignmentOptions.Left);
        UiFactory.Anchor(_valueLabel.rectTransform,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f),
            new Vector2(78f, 0f), Vector2.zero);
        _valueLabel.rectTransform.offsetMin = new Vector2(78f, 0f);
        _valueLabel.rectTransform.offsetMax = new Vector2(-12f, 0f);

        BuildTooltip(_safeArea);

        // Pointer probe over the whole pill. Tap/click toggles (so it works on touch,
        // where there's no hover); mouse hover still previews it transiently.
        var probe = pill.gameObject.AddComponent<PointerProbe>();
        probe.onClick = ToggleTooltip;
        probe.onHoverShow = HoverShow;
        probe.onHoverHide = HoverHide;
    }

    void BuildBlocker(Transform parent)
    {
        var img = UiFactory.Panel("TooltipBlocker", parent, new Color(0f, 0f, 0f, 0f));
        UiFactory.Stretch(img.rectTransform);
        img.raycastTarget = true;
        var button = img.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(CloseTooltip);
        _blocker = img.gameObject;
        _blocker.SetActive(false);
    }

    /// <summary>Shrink the HUD container to the device safe area (re-applied on rotation).</summary>
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

    void BuildTooltip(Transform parent)
    {
        var panel = UiFactory.Panel("EnergyTooltip", parent, Alpha(BinancePalette.SurfaceRaised, 0.96f));
        UiFactory.Anchor(panel.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -108f), new Vector2(440f, 150f));
        panel.raycastTarget = false;

        _tooltipLabel = UiFactory.Text("TooltipText", panel.transform, "", 30f,
            BinancePalette.TextPrimary, TextAlignmentOptions.TopLeft);
        UiFactory.Stretch(_tooltipLabel.rectTransform, 20f);

        _tooltip = panel.gameObject;
        _tooltip.SetActive(false);
    }

    /// <summary>Tap/click handler: open the tooltip and pin it, or close it if already open.</summary>
    void ToggleTooltip()
    {
        if (_pinned) CloseTooltip();
        else OpenTooltip();
    }

    void OpenTooltip()
    {
        _pinned = true;
        if (_tooltip != null) { _tooltip.SetActive(true); UpdateTooltipText(); }
        if (_blocker != null) _blocker.SetActive(true);
    }

    void CloseTooltip()
    {
        _pinned = false;
        if (_tooltip != null) _tooltip.SetActive(false);
        if (_blocker != null) _blocker.SetActive(false);
    }

    // Mouse hover previews the tooltip without pinning it (desktop convenience).
    void HoverShow()
    {
        if (_pinned || _tooltip == null) return;
        _tooltip.SetActive(true);
        UpdateTooltipText();
    }

    void HoverHide()
    {
        if (_pinned || _tooltip == null) return;
        _tooltip.SetActive(false);
    }

    void Start() => Refresh();

    void Update()
    {
        // Follow late safe-area changes (e.g. device rotation).
        if (_appliedSafeArea != Screen.safeArea) ApplySafeArea();

        // Keep the countdown ticking while the tooltip is open.
        if (_tooltip != null && _tooltip.activeSelf) UpdateTooltipText();
    }

    void Refresh()
    {
        if (_valueLabel != null && _energy != null)
            _valueLabel.text = _energy.Current.ToString();
    }

    void UpdateTooltipText()
    {
        if (_tooltipLabel == null || _energy == null) return;
        string line3 = _energy.IsFull
            ? "Energy full"
            : $"Next energy in {FormatTime(_energy.SecondsUntilNext)}";
        _tooltipLabel.text =
            $"Maximum energy: {_energy.Max}\n" +
            "You gain 1 energy per minute\n" +
            line3;
    }

    static string FormatTime(int seconds)
    {
        if (seconds < 0) seconds = 0;
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (FindAnyObjectByType<EventSystem>() != null) return;

        var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        // New Input System is active; the legacy StandaloneInputModule would throw.
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    /// <summary>
    /// Routes a tap/click (works on touch, where there's no hover) to a toggle callback,
    /// and mouse hover enter/exit to transient show/hide callbacks.
    /// </summary>
    class PointerProbe : MonoBehaviour,
        IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Action onClick;
        public Action onHoverShow;
        public Action onHoverHide;
        public void OnPointerClick(PointerEventData e) => onClick?.Invoke();
        public void OnPointerEnter(PointerEventData e) => onHoverShow?.Invoke();
        public void OnPointerExit(PointerEventData e) => onHoverHide?.Invoke();
    }
}

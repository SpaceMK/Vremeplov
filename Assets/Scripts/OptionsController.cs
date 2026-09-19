using BinanceTheme;
using TalesTensor.Map;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The options/settings modal: a notifications toggle, a frame-rate selector, plus
/// About, Help &amp; Support, and Privacy &amp; Security entries. Reuses the shared
/// <see cref="PopupController"/> for the About (version) and "Coming soon" messages.
/// </summary>
public class OptionsController : MonoBehaviour
{
    [Tooltip("The options overlay to show/hide. Should start inactive in the scene.")]
    public GameObject root;
    public Toggle notificationsToggle;

    [Tooltip("Shared message popup, used for About and the Coming-soon notices.")]
    public PopupController messagePopup;

    static readonly string[] FrameRateLabels = { "30", "60", "Max" };
    Image[] _fpsSegments;
    TextMeshProUGUI[] _fpsLabels;

    public bool NotificationsEnabled => GameSettings.NotificationsEnabled;

    void OnEnable()
    {
        // Reflect the stored preference each time the panel opens, without firing the callback.
        if (notificationsToggle != null)
            notificationsToggle.SetIsOnWithoutNotify(NotificationsEnabled);

        BuildFrameRateRow();   // once; no-op on later opens
        RefreshFrameRate();    // highlight the active segment
    }

    public void Open() { if (root != null) root.SetActive(true); }
    public void Close() { if (root != null) root.SetActive(false); }

    /// <summary>Hooked to the toggle's OnValueChanged; persists the current state.</summary>
    public void SetNotifications()
    {
        bool enabled = notificationsToggle != null && notificationsToggle.isOn;
        // Through GameSettings rather than PlayerPrefs directly, so the change is
        // backed up to the account instead of staying on this handset.
        GameSettings.NotificationsEnabled = enabled;
        Debug.Log($"[Options] Notifications {(enabled ? "on" : "off")}.");
    }

    public void OnAbout()
    {
        if (messagePopup != null)
            messagePopup.Show("About", $"Tales Tensor\nVersion {Application.version}");
    }

    public void OnHelp()
    {
        if (messagePopup != null) messagePopup.ShowComingSoon();
    }

    public void OnPrivacy()
    {
        if (messagePopup != null) messagePopup.ShowComingSoon();
    }

    // --- Frame-rate selector (built in code, dropped into the gap below Privacy) ---

    /// <summary>Builds the "Frame rate" row with its 30 / 60 / Max segmented toggle, once.</summary>
    void BuildFrameRateRow()
    {
        if (_fpsSegments != null || root == null) return;

        // The popup's first child is the dialog Card the other rows live in.
        Transform card = root.transform.childCount > 0 ? root.transform.GetChild(0) : root.transform;

        var row = UiFactory.Rect("FrameRateRow", card);
        // Top-anchored full-width row, mirroring the authored rows, sitting in the
        // free space between the Privacy button and the Close button.
        UiFactory.Anchor(row, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -600f), new Vector2(-80f, 96f));

        var label = UiFactory.Text("Label", row, "Frame rate", 34f,
            BinancePalette.TextPrimary, TextAlignmentOptions.Left);
        UiFactory.Anchor(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(32f, 0f), new Vector2(300f, 50f));

        // Segmented bar on the right: three equal buttons via a horizontal layout.
        var bar = UiFactory.Rect("Segments", row);
        UiFactory.Anchor(bar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-32f, 0f), new Vector2(366f, 64f));
        var hlg = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 6f;

        _fpsSegments = new Image[FrameRateLabels.Length];
        _fpsLabels = new TextMeshProUGUI[FrameRateLabels.Length];
        for (int i = 0; i < FrameRateLabels.Length; i++)
            BuildSegment(bar, i, FrameRateLabels[i]);
    }

    void BuildSegment(Transform parent, int index, string text)
    {
        var img = UiFactory.Panel($"Seg{index}", parent, BinancePalette.Line);
        img.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var button = img.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(() => OnFrameRateSelected(index));

        var lbl = UiFactory.Text("Label", img.transform, text, 30f,
            BinancePalette.TextSecondary, TextAlignmentOptions.Center);
        UiFactory.Stretch(lbl.rectTransform);

        _fpsSegments[index] = img;
        _fpsLabels[index] = lbl;
    }

    void OnFrameRateSelected(int index)
    {
        FrameRateSetting.Mode = (FrameRateMode)index; // persists + applies immediately
        RefreshFrameRate();
        Debug.Log($"[Options] Frame rate set to {FrameRateLabels[index]}.");
    }

    /// <summary>Tint the active segment yellow and the rest muted.</summary>
    void RefreshFrameRate()
    {
        if (_fpsSegments == null) return;
        int selected = (int)FrameRateSetting.Mode;
        for (int i = 0; i < _fpsSegments.Length; i++)
        {
            bool on = i == selected;
            _fpsSegments[i].color = on ? BinancePalette.Yellow : BinancePalette.Line;
            _fpsLabels[i].color = on ? BinancePalette.OnYellow : BinancePalette.TextSecondary;
        }
    }
}

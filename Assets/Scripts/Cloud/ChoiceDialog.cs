using System;
using BinanceTheme;
using TalesTensor.Map;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A modal two-choice dialog, built entirely in code in the same spirit as
/// <see cref="ProfileView"/> and the map cards — no prefab, no scene wiring, so it
/// works from the login screen and from the map alike.
///
/// The scene's <see cref="PopupController"/> only does title + message + dismiss,
/// which can't express a decision. This exists for the one moment that genuinely
/// needs the player to choose: a cloud save and a local save that disagree.
/// </summary>
[DisallowMultipleComponent]
public class ChoiceDialog : MonoBehaviour
{
    Action<bool> _onChoice;
    bool _answered;

    /// <summary>
    /// Show a modal choice. <paramref name="onChoice"/> receives true for the primary
    /// option and false for the secondary, and is called exactly once.
    /// </summary>
    public static void Show(string title, string message,
        string primaryText, string secondaryText, Action<bool> onChoice)
    {
        var go = new GameObject("ChoiceDialog");
        DontDestroyOnLoad(go);
        var dialog = go.AddComponent<ChoiceDialog>();
        dialog._onChoice = onChoice;
        dialog.Build(title, message, primaryText, secondaryText);
    }

    void Build(string title, string message, string primaryText, string secondaryText)
    {
        EnsureEventSystem();

        var canvasGo = new GameObject("ChoiceCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500; // above the profile sheet and every HUD

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        var overlay = UiFactory.Rect("Overlay", canvasGo.transform);
        UiFactory.Stretch(overlay);
        var dim = overlay.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.78f);
        dim.raycastTarget = true; // modal: swallow taps on whatever is behind

        var sheet = UiFactory.Panel("Sheet", overlay, BinancePalette.Surface);
        UiFactory.Anchor(sheet.rectTransform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(900f, 700f));

        var titleText = UiFactory.Text("Title", sheet.transform, title, 52f,
            BinancePalette.TextPrimary, TextAlignmentOptions.Center);
        titleText.fontStyle = FontStyles.Bold;
        UiFactory.Anchor(titleText.rectTransform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -48f), new Vector2(-80f, 70f));

        var body = UiFactory.Text("Message", sheet.transform, message, 36f,
            BinancePalette.TextSecondary, TextAlignmentOptions.Top);
        UiFactory.Anchor(body.rectTransform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -140f), new Vector2(-100f, 320f));

        AddButton(sheet.transform, "Primary", primaryText,
            BinancePalette.Yellow, BinancePalette.OnYellow, -190f, () => Answer(true));

        AddButton(sheet.transform, "Secondary", secondaryText,
            BinancePalette.Line, BinancePalette.TextPrimary, -60f, () => Answer(false));
    }

    /// <param name="yFromBottom">Anchored Y offset from the sheet's bottom edge.</param>
    void AddButton(Transform parent, string name, string label,
        Color fill, Color textColor, float yFromBottom, Action onClick)
    {
        var panel = UiFactory.Panel(name, parent, fill);
        UiFactory.Anchor(panel.rectTransform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, -yFromBottom), new Vector2(780f, 104f));

        var text = UiFactory.Text("Label", panel.transform, label, 38f,
            textColor, TextAlignmentOptions.Center);
        text.fontStyle = FontStyles.Bold;
        UiFactory.Stretch(text.rectTransform);

        var button = panel.gameObject.AddComponent<Button>();
        button.targetGraphic = panel;
        button.onClick.AddListener(() => onClick());
    }

    void Answer(bool primary)
    {
        // Both buttons stay live for a frame after the first tap; without this a
        // double-tap could resolve the same decision twice.
        if (_answered) return;
        _answered = true;

        var callback = _onChoice;
        _onChoice = null;
        Destroy(gameObject);
        callback?.Invoke(primary);
    }

    /// <summary>The login scene has an EventSystem, but a code-built scene may not.</summary>
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

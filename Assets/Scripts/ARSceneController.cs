using BinanceTheme;
using TalesTensor.Map;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Lightweight HUD layered over the AR starter scene. Reads the quest the player
/// accepted on the map (<see cref="ArSession.QuestName"/>) and shows it as a
/// banner, plus a Back button that wipes back to the map.
///
/// Self-bootstraps when <c>ARScene</c> loads — no prefab or scene wiring, matching
/// how the rest of the app self-assembles (see <see cref="EnergyHud"/>,
/// <see cref="SceneTransition"/>).
///
/// This is intentionally a scaffold: the accepted quest is surfaced here so future
/// work can drive what actually spawns in the AR view from it. For now it just
/// displays the name to prove the map -> AR handoff end to end.
/// </summary>
[DisallowMultipleComponent]
public class ARSceneController : MonoBehaviour
{
    public const string ArSceneName = "ARScene";
    public const string MapSceneName = "MapScene";

    GameObject _confirmRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        // Registered before any scene loads, so it also catches ARScene when the
        // app boots straight into it (e.g. pressing Play with ARScene open).
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != ArSceneName) return;
        if (FindAnyObjectByType<ARSceneController>() != null) return; // already present
        new GameObject("ARSceneController").AddComponent<ARSceneController>();
    }

    void Awake()
    {
        EnsureEventSystem();
        Build();
    }

    void Build()
    {
        // Own overlay canvas above the AR toolkit's own UI.
        var canvasGo = new GameObject("ARHudCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        // The quest name is presented centre-screen by ARFloorPlacer's fading intro,
        // so the HUD itself is just the Back button (+ its leave-confirm popup).
        BuildBackButton(canvasGo.transform);
        BuildConfirmPopup(canvasGo.transform);
    }

    void BuildBackButton(Transform parent)
    {
        // Top-left pill that returns to the map.
        var pill = UiFactory.Panel("BackButton", parent, BinancePalette.SurfaceRaised);
        UiFactory.Anchor(pill.rectTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(36f, -130f), new Vector2(220f, 96f));

        var label = UiFactory.Text("BackLabel", pill.transform, "‹  Back", 40f,
            BinancePalette.TextPrimary, TextAlignmentOptions.Center);
        UiFactory.Stretch(label.rectTransform);

        var button = pill.gameObject.AddComponent<Button>();
        button.targetGraphic = pill;
        // Don't leave straight away — confirm first, since the energy spent to enter
        // this experience won't be refunded.
        button.onClick.AddListener(ShowLeaveConfirm);
    }

    void ReturnToMap()
    {
        // Done with this experience; drop the accepted quest and wipe back to the map.
        ArSession.Clear();
        SceneTransition.Load(MapSceneName);
    }

    // --- "are you sure?" leave confirmation ---

    void ShowLeaveConfirm() { if (_confirmRoot != null) _confirmRoot.SetActive(true); }
    void HideLeaveConfirm() { if (_confirmRoot != null) _confirmRoot.SetActive(false); }

    /// <summary>Modal shown when the player taps Back: Yes leaves to the map (losing the
    /// energy already spent to enter), No keeps them in the AR experience.</summary>
    void BuildConfirmPopup(Transform parent)
    {
        var root = UiFactory.Rect("LeaveConfirm", parent);
        UiFactory.Stretch(root);
        _confirmRoot = root.gameObject;

        // Full-screen dim that also blocks taps on the HUD/chat behind the modal.
        var dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        dim.raycastTarget = true;

        var card = UiFactory.Panel("Card", root, BinancePalette.Surface);
        UiFactory.Anchor(card.rectTransform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(820f, 470f));

        var title = UiFactory.Text("Title", card.transform, "Leave experience?", 46f,
            BinancePalette.TextPrimary, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        UiFactory.Anchor(title.rectTransform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -44f), new Vector2(-60f, 70f));

        var msg = UiFactory.Text("Message", card.transform,
            "Are you sure? You'll lose the energy you spent for this event.", 30f,
            BinancePalette.TextSecondary, TextAlignmentOptions.Center);
        UiFactory.Anchor(msg.rectTransform,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        msg.rectTransform.offsetMin = new Vector2(50f, 160f);
        msg.rectTransform.offsetMax = new Vector2(-50f, -130f);

        // No (stay) on the left as the safe/primary choice; Yes (leave) on the right,
        // styled destructive since it forfeits the spent energy.
        var noBtn = MakeButton("NoButton", card.transform, "No", BinancePalette.Yellow,
            BinancePalette.OnYellow, new Vector2(-190f, 40f), new Vector2(360f, 96f));
        noBtn.onClick.AddListener(HideLeaveConfirm);

        var yesBtn = MakeButton("YesButton", card.transform, "Yes", BinancePalette.Negative,
            Color.white, new Vector2(190f, 40f), new Vector2(360f, 96f));
        yesBtn.onClick.AddListener(ReturnToMap);

        root.gameObject.SetActive(false);
    }

    static Button MakeButton(string name, Transform parent, string label, Color bg, Color fg,
        Vector2 anchoredPos, Vector2 size)
    {
        var panel = UiFactory.Panel(name, parent, bg);
        UiFactory.Anchor(panel.rectTransform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            anchoredPos, size);
        var btn = panel.gameObject.AddComponent<Button>();
        btn.targetGraphic = panel;
        var text = UiFactory.Text(name + "Label", panel.transform, label, 34f, fg,
            TextAlignmentOptions.Center);
        text.fontStyle = FontStyles.Bold;
        UiFactory.Stretch(text.rectTransform);
        return btn;
    }

    /// <summary>uGUI needs an EventSystem for button clicks; the AR scene may not
    /// have one we can rely on, so guarantee it.</summary>
    static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (FindAnyObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }
}

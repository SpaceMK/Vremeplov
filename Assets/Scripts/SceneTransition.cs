using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Smooth animated scene loads. A full-screen panel wipes in from the left to
/// cover the screen, the target scene loads asynchronously behind it, then the
/// panel wipes out to the right to reveal it.
///
/// Self-contained: builds its own top-most overlay canvas and survives the load
/// via <see cref="Object.DontDestroyOnLoad"/>. Just call
/// <see cref="Load(string)"/> from anywhere — no per-scene setup required.
/// </summary>
public class SceneTransition : MonoBehaviour
{
    static SceneTransition _instance;

    [Tooltip("Wipe colour. Defaults to the Binance background.")]
    public Color panelColor = new Color(0.043137f, 0.054902f, 0.066667f, 1f);
    [Tooltip("Seconds for each half of the wipe (cover / reveal).")]
    public float duration = 0.45f;

    [Tooltip("Pause (seconds) while fully covered after the new scene loads, letting its " +
             "first-frame hitches settle so the reveal stays smooth.")]
    public float settleDelay = 0.5f;

    [Tooltip("Loading messages; one is shown at random each transition.")]
    public string[] phrases =
    {
        "Warming Up",
        "Time Portal Starting Up",
        "Calibrating Temporal Flux",
        "Spooling the Chrono Core",
        "Aligning Timelines",
        "Charging the Flux Capacitor",
        "Stabilizing the Wormhole",
        "Syncing Spacetime Coordinates",
        "Reticulating Time Splines",
        "Engaging the Tachyon Drive",
        "Opening the Rift",
        "Winding the Clockwork",
        "Bending Spacetime",
    };

    Canvas _canvas;
    RectTransform _panel;
    TMP_Text _label;
    bool _busy;
    int _lastPhraseIndex = -1; // so a transition never shows the same phrase twice in a row

    /// <summary>Animate a wipe and load <paramref name="sceneName"/> behind it.</summary>
    public static void Load(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogWarning($"[SceneTransition] Scene '{sceneName}' is not in Build Settings.");
            return;
        }

        var t = EnsureInstance();
        if (t._busy) return;
        t.StartCoroutine(t.Run(sceneName));
    }

    static SceneTransition EnsureInstance()
    {
        if (_instance != null) return _instance;
        // Create with Canvas + GraphicRaycaster already attached so Awake/Build can
        // fetch them — adding a Canvas from inside Awake-during-AddComponent fails.
        var go = new GameObject("SceneTransition", typeof(Canvas), typeof(GraphicRaycaster), typeof(SceneTransition));
        if (_instance == null) _instance = go.GetComponent<SceneTransition>();
        return _instance;
    }

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        Build();
    }

    void Build()
    {
        if (_panel != null) return;

        _canvas = GetComponent<Canvas>();
        if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = short.MaxValue; // above all in-scene UI
        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>(); // swallow input mid-wipe

        var panelGO = new GameObject("Wipe", typeof(RectTransform), typeof(Image));
        panelGO.transform.SetParent(transform, false);
        panelGO.GetComponent<Image>().color = panelColor;

        _panel = panelGO.GetComponent<RectTransform>();
        _panel.anchorMin = Vector2.zero;
        _panel.anchorMax = Vector2.one;
        _panel.sizeDelta = Vector2.zero;
        _panel.anchoredPosition = Vector2.zero;

        // Centered loading message (uses TMP's default font asset).
        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGO.transform.SetParent(_panel, false);
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0f, 0.5f);
        labelRT.anchorMax = new Vector2(1f, 0.5f);
        labelRT.pivot = new Vector2(0.5f, 0.5f);
        labelRT.sizeDelta = new Vector2(-160f, 160f);
        labelRT.anchoredPosition = Vector2.zero;

        _label = labelGO.GetComponent<TextMeshProUGUI>();
        _label.alignment = TextAlignmentOptions.Center;
        _label.fontSize = 44;
        _label.color = new Color(0.917647f, 0.92549f, 0.937255f, 1f); // Binance text-primary
        _label.raycastTarget = false;

        panelGO.SetActive(false); // idle: off, so it never blocks input
    }

    IEnumerator Run(string sceneName)
    {
        _busy = true;

        // Re-assert top-most overlay ordering every run. The singleton is reused
        // across scene loads; a scene that builds its own ScreenSpaceOverlay canvas
        // at runtime (e.g. QuestionScene) can otherwise tie/win the sort and leave
        // the wipe rendering *behind* that UI. Forcing it here keeps it on top.
        if (_canvas != null)
        {
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = short.MaxValue;
        }

        if (_label != null && phrases != null && phrases.Length > 0)
            _label.text = NextPhrase() + "…";
        _panel.gameObject.SetActive(true);

        // Cover: slide in from the left.
        yield return Move(-1f, 0f);

        // Load behind the cover, then activate.
        var op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;
        while (op.progress < 0.9f) yield return null;
        op.allowSceneActivation = true;
        while (!op.isDone) yield return null;

        // Hold the cover briefly so the new scene's first-frame hitches pass before revealing.
        if (settleDelay > 0f) yield return new WaitForSecondsRealtime(settleDelay);

        // Reveal: slide out to the right.
        yield return Move(0f, 1f);

        _panel.gameObject.SetActive(false);
        _busy = false;
    }

    /// <summary>A random phrase, never repeating the one shown on the previous transition.</summary>
    string NextPhrase()
    {
        int i = Random.Range(0, phrases.Length);
        // With 2+ phrases, reroll once off the last index to avoid an immediate repeat.
        if (phrases.Length > 1 && i == _lastPhraseIndex)
            i = (i + 1 + Random.Range(0, phrases.Length - 1)) % phrases.Length;
        _lastPhraseIndex = i;
        return phrases[i];
    }

    /// <summary>Slide the full-screen panel between screen-width multiples, eased.</summary>
    IEnumerator Move(float fromFraction, float toFraction)
    {
        float width = Screen.width;
        float t = 0f;
        float dur = Mathf.Max(0.0001f, duration);

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            _panel.anchoredPosition = new Vector2(Mathf.Lerp(fromFraction, toFraction, e) * width, 0f);
            yield return null;
        }
        _panel.anchoredPosition = new Vector2(toFraction * width, 0f);
    }
}

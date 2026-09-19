using System.Collections;
using System.Collections.Generic;
using BinanceTheme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives <c>QuestionScene</c>: a short, Duolingo-style onboarding questionnaire
/// that profiles what the player likes to do on holiday (food, history, nightlife…)
/// and stores the result locally via <see cref="QuestionnaireStore"/>.
///
/// The whole UI is built in code (like <see cref="SceneTransition"/>) so the scene
/// itself stays a bare Camera + EventSystem + this component — no per-element wiring.
/// Layout borrows Duolingo's shape: a top progress bar, one big centred prompt, a
/// stack of large tappable answer cards with a clear selected state, and a single
/// CONTINUE button along the bottom. Colours come from <see cref="BinancePalette"/>.
///
/// Answers are never right or wrong; each contributes weight to one or more profile
/// categories, summed across the run. On finish we save the profile and wipe to the
/// map. If the questionnaire is already complete we skip straight to the map.
/// </summary>
public class QuestionnaireController : MonoBehaviour
{
    [Header("Navigation")]
    [Tooltip("Scene loaded once the questionnaire is finished (or already complete). Must be in Build Settings.")]
    public string mapSceneName = "MapScene";

    [Header("Content")]
    [Tooltip("Optional override. Leave empty to use the built-in 10-question holiday-profile set.")]
    public List<ProfileQuestion> questions = new List<ProfileQuestion>();

    [Header("Copy")]
    public string headerText = "Let's tailor your adventures";
    public string continueText = "CONTINUE";
    public string finishText = "LET'S GO";

    [Header("Transition")]
    [Tooltip("Seconds for each half of the question slide/fade (out, then in).")]
    public float transitionDuration = 0.22f;
    [Tooltip("Horizontal slide distance (px at reference resolution) for the question swap.")]
    public float slideDistance = 80f;

    // --- Theme shorthand -----------------------------------------------------
    static Color Bg          => BinancePalette.Background;
    static Color Surface     => BinancePalette.Surface;
    static Color Card        => BinancePalette.SurfaceRaised;
    static Color Line        => BinancePalette.Line;
    static Color Accent      => BinancePalette.Yellow;
    static Color OnAccent    => BinancePalette.OnYellow;
    static Color TextPrimary => BinancePalette.TextPrimary;
    static Color TextMuted   => BinancePalette.TextSecondary;
    static Color CardSelected => UnityEngine.Color.Lerp(BinancePalette.SurfaceRaised, BinancePalette.Yellow, 0.12f);

    // --- Built UI references --------------------------------------------------
    RectTransform _content;
    CanvasGroup _contentGroup;
    TMP_Text _questionLabel;
    RectTransform _answersRoot;
    RectTransform _progressFill;
    TMP_Text _counterLabel;
    Button _continueButton;
    Image _continueImage;
    TMP_Text _continueLabel;

    // --- State ---------------------------------------------------------------
    readonly List<CardView> _cards = new List<CardView>();
    Button _backButton;
    UserProfile _profile;
    int[] _answers;       // chosen answer index per question (-1 = unanswered)
    int _index;
    int _selected = -1;
    bool _busy;

    /// <summary>One answer card's pieces, so we can repaint its selected state.</summary>
    class CardView
    {
        public Image border;
        public Image fill;
        public TMP_Text label;
    }

    // Unity's built-in rounded UI sprite — gives Duolingo-ish rounded corners with
    // no imported assets (9-sliced, so it stays crisp at any size).
    static Sprite _rounded;
    static Sprite Rounded()
    {
        if (_rounded == null) _rounded = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        return _rounded;
    }

    void Start()
    {
        // Returning players who already answered never see this — go straight in.
        if (QuestionnaireStore.IsComplete())
        {
            SceneTransition.Load(mapSceneName);
            return;
        }

        if (questions == null || questions.Count == 0)
            questions = QuestionBank.Default();

        _answers = new int[questions.Count];
        for (int i = 0; i < _answers.Length; i++) _answers[i] = -1; // nothing chosen yet

        BuildUI();
        ShowQuestion(0, animate: false);
    }

    // -------------------------------------------------------------------------
    // UI construction
    // -------------------------------------------------------------------------

    void BuildUI()
    {
        // Canvas ---------------------------------------------------------------
        var canvasGO = new GameObject("QuestionCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        canvasGO.layer = LayerMask.NameToLayer("UI");

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0; // stay below the SceneTransition wipe overlay (short.MaxValue)

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 2340f);
        scaler.matchWidthOrHeight = 0.5f;

        var root = (RectTransform)canvasGO.transform;

        // Full-screen background ----------------------------------------------
        var bg = MakeImage("Background", root, Bg);
        Stretch(bg.rectTransform);

        // Side margins for everything else.
        const float sideMargin = 64f;

        // Progress bar (top) ---------------------------------------------------
        var bar = MakeImage("ProgressTrack", root, Line);
        bar.sprite = Rounded(); bar.type = Image.Type.Sliced;
        var barRT = bar.rectTransform;
        barRT.anchorMin = new Vector2(0f, 1f);
        barRT.anchorMax = new Vector2(1f, 1f);
        barRT.pivot = new Vector2(0.5f, 1f);
        barRT.offsetMin = new Vector2(160f, -260f);        // left margin leaves room for the back button
        barRT.offsetMax = new Vector2(-sideMargin, -224f); // 36px tall, 224px down from top (clears the notch)

        var fill = MakeImage("ProgressFill", barRT, Accent);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced;
        _progressFill = fill.rectTransform;
        _progressFill.anchorMin = new Vector2(0f, 0f);
        _progressFill.anchorMax = new Vector2(0f, 1f); // width driven by anchorMax.x in UpdateProgress
        _progressFill.pivot = new Vector2(0f, 0.5f);
        _progressFill.offsetMin = Vector2.zero;
        _progressFill.offsetMax = Vector2.zero;

        // Counter ("3 / 10") ---------------------------------------------------
        _counterLabel = MakeText("Counter", root, "", 30f, TextMuted, TextAlignmentOptions.Right, false);
        var counterRT = _counterLabel.rectTransform;
        counterRT.anchorMin = new Vector2(0f, 1f);
        counterRT.anchorMax = new Vector2(1f, 1f);
        counterRT.pivot = new Vector2(0.5f, 1f);
        counterRT.offsetMin = new Vector2(sideMargin, -218f);
        counterRT.offsetMax = new Vector2(-sideMargin, -170f);

        // Back button (top-left, beside the progress bar; hidden on the first question) ---
        var backGO = new GameObject("Back", typeof(RectTransform), typeof(Image), typeof(Button));
        backGO.transform.SetParent(root, false);
        var backImg = backGO.GetComponent<Image>();
        backImg.sprite = Rounded(); backImg.type = Image.Type.Sliced;
        backImg.color = Surface;
        var backRT = (RectTransform)backGO.transform;
        backRT.anchorMin = new Vector2(0f, 1f);
        backRT.anchorMax = new Vector2(0f, 1f);
        backRT.pivot = new Vector2(0f, 1f);
        backRT.sizeDelta = new Vector2(96f, 64f);
        backRT.anchoredPosition = new Vector2(48f, -212f); // vertically centred on the progress bar band

        _backButton = backGO.GetComponent<Button>();
        _backButton.targetGraphic = backImg;
        _backButton.transition = Selectable.Transition.ColorTint;
        var bcb = _backButton.colors;
        bcb.normalColor = Color.white;
        bcb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        bcb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        bcb.selectedColor = Color.white;
        bcb.fadeDuration = 0.08f;
        _backButton.colors = bcb;
        _backButton.onClick.AddListener(OnBack);

        var backLabel = MakeText("Label", backRT, "<", 50f, TextPrimary, TextAlignmentOptions.Center, false);
        backLabel.fontStyle = FontStyles.Bold;
        Stretch(backLabel.rectTransform);

        // Header tagline -------------------------------------------------------
        var header = MakeText("Header", root, headerText, 32f, TextMuted, TextAlignmentOptions.Center, false);
        var headerRT = header.rectTransform;
        headerRT.anchorMin = new Vector2(0f, 1f);
        headerRT.anchorMax = new Vector2(1f, 1f);
        headerRT.pivot = new Vector2(0.5f, 1f);
        headerRT.offsetMin = new Vector2(sideMargin, -360f);
        headerRT.offsetMax = new Vector2(-sideMargin, -300f);

        // Animated content (question + answers) -------------------------------
        var contentGO = new GameObject("Content", typeof(RectTransform), typeof(CanvasGroup));
        contentGO.transform.SetParent(root, false);
        _content = (RectTransform)contentGO.transform;
        _content.anchorMin = new Vector2(0f, 0f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.offsetMin = new Vector2(sideMargin, 280f);   // leave room for the Continue button
        _content.offsetMax = new Vector2(-sideMargin, -400f); // below the header
        _contentGroup = contentGO.GetComponent<CanvasGroup>();

        // Question prompt.
        _questionLabel = MakeText("Question", _content, "", 52f, TextPrimary, TextAlignmentOptions.Center, true);
        _questionLabel.fontStyle = FontStyles.Bold;
        var qRT = _questionLabel.rectTransform;
        qRT.anchorMin = new Vector2(0f, 1f);
        qRT.anchorMax = new Vector2(1f, 1f);
        qRT.pivot = new Vector2(0.5f, 1f);
        qRT.offsetMin = new Vector2(0f, -300f);
        qRT.offsetMax = new Vector2(0f, 0f);

        // Answers stack (vertical layout, grows downward from below the prompt).
        var answersGO = new GameObject("Answers", typeof(RectTransform), typeof(VerticalLayoutGroup));
        answersGO.transform.SetParent(_content, false);
        _answersRoot = (RectTransform)answersGO.transform;
        _answersRoot.anchorMin = new Vector2(0f, 0f);
        _answersRoot.anchorMax = new Vector2(1f, 1f);
        _answersRoot.offsetMin = new Vector2(0f, 0f);
        _answersRoot.offsetMax = new Vector2(0f, -340f); // start the stack under the prompt
        var vlg = answersGO.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 28f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        // Continue button (bottom) --------------------------------------------
        var contGO = new GameObject("Continue", typeof(RectTransform), typeof(Image), typeof(Button));
        contGO.transform.SetParent(root, false);
        _continueImage = contGO.GetComponent<Image>();
        _continueImage.sprite = Rounded(); _continueImage.type = Image.Type.Sliced;
        _continueImage.color = Line;
        var contRT = (RectTransform)contGO.transform;
        contRT.anchorMin = new Vector2(0f, 0f);
        contRT.anchorMax = new Vector2(1f, 0f);
        contRT.pivot = new Vector2(0.5f, 0f);
        contRT.offsetMin = new Vector2(sideMargin, 96f);
        contRT.offsetMax = new Vector2(-sideMargin, 96f + 132f); // 132px tall

        _continueButton = contGO.GetComponent<Button>();
        _continueButton.targetGraphic = _continueImage;
        _continueButton.transition = Selectable.Transition.ColorTint;
        var cb = _continueButton.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(0.92f, 0.92f, 0.92f);
        cb.pressedColor = new Color(0.82f, 0.82f, 0.82f);
        cb.selectedColor = new Color(0.92f, 0.92f, 0.92f);
        cb.disabledColor = new Color(1f, 1f, 1f, 1f); // we drive the disabled look via colour, not alpha
        cb.fadeDuration = 0.1f;
        _continueButton.colors = cb;
        _continueButton.onClick.AddListener(OnContinue);

        _continueLabel = MakeText("Label", contRT, continueText, 38f, TextMuted, TextAlignmentOptions.Center, false);
        _continueLabel.fontStyle = FontStyles.Bold;
        Stretch(_continueLabel.rectTransform);
    }

    // -------------------------------------------------------------------------
    // Flow
    // -------------------------------------------------------------------------

    void ShowQuestion(int index, bool animate, float inDir = 1f)
    {
        _index = index;
        BuildAnswers(questions[index]);
        _questionLabel.text = questions[index].text;

        // Restore any earlier choice so revisiting a question shows it selected.
        _selected = _answers[index];
        for (int i = 0; i < _cards.Count; i++) PaintCard(_cards[i], i == _selected);

        UpdateCounter();
        UpdateProgress(animate);
        UpdateBackButton();
        SetContinueEnabled(_selected >= 0);

        if (animate) StartCoroutine(SlideIn(inDir));
        else { _contentGroup.alpha = 1f; _content.anchoredPosition = Vector2.zero; }
    }

    void BuildAnswers(ProfileQuestion q)
    {
        for (int i = _answersRoot.childCount - 1; i >= 0; i--)
            Destroy(_answersRoot.GetChild(i).gameObject);
        _cards.Clear();

        for (int i = 0; i < q.answers.Count; i++)
        {
            int answerIndex = i; // capture for the closure
            _cards.Add(MakeAnswerCard(q.answers[i].text, () => OnSelect(answerIndex)));
        }
    }

    CardView MakeAnswerCard(string text, System.Action onClick)
    {
        // Outer "border" image — recoloured to the accent when selected.
        var cardGO = new GameObject("AnswerCard",
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        cardGO.transform.SetParent(_answersRoot, false);

        var border = cardGO.GetComponent<Image>();
        border.sprite = Rounded(); border.type = Image.Type.Sliced;
        border.color = Line;

        var le = cardGO.GetComponent<LayoutElement>();
        le.minHeight = 150f;
        le.flexibleHeight = 0f;

        // Inner fill — inset a few px so the border shows; this is the press target.
        var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGO.transform.SetParent(cardGO.transform, false);
        var fill = fillGO.GetComponent<Image>();
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced;
        fill.color = Card;
        var fillRT = fill.rectTransform;
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(5f, 5f);
        fillRT.offsetMax = new Vector2(-5f, -5f);

        var label = MakeText("Label", fillRT, text, 38f, TextPrimary, TextAlignmentOptions.Center, true);
        var labelRT = label.rectTransform;
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = new Vector2(40f, 12f);
        labelRT.offsetMax = new Vector2(-40f, -12f);

        var button = cardGO.GetComponent<Button>();
        button.targetGraphic = fill;
        button.transition = Selectable.Transition.ColorTint;
        var cb = button.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.selectedColor = Color.white;
        cb.fadeDuration = 0.08f;
        button.colors = cb;
        button.onClick.AddListener(() => onClick());

        return new CardView { border = border, fill = fill, label = label };
    }

    void OnSelect(int answerIndex)
    {
        if (_busy) return;
        _selected = answerIndex;
        for (int i = 0; i < _cards.Count; i++) PaintCard(_cards[i], i == answerIndex);
        SetContinueEnabled(true);
    }

    void PaintCard(CardView card, bool selected)
    {
        card.border.color = selected ? Accent : Line;
        card.fill.color = selected ? CardSelected : Card;
        card.label.color = selected ? Accent : TextPrimary;
        card.label.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
    }

    void OnContinue()
    {
        if (_busy || _selected < 0) return;

        _answers[_index] = _selected; // remember the choice (revisitable via Back)
        bool last = _index >= questions.Count - 1;
        StartCoroutine(Advance(last));
    }

    void OnBack()
    {
        if (_busy || _index <= 0) return;
        StartCoroutine(GoBack());
    }

    IEnumerator Advance(bool finish)
    {
        _busy = true;
        SetContinueEnabled(false);
        yield return SlideOut(-1f); // current question exits to the left

        if (finish)
        {
            BuildProfile();
            QuestionnaireStore.Save(_profile);
            Debug.Log($"[Questionnaire] Complete. Travel style: {_profile.Dominant()}.");
            SceneTransition.Load(mapSceneName);
            yield break; // leave covered by the wipe
        }

        ShowQuestion(_index + 1, animate: true, inDir: 1f); // next enters from the right
        _busy = false;
    }

    IEnumerator GoBack()
    {
        _busy = true;
        SetContinueEnabled(false);
        yield return SlideOut(1f); // current question slides back out to the right
        ShowQuestion(_index - 1, animate: true, inDir: -1f); // previous re-enters from the left
        _busy = false;
    }

    /// <summary>Sum every answered question's chosen contributions into <see cref="_profile"/>.</summary>
    void BuildProfile()
    {
        _profile = new UserProfile();
        foreach (var c in ProfileCategory.All) _profile.Set(c, 0); // seed all axes at 0

        for (int q = 0; q < questions.Count; q++)
        {
            int a = _answers[q];
            if (a < 0 || a >= questions[q].answers.Count) continue;
            var contributions = questions[q].answers[a].contributions;
            if (contributions == null) continue;
            foreach (var cw in contributions)
                if (!string.IsNullOrEmpty(cw.category)) _profile.Add(cw.category, cw.weight);
        }
    }

    // -------------------------------------------------------------------------
    // Visual state helpers
    // -------------------------------------------------------------------------

    void UpdateCounter()
    {
        if (_counterLabel != null)
            _counterLabel.text = $"{_index + 1} / {questions.Count}";
    }

    void UpdateBackButton()
    {
        // No "previous" before the first question.
        if (_backButton != null) _backButton.gameObject.SetActive(_index > 0);
    }

    void UpdateProgress(bool animate)
    {
        float target = (_index + 1) / (float)questions.Count;
        if (animate) StartCoroutine(AnimateProgress(target));
        else SetProgress(target);
    }

    void SetProgress(float fraction)
    {
        if (_progressFill == null) return;
        _progressFill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
    }

    IEnumerator AnimateProgress(float target)
    {
        float from = _progressFill != null ? _progressFill.anchorMax.x : 0f;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / 0.3f;
            SetProgress(Mathf.Lerp(from, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t))));
            yield return null;
        }
        SetProgress(target);
    }

    void SetContinueEnabled(bool enabled)
    {
        if (_continueButton != null) _continueButton.interactable = enabled;
        if (_continueImage != null) _continueImage.color = enabled ? Accent : Line;
        if (_continueLabel != null)
        {
            _continueLabel.color = enabled ? OnAccent : TextMuted;
            bool last = _index >= questions.Count - 1;
            _continueLabel.text = (enabled && last) ? finishText : continueText;
        }
    }

    // -------------------------------------------------------------------------
    // Transitions (unscaled time, eased — matches SceneTransition's feel)
    // -------------------------------------------------------------------------

    IEnumerator SlideIn(float dir)
    {
        float dur = Mathf.Max(0.0001f, transitionDuration);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            _contentGroup.alpha = e;
            _content.anchoredPosition = new Vector2(Mathf.Lerp(dir * slideDistance, 0f, e), 0f);
            yield return null;
        }
        _contentGroup.alpha = 1f;
        _content.anchoredPosition = Vector2.zero;
    }

    IEnumerator SlideOut(float dir)
    {
        float dur = Mathf.Max(0.0001f, transitionDuration);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            _contentGroup.alpha = 1f - e;
            _content.anchoredPosition = new Vector2(Mathf.Lerp(0f, dir * slideDistance, e), 0f);
            yield return null;
        }
        _contentGroup.alpha = 0f;
    }

    // -------------------------------------------------------------------------
    // Tiny UI factories
    // -------------------------------------------------------------------------

    static Image MakeImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static TMP_Text MakeText(string name, Transform parent, string text, float size,
                             Color color, TextAlignmentOptions align, bool wrap)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        return tmp;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}

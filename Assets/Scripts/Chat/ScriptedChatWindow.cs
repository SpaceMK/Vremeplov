using System.Collections;
using System.Collections.Generic;
using BinanceTheme;
using TalesTensor.Map;      // UiFactory, ProceduralIcons
using TalesTensor.Quests;   // ScriptedQuest
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TalesTensor.Chat
{
    /// <summary>
    /// Plays a fully authored, fixed-text AR quest (see <see cref="ScriptedQuest"/>) as a
    /// guided conversation: the character speaks scripted lines, the player uncovers clues by
    /// tapping quick-ask chips, and the quest resolves into item rewards and a letter hand-off.
    ///
    /// Deliberately separate from <see cref="ChatWindow"/> (the live, chatbot-driven King
    /// chat) so neither disturbs the other: there is no text input, microphone, TTS or network
    /// here — every line is local. Self-building in code and styled from the same
    /// <see cref="UiFactory"/> / <see cref="BinancePalette"/> vocabulary so it matches the rest
    /// of the app. Brought up by <see cref="ARFloorPlacer"/> once the character is placed.
    /// </summary>
    [DisallowMultipleComponent]
    public class ScriptedChatWindow : MonoBehaviour
    {
        const float PanelHeight = 900f;
        const float SidePad = 28f;
        const float BottomPad = 18f;
        const float HeaderHeight = 84f;
        const float ChipsAreaHeight = 230f; // vertical stack of quick-ask chips
        const float MaxBubbleWidth = 760f;
        const float BubblePadX = 28f, BubblePadY = 18f, BubbleSideMargin = 32f;
        const float TypingSeconds = 0.55f;  // "…" beat before each character line
        const float BeatSeconds = 0.35f;    // small pause after a line settles
        const string MapSceneName = "MapScene";

        ScriptedQuest _quest;
        ScrollRect _scroll;
        RectTransform _content;
        RectTransform _chipsArea;
        bool _destroyed;

        GameObject _typingRow;
        TextMeshProUGUI _typingText;

        public static ScriptedChatWindow Create(ScriptedQuest quest)
        {
            var go = new GameObject("ScriptedChatWindow");
            var w = go.AddComponent<ScriptedChatWindow>();
            w._quest = quest;
            w.Build();
            w.StartCoroutine(w.Play());
            return w;
        }

        void OnDestroy() => _destroyed = true;

        // --- construction ---

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 190; // above the floor prompt (150), below the HUD (200)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            var panel = UiFactory.Panel("ChatPanel", transform, Alpha(BinancePalette.Surface, 0.97f));
            UiFactory.Anchor(panel.rectTransform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, BottomPad), new Vector2(-2f * SidePad, PanelHeight));

            BuildHeader(panel.transform);
            BuildMessageList(panel.transform);
            BuildChipsArea(panel.transform);
        }

        void BuildHeader(Transform parent)
        {
            var header = UiFactory.Panel("Header", parent, BinancePalette.SurfaceRaised);
            UiFactory.Anchor(header.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, HeaderHeight));

            var label = UiFactory.Text("Title", header.transform,
                _quest != null ? _quest.characterName : "", 40f,
                BinancePalette.TextPrimary, TextAlignmentOptions.Center);
            UiFactory.Stretch(label.rectTransform, 16f);
            label.fontStyle = FontStyles.Bold;
        }

        void BuildMessageList(Transform parent)
        {
            var scrollGo = new GameObject("Messages", typeof(RectTransform), typeof(ScrollRect));
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.SetParent(parent, false);
            scrollRt.anchorMin = new Vector2(0f, 0f);
            scrollRt.anchorMax = new Vector2(1f, 1f);
            scrollRt.offsetMin = new Vector2(0f, ChipsAreaHeight);
            scrollRt.offsetMax = new Vector2(0f, -HeaderHeight);

            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30f;

            var viewport = new GameObject("Viewport",
                typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            var viewRt = (RectTransform)viewport.transform;
            viewRt.SetParent(scrollRt, false);
            UiFactory.Stretch(viewRt);
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            _scroll.viewport = viewRt;

            var contentGo = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _content = (RectTransform)contentGo.transform;
            _content.SetParent(viewRt, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;

            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(0, 0, 16, 16);
            vlg.spacing = 16f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            contentGo.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = _content;
        }

        void BuildChipsArea(Transform parent)
        {
            var area = UiFactory.Rect("Chips", parent);
            UiFactory.Anchor(area,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0f, ChipsAreaHeight));
            var vlg = area.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(24, 24, 16, 16);
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = true;
            vlg.childAlignment = TextAnchor.LowerCenter;
            _chipsArea = area;
        }

        // --- scripted flow ---

        IEnumerator Play()
        {
            if (_quest == null) yield break;

            yield return Speak(_quest.opening);
            yield return Speak(_quest.questFollowUp);

            // Clue hunt: show a chip per not-yet-found clue; tapping one plays its answer and
            // reveals its token. Repeats until all clues are uncovered.
            var remaining = new List<ScriptedQuestClue>(_quest.clues);
            while (remaining.Count > 0)
            {
                ScriptedQuestClue picked = null;
                yield return WaitForChip(remaining, c => picked = c);
                if (_destroyed) yield break;

                remaining.Remove(picked);
                AddUserBubble(picked.chip);
                yield return Speak(picked.answer);
                AddBanner($"CLUE DISCOVERED: {picked.discovered}");
                yield return new WaitForSecondsRealtime(BeatSeconds);
            }

            // All clues found: the mystery, then the real-world objective.
            yield return Speak(_quest.mystery);
            AddBanner($"OBJECTIVE: {_quest.objective}");
            yield return new WaitForSecondsRealtime(BeatSeconds);

            yield return WaitForButton(_quest.objectiveButton);
            if (_destroyed) yield break;

            // Completion → reward → letter hand-off → back to the map.
            yield return Speak(_quest.completion);

            if (!string.IsNullOrEmpty(_quest.rewardItemId))
                PlayerProgress.Instance.AddItem(_quest.rewardItemId, 1);

            ShowRewardPopup(ProceduralIcons.Flower(), "Item obtained",
                _quest.rewardTitle, _quest.rewardDesc, "Collect",
                () => StartCoroutine(HandOff()));
        }

        IEnumerator HandOff()
        {
            string next = string.IsNullOrEmpty(_quest.nextQuest) ? "another keeper in Skopje" : _quest.nextQuest;
            yield return Speak(string.Format(_quest.handOff, next));
            if (_destroyed) yield break;

            PlayerProgress.Instance.AddItem(ItemCatalog.KingsScroll, 1);
            QuestRewards.LetterQuest = next; // the quest the sealed letter is addressed to

            ShowRewardPopup(ProceduralIcons.Scroll(), "Item obtained for quest", "Sealed Letter",
                $"Carry it to the keeper of {next}.", "Collect", ReturnToMap);
        }

        /// <summary>Show the typing beat, then the character's line as a left bubble.</summary>
        IEnumerator Speak(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            ShowTyping();
            yield return new WaitForSecondsRealtime(TypingSeconds);
            HideTyping();
            if (_destroyed) yield break;
            AddCharBubble(text);
            yield return new WaitForSecondsRealtime(BeatSeconds);
        }

        /// <summary>Build a chip per clue and wait for the player to tap one.</summary>
        IEnumerator WaitForChip(List<ScriptedQuestClue> clues, System.Action<ScriptedQuestClue> onPick)
        {
            ClearChips();
            bool done = false;
            foreach (var clue in clues)
            {
                var captured = clue;
                BuildChip(captured.chip, () => { if (!done) { done = true; onPick?.Invoke(captured); } });
            }
            while (!done && !_destroyed) yield return null;
            ClearChips();
        }

        /// <summary>Show a single confirm chip and wait for it.</summary>
        IEnumerator WaitForButton(string label)
        {
            ClearChips();
            bool done = false;
            BuildChip(label, () => done = true);
            while (!done && !_destroyed) yield return null;
            ClearChips();
        }

        // --- chips ---

        void BuildChip(string text, UnityEngine.Events.UnityAction onClick)
        {
            var chip = UiFactory.Panel("Chip", _chipsArea, Alpha(BinancePalette.SurfaceRaised, 0.97f));
            var button = chip.gameObject.AddComponent<Button>();
            button.targetGraphic = chip;
            button.onClick.AddListener(onClick);
            var label = UiFactory.Text("ChipLabel", chip.transform, text, 26f,
                BinancePalette.TextPrimary, TextAlignmentOptions.Center);
            UiFactory.Stretch(label.rectTransform, 14f);
            label.enableWordWrapping = true;
        }

        void ClearChips()
        {
            if (_chipsArea == null) return;
            for (int i = _chipsArea.childCount - 1; i >= 0; i--)
                Destroy(_chipsArea.GetChild(i).gameObject);
        }

        // --- bubbles + banners ---

        void AddCharBubble(string text) => BuildBubble(text, isUser: false);
        void AddUserBubble(string text) => BuildBubble(text, isUser: true);

        TextMeshProUGUI BuildBubble(string text, bool isUser)
        {
            var row = UiFactory.Rect("Row", _content);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            int margin = Mathf.RoundToInt(BubbleSideMargin);
            rowLayout.padding = new RectOffset(margin, margin, 0, 0);
            rowLayout.childAlignment = isUser ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            var bubble = UiFactory.Panel("Bubble", row,
                isUser ? BinancePalette.Yellow : BinancePalette.SurfaceRaised);
            bubble.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            var tmp = UiFactory.Text("Text", bubble.transform, text, 32f,
                isUser ? BinancePalette.OnYellow : BinancePalette.TextPrimary,
                TextAlignmentOptions.TopLeft);
            tmp.enableWordWrapping = true;

            float maxTextWidth = MaxBubbleWidth - BubblePadX * 2f;
            Vector2 natural = tmp.GetPreferredValues(text, maxTextWidth, Mathf.Infinity);
            float textW = Mathf.Min(natural.x, maxTextWidth);
            float textH = tmp.GetPreferredValues(text, textW, Mathf.Infinity).y;

            var le = bubble.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = textW + BubblePadX * 2f;
            le.preferredHeight = le.minHeight = textH + BubblePadY * 2f;

            var tmpRt = tmp.rectTransform;
            tmpRt.anchorMin = Vector2.zero;
            tmpRt.anchorMax = Vector2.one;
            tmpRt.offsetMin = new Vector2(BubblePadX, BubblePadY);
            tmpRt.offsetMax = new Vector2(-BubblePadX, -BubblePadY);

            StartCoroutine(PopIn(bubble.rectTransform));
            ScrollToBottomNextFrame();
            return tmp;
        }

        /// <summary>A centred highlight strip — a discovered clue or the objective.</summary>
        void AddBanner(string text)
        {
            var row = UiFactory.Rect("BannerRow", _content);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            var pill = UiFactory.Panel("Banner", row, Alpha(BinancePalette.Yellow, 0.16f));
            var tmp = UiFactory.Text("Text", pill.transform, text, 27f,
                BinancePalette.Yellow, TextAlignmentOptions.Center);
            tmp.fontStyle = FontStyles.Bold;
            tmp.enableWordWrapping = true;

            float maxTextWidth = MaxBubbleWidth - BubblePadX * 2f;
            Vector2 natural = tmp.GetPreferredValues(text, maxTextWidth, Mathf.Infinity);
            float textW = Mathf.Min(natural.x, maxTextWidth);
            float textH = tmp.GetPreferredValues(text, textW, Mathf.Infinity).y;

            var le = pill.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = textW + BubblePadX * 2f;
            le.preferredHeight = le.minHeight = textH + BubblePadY * 1.4f;

            var tmpRt = tmp.rectTransform;
            tmpRt.anchorMin = Vector2.zero;
            tmpRt.anchorMax = Vector2.one;
            tmpRt.offsetMin = new Vector2(BubblePadX, BubblePadY * 0.7f);
            tmpRt.offsetMax = new Vector2(-BubblePadX, -BubblePadY * 0.7f);

            StartCoroutine(PopIn(pill.rectTransform));
            ScrollToBottomNextFrame();
        }

        void ShowTyping()
        {
            if (_typingRow != null) return;
            _typingText = BuildBubble("...", isUser: false);
            _typingRow = _typingText.transform.parent.parent.gameObject; // Text -> Bubble -> Row
        }

        void HideTyping()
        {
            if (_typingRow != null) Destroy(_typingRow);
            _typingRow = null;
            _typingText = null;
        }

        void Update()
        {
            if (_typingText == null) return;
            int dots = (int)(Time.unscaledTime * 3f) % 3 + 1;
            _typingText.text = new string('.', dots);
        }

        IEnumerator PopIn(RectTransform rt)
        {
            const float dur = 0.2f;
            float t = 0f;
            rt.localScale = Vector3.zero;
            while (t < dur && rt != null)
            {
                t += Time.unscaledDeltaTime;
                float k = EaseOutBack(Mathf.Clamp01(t / dur));
                rt.localScale = new Vector3(k, k, 1f);
                yield return null;
            }
            if (rt != null) rt.localScale = Vector3.one;
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float p = t - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }

        void ScrollToBottomNextFrame() => StartCoroutine(ScrollToBottom());

        IEnumerator ScrollToBottom()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 0f;
        }

        // --- reward popup (mirrors ChatWindow's, standalone here) ---

        void ShowRewardPopup(Sprite icon, string title, string headline, string sub,
            string buttonLabel, UnityEngine.Events.UnityAction onClose)
        {
            var canvasGo = new GameObject("RewardPopupCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 260;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            var root = UiFactory.Rect("RewardPopup", canvasGo.transform);
            UiFactory.Stretch(root);
            var dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.8f);
            dim.raycastTarget = true;

            var card = UiFactory.Panel("Card", root, BinancePalette.Surface);
            UiFactory.Anchor(card.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(840f, 760f));

            var iconImg = UiFactory.Icon("Icon", card.transform, icon, BinancePalette.Yellow);
            UiFactory.Anchor(iconImg.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -180f), new Vector2(240f, 240f));

            var titleLabel = UiFactory.Text("Title", card.transform, title, 38f,
                BinancePalette.TextSecondary, TextAlignmentOptions.Center);
            UiFactory.Anchor(titleLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -330f), new Vector2(-60f, 60f));

            var headlineLabel = UiFactory.Text("Headline", card.transform, headline, 56f,
                BinancePalette.Yellow, TextAlignmentOptions.Center);
            headlineLabel.fontStyle = FontStyles.Bold;
            UiFactory.Anchor(headlineLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -400f), new Vector2(-60f, 80f));

            var subLabel = UiFactory.Text("Sub", card.transform, sub, 28f,
                BinancePalette.TextSecondary, TextAlignmentOptions.Center);
            UiFactory.Anchor(subLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -500f), new Vector2(-80f, 160f));

            var closeBg = UiFactory.Panel("CloseButton", card.transform, BinancePalette.Yellow);
            UiFactory.Anchor(closeBg.rectTransform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 48f), new Vector2(360f, 96f));
            var closeBtn = closeBg.gameObject.AddComponent<Button>();
            closeBtn.targetGraphic = closeBg;
            closeBtn.onClick.AddListener(() => { Destroy(canvasGo); onClose?.Invoke(); });
            var closeLabel = UiFactory.Text("Label", closeBg.transform, buttonLabel, 34f,
                BinancePalette.OnYellow, TextAlignmentOptions.Center);
            closeLabel.fontStyle = FontStyles.Bold;
            UiFactory.Stretch(closeLabel.rectTransform);
        }

        void ReturnToMap()
        {
            ArSession.Clear();
            SceneTransition.Load(MapSceneName);
        }

        static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}

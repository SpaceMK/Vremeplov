using System;
using System.Collections;
using System.IO;
using BinanceTheme;
using TalesTensor.Map;   // UiFactory
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace TalesTensor.Chat
{
    /// <summary>
    /// A messenger-style chat window pinned to the bottom of the screen for talking to
    /// the AR object (a placeholder cube today, an avatar soon). Brought up by
    /// <see cref="ARFloorPlacer"/> the moment the object is placed.
    ///
    /// Self-building in code (no prefabs/scene wiring) like the rest of this app's UI,
    /// and it drives <see cref="ChatClient"/> for the actual conversation — the same
    /// backend and protocol as the original TalesTensor project's ChatManager,
    /// including streamed TTS audio playback.
    /// </summary>
    [DisallowMultipleComponent]
    public class ChatWindow : MonoBehaviour
    {
        const float PanelHeight = 820f;   // of a 1920 reference height
        const float SidePad = 28f;        // gap from the screen's left/right edges
        const float BottomPad = 18f;      // gap from the screen's bottom edge
        const float HeaderHeight = 84f;
        const float InputHeight = 130f;
        const float SendWidth = 170f;
        const float MicWidth = 140f;
        const float MaxBubbleWidth = 720f;
        const float ShortcutRowHeight = 136f; // quick-ask chip row above the panel
        const string MapSceneName = "MapScene";

        ChatClient _client;
        AudioSource _audio;
        Animator _avatar; // the AR character, gestured as it replies
        // Gesture states in the Edinburgh King's animator (EdinburghKingAnim). Anim6 is the
        // "Nervously Look Around" clip — the most conversational of the available set; the
        // others (Defeated/Kiss/Dwarf Idle) read oddly for a reply, so we stick to it.
        static readonly string[] ResponseStates = { "Anim6" };

        ScrollRect _scroll;
        RectTransform _content;
        TMP_InputField _input;
        Button _sendButton;
        MicButton _micButton;
        GameObject _shortcutsRow; // quest quick-ask chips, shown after the intro

        bool _waiting;
        GameObject _typingRow;
        TextMeshProUGUI _typingText;

        /// <summary>Build a chat window and (optionally) have the character greet first.
        /// The header shows the character's name (from <see cref="ChatClient.CharacterName"/>).
        /// <paramref name="avatar"/>, when given, gestures as the character replies.</summary>
        public static ChatWindow Create(Animator avatar = null, bool sendIntro = true)
        {
            var go = new GameObject("ChatWindow");
            var window = go.AddComponent<ChatWindow>();
            window._avatar = avatar;
            window.Build();
            if (sendIntro) window.PlayIntro();
            return window;
        }

        bool _destroyed;

        void Awake()
        {
            // Share the preloader's client so the live chat continues the same session
            // as the intro that was warmed up on AR-scene entry.
            _client = ChatIntro.Client;
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
        }

        void OnDestroy() => _destroyed = true;

        // --- construction ---

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 190; // above the floor prompt (150), below the back-button HUD (200)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            // Bottom panel, inset from the screen edges so it reads as a contained chat
            // card with breathing room rather than hugging the sides/bottom.
            var panel = UiFactory.Panel("ChatPanel", transform, Alpha(BinancePalette.Surface, 0.97f));
            UiFactory.Anchor(panel.rectTransform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, BottomPad), new Vector2(-2f * SidePad, PanelHeight));

            BuildHeader(panel.transform);
            BuildMessageList(panel.transform);
            BuildInputBar(panel.transform);
            BuildShortcuts(panel.transform);
        }

        void BuildHeader(Transform parent)
        {
            var header = UiFactory.Panel("Header", parent, BinancePalette.SurfaceRaised);
            UiFactory.Anchor(header.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, HeaderHeight));

            // The character we're chatting to, e.g. "Charles II".
            string name = _client != null && !string.IsNullOrEmpty(_client.CharacterName)
                ? _client.CharacterName : "Charles II";
            var label = UiFactory.Text("Title", header.transform, name,
                40f, BinancePalette.TextPrimary, TextAlignmentOptions.Center);
            UiFactory.Stretch(label.rectTransform, 16f);
            label.fontStyle = FontStyles.Bold;
        }

        void BuildMessageList(Transform parent)
        {
            var scrollGo = new GameObject("Messages",
                typeof(RectTransform), typeof(ScrollRect));
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.SetParent(parent, false);
            // Fill the gap between the header (top) and the input bar (bottom). Kept full
            // panel width so the mask is at the panel edge (it never clips a bubble); the
            // bubbles' side gap comes from their own margin instead.
            scrollRt.anchorMin = new Vector2(0f, 0f);
            scrollRt.anchorMax = new Vector2(1f, 1f);
            scrollRt.offsetMin = new Vector2(0f, InputHeight);
            scrollRt.offsetMax = new Vector2(0f, -HeaderHeight);

            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30f;

            // Viewport — masks the content and catches drags.
            var viewport = new GameObject("Viewport",
                typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            var viewRt = (RectTransform)viewport.transform;
            viewRt.SetParent(scrollRt, false);
            UiFactory.Stretch(viewRt);
            var viewImg = viewport.GetComponent<Image>();
            viewImg.color = new Color(0f, 0f, 0f, 0f); // invisible but still receives drags
            _scroll.viewport = viewRt;

            // Content — a top-anchored vertical stack that grows with the messages.
            var contentGo = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _content = (RectTransform)contentGo.transform;
            _content.SetParent(viewRt, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;

            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(0, 0, 16, 16); // side gap comes from the scroll inset
            vlg.spacing = 16f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll.content = _content;
        }

        void BuildInputBar(Transform parent)
        {
            const float rightMargin = 16f, gap = 12f, leftMargin = 16f;
            float btnH = InputHeight - 36f;

            var bar = UiFactory.Rect("InputBar", parent);
            UiFactory.Anchor(bar,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0f, InputHeight));

            // Send button (far right).
            var send = UiFactory.Panel("Send", bar, BinancePalette.Yellow);
            UiFactory.Anchor(send.rectTransform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-rightMargin, 0f), new Vector2(SendWidth, btnH));
            _sendButton = send.gameObject.AddComponent<Button>();
            _sendButton.targetGraphic = send;
            _sendButton.onClick.AddListener(OnSendPressed);
            var sendLabel = UiFactory.Text("SendLabel", send.transform, "Send", 34f,
                BinancePalette.OnYellow, TextAlignmentOptions.Center);
            UiFactory.Stretch(sendLabel.rectTransform);
            sendLabel.fontStyle = FontStyles.Bold;

            // Mic button (left of Send) — voice input into the field.
            float micX = -(rightMargin + SendWidth + gap);
            var mic = UiFactory.Panel("Mic", bar, BinancePalette.SurfaceRaised);
            UiFactory.Anchor(mic.rectTransform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(micX, 0f), new Vector2(MicWidth, btnH));
            var micButton = mic.gameObject.AddComponent<Button>();
            micButton.targetGraphic = mic;
            var micLabel = UiFactory.Text("MicLabel", mic.transform, "Mic", 30f,
                BinancePalette.TextPrimary, TextAlignmentOptions.Center);
            UiFactory.Stretch(micLabel.rectTransform);
            micLabel.fontStyle = FontStyles.Bold;

            // Input field fills the rest, to the left of the two buttons.
            float rightCluster = rightMargin + SendWidth + gap + MicWidth + gap;
            var inputRt = BuildInputField(bar);
            inputRt.anchorMin = new Vector2(0f, 0.5f);
            inputRt.anchorMax = new Vector2(1f, 0.5f);
            inputRt.pivot = new Vector2(0.5f, 0.5f);
            inputRt.sizeDelta = new Vector2(-(leftMargin + rightCluster), btnH);
            inputRt.anchoredPosition = new Vector2((leftMargin - rightCluster) * 0.5f, 0f);

            // Wire the mic up now the input field exists, so transcripts write into it.
            // (Hides itself if speech isn't supported on this platform.)
            _micButton = mic.gameObject.AddComponent<MicButton>();
            _micButton.Initialize(micButton, mic, micLabel, _input,
                BinancePalette.SurfaceRaised, BinancePalette.Negative);
        }

        RectTransform BuildInputField(Transform parent)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            // Wire the field up while inactive so TMP_InputField doesn't initialize
            // (and warn) before its text component/viewport are assigned.
            go.SetActive(false);
            go.GetComponent<Image>().color = BinancePalette.SurfaceRaised;

            var input = go.AddComponent<TMP_InputField>();

            // Masked text area inside the field.
            var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            var areaRt = (RectTransform)area.transform;
            areaRt.SetParent(go.transform, false);
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(24f, 8f);
            areaRt.offsetMax = new Vector2(-24f, -8f);

            var placeholder = UiFactory.Text("Placeholder", area.transform, "Say something…",
                32f, BinancePalette.TextSecondary, TextAlignmentOptions.MidlineLeft);
            UiFactory.Stretch(placeholder.rectTransform);
            placeholder.enableWordWrapping = false;

            var text = UiFactory.Text("Text", area.transform, string.Empty,
                32f, BinancePalette.TextPrimary, TextAlignmentOptions.MidlineLeft);
            UiFactory.Stretch(text.rectTransform);
            text.enableWordWrapping = false;

            input.textViewport = areaRt;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.fontAsset = text.font;
            input.pointSize = 32f;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.onFocusSelectAll = false;
            input.onSubmit.AddListener(OnInputSubmit);

            go.SetActive(true);
            _input = input;
            return rt;
        }

        // --- conversation flow (ported from ChatManager) ---

        void PlayIntro() => StartCoroutine(IntroSequence());

        /// <summary>The opening exchange, with input locked throughout: show the preloaded
        /// introduction, fetch the quest answer IN PARALLEL while the intro's audio plays,
        /// then show it the moment the intro finishes — and only then unlock input.</summary>
        IEnumerator IntroSequence()
        {
            SetWaiting(true); // stays locked until the whole intro exchange is done
            ShowTyping();

            // 1. The introduction (usually already preloaded on AR entry).
            bool introReady = false;
            ChatClient.Result intro = default;
            ChatIntro.Request(this, r => { intro = r; introReady = true; });
            while (!introReady && !_destroyed) yield return null;
            if (_destroyed) yield break;

            HideTyping();
            ShowReply(intro); // bubble + gesture + audio; input still locked

            string quest = ChatIntro.QuestName;
            if (!intro.ok || string.IsNullOrEmpty(quest))
            {
                SetWaiting(false);
                yield break;
            }

            // Delivery quest: the King has thanked us for the letter — let his audio play,
            // grant the cupcake voucher, then reveal the XP quick-asks (no quest follow-up).
            if (ChatIntro.IsDelivery)
            {
                float a = 0f;
                while (_audio != null && _audio.isPlaying && a < 25f)
                {
                    a += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (_destroyed) yield break;

                QuestRewards.LetterQuest = null; // letter delivered
                PlayerProgress.Instance.AddItem(ItemCatalog.CupcakeVoucher, 1);
                SetWaiting(false);
                ShowRewardPopup(ProceduralIcons.Gem(), "Reward received",
                    "10% Off at Awesome Cupcakes", "A sweet token of His Majesty's thanks.",
                    "Continue", ShowShortcuts);
                yield break;
            }

            // 2. Fetch the quest answer NOW, in the background, while the intro audio plays.
            bool questReady = false;
            ChatClient.Result questReply = default;
            StartCoroutine(_client.Send($"I wanted to know more about {quest}",
                r => { questReply = r; questReady = true; }));

            // 3. Let the spoken introduction finish so its audio isn't cut off (capped).
            float waited = 0f;
            while (_audio != null && _audio.isPlaying && waited < 25f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            // 4. Show the quest answer the instant it's ready (typing only if we're still
            //    waiting on it), then unlock input.
            if (!questReady)
            {
                ShowTyping();
                while (!questReady && !_destroyed) yield return null;
                HideTyping();
            }
            if (_destroyed) yield break;

            ShowReply(questReply);
            SetWaiting(false);
            ShowShortcuts(); // the quest quick-asks appear once the full intro is shown
        }

        void OnInputSubmit(string _) => OnSendPressed();

        void OnSendPressed()
        {
            if (_waiting || _input == null) return;
            if (_micButton != null && _micButton.IsListening) _micButton.StopListening();
            string text = _input.text;
            if (string.IsNullOrWhiteSpace(text)) return;
            _input.text = string.Empty;
            Submit(text);
        }

        void Submit(string text, bool showUserBubble = true)
        {
            if (_waiting || string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();

            if (showUserBubble) AddBubble(text, isUser: true);
            SetWaiting(true);
            ShowTyping();
            StartCoroutine(_client.Send(text, OnResult));
        }

        void OnResult(ChatClient.Result result)
        {
            HideTyping();
            SetWaiting(false);
            ShowReply(result);

#if !UNITY_ANDROID && !UNITY_IOS
            if (result.ok && _input != null) _input.ActivateInputField();
#endif
        }

        /// <summary>Render a reply (bubble + gesture + audio) without changing the input-lock
        /// state — so the intro sequence can show replies while keeping input disabled.</summary>
        void ShowReply(ChatClient.Result result)
        {
            if (!result.ok)
            {
                AddBubble($"(I cannot answer right now — {result.error})", isUser: false);
                return;
            }

            AddBubble(result.reply, isUser: false);
            PlayResponseGesture();
            if (!string.IsNullOrEmpty(result.audioBase64))
                StartCoroutine(PlayAudioFromBase64(result.audioBase64));
        }

        /// <summary>Cross-fade the AR character into a random gesture as it replies; the
        /// controller's exit transitions return it to idle afterwards.</summary>
        void PlayResponseGesture()
        {
            if (_avatar == null || _avatar.runtimeAnimatorController == null) return;
            string state = ResponseStates[UnityEngine.Random.Range(0, ResponseStates.Length)];
            _avatar.CrossFadeInFixedTime(state, 0.2f, 0, 0f);
        }

        // --- quest quick-ask shortcuts + letter reward ---

        void BuildShortcuts(Transform panel)
        {
            string quest = ChatIntro.QuestName;
            if (string.IsNullOrEmpty(quest)) return; // shortcuts are quest-specific

            // A row of chips floating just above the panel's top edge.
            var row = UiFactory.Rect("Shortcuts", panel);
            UiFactory.Anchor(row, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0f),
                new Vector2(0f, 14f), new Vector2(0f, ShortcutRowHeight));
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            string[] questions =
            {
                $"Tell me about {quest}",
                $"Why does {quest} matter?",
                $"How can I aid {quest}?",
            };
            foreach (string q in questions) BuildChip(row, q);

            _shortcutsRow = row.gameObject;
            _shortcutsRow.SetActive(false);
        }

        void BuildChip(Transform parent, string question)
        {
            var chip = UiFactory.Panel("Chip", parent, Alpha(BinancePalette.SurfaceRaised, 0.97f));
            var button = chip.gameObject.AddComponent<Button>();
            button.targetGraphic = chip;
            button.onClick.AddListener(() => OnShortcut(question));

            var label = UiFactory.Text("ChipLabel", chip.transform, question, 24f,
                BinancePalette.TextPrimary, TextAlignmentOptions.Center);
            UiFactory.Stretch(label.rectTransform, 14f);
            label.enableWordWrapping = true;
        }

        void ShowShortcuts() { if (_shortcutsRow != null) _shortcutsRow.SetActive(true); }
        void HideShortcuts() { if (_shortcutsRow != null) _shortcutsRow.SetActive(false); }

        void OnShortcut(string question)
        {
            if (_waiting) return; // mid-reply
            // On a delivery quest the quick-asks award XP; otherwise they earn a letter.
            StartCoroutine(ChatIntro.IsDelivery ? XpShortcutFlow(question) : ShortcutFlow(question));
        }

        /// <summary>A quick-ask leads the King to hand over a letter for a DIFFERENT (actually
        /// spawned) quest: ask him to task us with delivering it, show his reply, grant the
        /// King's Scroll, remember the target, and open the reward popup (→ map).</summary>
        IEnumerator ShortcutFlow(string question)
        {
            HideShortcuts();
            string otherQuest = PickDeliveryQuest(ChatIntro.QuestName);

            AddBubble(question, isUser: true);
            SetWaiting(true); // stays locked — this exchange ends the AR visit
            ShowTyping();

            string prompt = $"{question} Reply in character, then tell me you have urgent " +
                            $"business and ask me to deliver a sealed letter to the keeper of {otherQuest}.";
            yield return SendAndShow(prompt);
            if (_destroyed) yield break;

            PlayerProgress.Instance.AddItem(ItemCatalog.KingsScroll, 1);
            QuestRewards.LetterQuest = otherQuest; // the quest that delivery unlocks
            ShowRewardPopup(ProceduralIcons.Scroll(), "Item obtained for quest", "King's Scroll",
                $"Carry it to the keeper of {otherQuest}.", "Collect", ReturnToMap);
        }

        /// <summary>On the delivery quest, a quick-ask completes it: the King replies, then we
        /// grant enough XP to reach the next level (deferred to the map for the celebration)
        /// and show the XP popup (→ map, where the level-up + energy refill happen).</summary>
        IEnumerator XpShortcutFlow(string question)
        {
            HideShortcuts();

            AddBubble(question, isUser: true);
            SetWaiting(true);
            ShowTyping();

            string prompt = $"{question} Reply in character, wish me well, and bid me farewell.";
            yield return SendAndShow(prompt);
            if (_destroyed) yield break;

            var progress = PlayerProgress.Instance;
            int xp = Mathf.Max(1, progress.XpToNext - progress.Xp); // enough to level up
            QuestRewards.PendingXp = xp;
            QuestRewards.CelebratePending = true; // applied on the next map load

            ShowRewardPopup(ProceduralIcons.Lightning(), "Quest complete", $"+{xp} XP",
                "Enough to reach the next level!", "Collect", ReturnToMap);
        }

        /// <summary>Send a prompt, show the reply (bubble + audio + gesture), and wait for the
        /// reply's audio to finish — shared by the quick-ask flows.</summary>
        IEnumerator SendAndShow(string prompt)
        {
            bool done = false;
            ChatClient.Result reply = default;
            StartCoroutine(_client.Send(prompt, r => { reply = r; done = true; }));
            while (!done && !_destroyed) yield return null;
            if (_destroyed) yield break;

            HideTyping();
            ShowReply(reply);

            float waited = 0f;
            while (_audio != null && _audio.isPlaying && waited < 25f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>Pick a quest to deliver a letter to: a DIFFERENT quest that was actually
        /// generated on the map this session (from <see cref="MapPinStore"/>), falling back to
        /// the catalog if there are no other spawned quests.</summary>
        string PickDeliveryQuest(string current)
        {
            var save = MapPinStore.Load();
            if (save != null && save.pins != null)
            {
                var candidates = new System.Collections.Generic.List<string>();
                foreach (var p in save.pins)
                    if (p != null && p.type == PinType.ArChat &&
                        !string.IsNullOrEmpty(p.questName) && p.questName != current &&
                        !candidates.Contains(p.questName))
                        candidates.Add(p.questName);
                if (candidates.Count > 0)
                    return candidates[UnityEngine.Random.Range(0, candidates.Count)];
            }
            return QuestCatalog.RandomOther(current);
        }

        /// <summary>A centred "reward" modal (own canvas, above everything). <paramref name="onClose"/>
        /// runs when its button is pressed.</summary>
        void ShowRewardPopup(Sprite icon, string title, string headline, string sub,
            string buttonLabel, UnityEngine.Events.UnityAction onClose)
        {
            var canvasGo = new GameObject("RewardPopupCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 260; // above the AR HUD (200) + chat (190)
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
                new Vector2(0f, -500f), new Vector2(-80f, 100f));

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

        void SetWaiting(bool waiting)
        {
            _waiting = waiting;
            if (_input != null) _input.interactable = !waiting;
            if (_sendButton != null) _sendButton.interactable = !waiting;
            if (_micButton != null) _micButton.SetInteractable(!waiting);
        }

        // --- message bubbles ---

        void AddBubble(string text, bool isUser) => BuildBubble(text, isUser);

        const float BubblePadX = 28f;   // horizontal text inset inside a bubble
        const float BubblePadY = 18f;   // vertical text inset
        const float BubbleSideMargin = 32f; // gap from the bubble to the panel's L/R edge

        TextMeshProUGUI BuildBubble(string text, bool isUser)
        {
            // Row spans the full list width (forced by the content's VerticalLayoutGroup);
            // its own HorizontalLayoutGroup insets the bubble from the sides via padding and
            // aligns it left (received) / right (sent). Padding is the reliable side margin.
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
            bubble.rectTransform.pivot = new Vector2(0.5f, 0.5f); // pop-in scales from centre

            var tmp = UiFactory.Text("Text", bubble.transform, text, 32f,
                isUser ? BinancePalette.OnYellow : BinancePalette.TextPrimary,
                TextAlignmentOptions.TopLeft);
            tmp.enableWordWrapping = true;

            // Measure the text so the bubble hugs it (up to a max), wrapping beyond that.
            float maxTextWidth = MaxBubbleWidth - BubblePadX * 2f;
            Vector2 natural = tmp.GetPreferredValues(text, maxTextWidth, Mathf.Infinity);
            float textW = Mathf.Min(natural.x, maxTextWidth);
            float textH = tmp.GetPreferredValues(text, textW, Mathf.Infinity).y;

            // The row's HorizontalLayoutGroup sizes the bubble to these preferred dims.
            var bubbleLe = bubble.gameObject.AddComponent<LayoutElement>();
            bubbleLe.preferredWidth = bubbleLe.minWidth = textW + BubblePadX * 2f;
            bubbleLe.preferredHeight = bubbleLe.minHeight = textH + BubblePadY * 2f;

            // Text fills the bubble minus padding.
            var tmpRt = tmp.rectTransform;
            tmpRt.anchorMin = Vector2.zero;
            tmpRt.anchorMax = Vector2.one;
            tmpRt.offsetMin = new Vector2(BubblePadX, BubblePadY);
            tmpRt.offsetMax = new Vector2(-BubblePadX, -BubblePadY);

            StartCoroutine(PopIn(bubble.rectTransform));
            ScrollToBottomNextFrame();
            return tmp;
        }

        /// <summary>Grow a freshly added bubble from nothing to full size with a springy pop.</summary>
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

        void ShowTyping()
        {
            if (_typingRow != null) return;
            // Build sized for three dots so the animation doesn't reflow the bubble.
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
            // Animate the typing indicator's dots while we wait for a reply.
            if (_typingText == null) return;
            int dots = (int)(Time.unscaledTime * 3f) % 3 + 1;
            _typingText.text = new string('.', dots);
        }

        void ScrollToBottomNextFrame() => StartCoroutine(ScrollToBottom());

        IEnumerator ScrollToBottom()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 0f;
        }

        // --- TTS audio (ported from ChatManager) ---

        IEnumerator PlayAudioFromBase64(string base64)
        {
            if (_audio == null) yield break;

            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64); }
            catch (Exception e) { Debug.LogError($"[Chat] audio base64 decode failed: {e.Message}"); yield break; }
            if (bytes == null || bytes.Length == 0) yield break;

            AudioType type = DetectAudioType(bytes);

            if (type == AudioType.WAV)
            {
                // The bot streams WAV with -1 size fields, which Unity's loader refuses;
                // decode the PCM directly.
                AudioClip wavClip = null;
                try { wavClip = WavUtility.ToAudioClip(bytes, "ChatReply"); }
                catch (Exception e) { Debug.LogWarning($"[Chat] WavUtility failed, falling back: {e.Message}"); }
                if (wavClip != null && wavClip.length > 0f)
                {
                    _audio.Stop();
                    _audio.clip = wavClip;
                    _audio.Play();
                    yield break;
                }
            }

            string ext = type switch
            {
                AudioType.MPEG => "mp3",
                AudioType.OGGVORBIS => "ogg",
                AudioType.WAV => "wav",
                _ => "bin",
            };
            string path = Path.Combine(Application.temporaryCachePath, $"chat_{Guid.NewGuid():N}.{ext}");
            try { File.WriteAllBytes(path, bytes); }
            catch (Exception e) { Debug.LogError($"[Chat] failed to write audio temp file: {e.Message}"); yield break; }

            string url = "file://" + path.Replace("\\", "/");
            using (var req = UnityWebRequestMultimedia.GetAudioClip(url, type))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                    if (clip != null && clip.length > 0f)
                    {
                        _audio.Stop();
                        _audio.clip = clip;
                        _audio.Play();
                    }
                }
                else
                {
                    Debug.LogError($"[Chat] audio load failed ({type}): {req.error}");
                }
            }

            try { File.Delete(path); } catch { /* best effort */ }
        }

        static AudioType DetectAudioType(byte[] data)
        {
            if (data.Length < 4) return AudioType.UNKNOWN;
            if (data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F') return AudioType.WAV;
            if (data[0] == 'O' && data[1] == 'g' && data[2] == 'g' && data[3] == 'S') return AudioType.OGGVORBIS;
            if (data[0] == 'I' && data[1] == 'D' && data[2] == '3') return AudioType.MPEG;
            if (data[0] == 0xFF && (data[1] & 0xE0) == 0xE0) return AudioType.MPEG;
            return AudioType.UNKNOWN;
        }

        static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}

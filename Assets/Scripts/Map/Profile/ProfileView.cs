using BinanceTheme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TalesTensor.Map
{
    /// <summary>
    /// The player's profile: a scrollable modal sheet built entirely in code (no
    /// prefabs), opened from the map's top-left profile button. Shows a large avatar,
    /// the player's level + experience bars (Pokémon-Go style), the travel persona
    /// discovered in the onboarding quiz, and a list-based inventory split into regular
    /// Items and real-world Premium Items. Also owns the celebratory "Level up!" popup,
    /// which it raises whenever <see cref="PlayerProgress.OnLevelUp"/> fires.
    ///
    /// A scene-wide singleton created by <see cref="MapController"/> (so the level-up
    /// popup works even before the sheet is ever opened), mirroring <see cref="MapMenu"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class ProfileView : MonoBehaviour
    {
        public static ProfileView Instance { get; private set; }

        PlayerProgress _progress;
        RectTransform _overlay;   // dim + sheet
        RectTransform _content;   // scroll content the sections are rebuilt into
        PopupController _message; // use feedback (above the sheet)
        PopupController _levelUp; // congratulations (above everything)
        bool _open;

        public static ProfileView Create()
        {
            var go = new GameObject("ProfileView");
            go.SetActive(false);            // build in Awake after we're parented/configured
            var view = go.AddComponent<ProfileView>();
            go.SetActive(true);
            return view;
        }

        void Awake()
        {
            Instance = this;
            EnsureEventSystem();

            _progress = PlayerProgress.Instance;
            _progress.OnChanged += OnProgressChanged;
            _progress.OnLevelUp += OnLevelUp;

            Build();
        }

        void OnDestroy()
        {
            if (_progress != null)
            {
                _progress.OnChanged -= OnProgressChanged;
                _progress.OnLevelUp -= OnLevelUp;
            }
            if (Instance == this) Instance = null;
        }

        // --- public API ---

        public void Open()
        {
            _open = true;
            Refresh();
            _overlay.gameObject.SetActive(true);
        }

        public void Close()
        {
            _open = false;
            _overlay.gameObject.SetActive(false);
        }

        // --- construction ---

        Canvas _canvas;

        void Build()
        {
            var canvasGo = new GameObject("ProfileCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 210; // above the energy HUD (and MapMenu's profile button)
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            BuildSheet(canvasGo.transform);

            // Feedback popups are added AFTER the sheet so later siblings draw on top of
            // it within this canvas — otherwise the modal sheet would hide them.
            _message = BuildPopup(canvasGo.transform, "MessagePopup");
            _levelUp = BuildPopup(canvasGo.transform, "LevelUpPopup");
        }

        void BuildSheet(Transform parent)
        {
            _overlay = UiFactory.Rect("ProfileOverlay", parent);
            UiFactory.Stretch(_overlay);

            var dim = _overlay.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.72f);
            dim.raycastTarget = true; // blocks taps on the map behind the sheet

            var sheet = UiFactory.Panel("Sheet", _overlay, BinancePalette.Surface);
            UiFactory.Anchor(sheet.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(980f, 1680f));

            // Header: title + close button.
            var title = UiFactory.Text("Title", sheet.transform, "Profile", 46f,
                BinancePalette.TextPrimary, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            UiFactory.Anchor(title.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -34f), new Vector2(-40f, 64f));

            var close = UiFactory.Panel("Close", sheet.transform, BinancePalette.SurfaceRaised);
            UiFactory.Anchor(close.rectTransform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-24f, -22f), new Vector2(72f, 72f));
            var closeBtn = close.gameObject.AddComponent<Button>();
            closeBtn.targetGraphic = close;
            closeBtn.onClick.AddListener(Close);
            var closeX = UiFactory.Text("X", close.transform, "X", 40f,
                BinancePalette.TextPrimary, TextAlignmentOptions.Center);
            closeX.fontStyle = FontStyles.Bold;
            UiFactory.Stretch(closeX.rectTransform);

            BuildScroll(sheet.transform);
        }

        void BuildScroll(Transform sheet)
        {
            var scrollGo = UiFactory.Rect("Scroll", sheet);
            // Fill the sheet below the header, with side/bottom margins.
            scrollGo.anchorMin = Vector2.zero;
            scrollGo.anchorMax = Vector2.one;
            scrollGo.offsetMin = new Vector2(24f, 24f);
            scrollGo.offsetMax = new Vector2(-24f, -110f);
            var scroll = scrollGo.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;

            var viewport = UiFactory.Rect("Viewport", scrollGo);
            UiFactory.Stretch(viewport);
            var vpImage = viewport.gameObject.AddComponent<Image>();
            vpImage.color = new Color(0f, 0f, 0f, 0f); // invisible, but receives drags/scroll
            viewport.gameObject.AddComponent<RectMask2D>();

            _content = UiFactory.Rect("Content", viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;

            var vlg = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 24);
            vlg.spacing = 22f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = _content;

            // Hidden until the player taps the profile button.
            _overlay.gameObject.SetActive(false);
        }

        // --- dynamic content (rebuilt each time the sheet opens / data changes) ---

        void Refresh()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            BuildAvatar();
            BuildName();
            BuildProgressCard();
            BuildPersonaCard();
            BuildInventory();
            BuildAccountCard();
        }

        /// <summary>
        /// Account status and the way out of it: whether progress is backed up, and a
        /// sign-in / sign-out action. Guests get a prompt to connect an account, since
        /// this is the only place in the app that explains why they'd want to.
        /// </summary>
        void BuildAccountCard()
        {
            AddHeader("Account");

            bool cloud = AuthService.IsCloudBacked;
            var card = AddCard("Account", 200f);

            string title = cloud
                ? (string.IsNullOrEmpty(AuthService.Email) ? "Signed in with Google" : AuthService.Email)
                : "Playing as a guest";

            string detail = cloud
                ? "Your progress is backed up and will follow you to a new device."
                : "Progress is saved on this device only. Sign in with Google to back it up.";

            var titleLabel = UiFactory.Text("AccountTitle", card.transform, title, 28f,
                BinancePalette.TextPrimary, TextAlignmentOptions.Left);
            titleLabel.fontStyle = FontStyles.Bold;
            TopBand(titleLabel.rectTransform, 24f, -20f, -58f);

            var detailLabel = UiFactory.Text("AccountDetail", card.transform, detail, 22f,
                BinancePalette.TextSecondary, TextAlignmentOptions.TopLeft);
            TopBand(detailLabel.rectTransform, 24f, -62f, -130f);

            AddAccountButton(card.transform, cloud);
        }

        void AddAccountButton(Transform card, bool signedIn)
        {
            var fill = signedIn ? BinancePalette.Line : BinancePalette.Yellow;
            var textColor = signedIn ? BinancePalette.TextPrimary : BinancePalette.OnYellow;
            string label = signedIn ? "Sign out" : "Sign in with Google";

            var panel = UiFactory.Panel("AccountAction", card, fill);
            UiFactory.Anchor(panel.rectTransform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 18f), new Vector2(-48f, 68f));

            var text = UiFactory.Text("Label", panel.transform, label, 26f,
                textColor, TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Bold;
            UiFactory.Stretch(text.rectTransform);

            var button = panel.gameObject.AddComponent<Button>();
            button.targetGraphic = panel;
            if (signedIn) button.onClick.AddListener(ConfirmSignOut);
            else button.onClick.AddListener(SignIn);
        }

        void ConfirmSignOut()
        {
            ChoiceDialog.Show(
                "Sign out?",
                "Your progress is backed up to your Google account and will be restored " +
                "when you sign back in.\n\nIt will be removed from this device.",
                primaryText: "Sign out",
                secondaryText: "Cancel",
                onChoice: async confirmed =>
                {
                    if (!confirmed) return;
                    await SignInFlow.SignOutAsync();
                    if (this != null) Refresh();
                });
        }

        async void SignIn()
        {
            if (!GoogleCredentialProvider.IsSupported)
            {
                ShowMessage("Not available here", "Google sign-in needs a phone build.");
                return;
            }

            var result = await SignInFlow.SignInWithGoogleAsync();
            if (this == null || result.Cancelled) return;

            if (!result.SignedIn)
            {
                ShowMessage("Couldn't sign in", result.Error ?? "Please try again.");
                return;
            }

            Refresh();
        }

        void ShowMessage(string title, string message)
        {
            if (_message != null) _message.Show(title, message);
            else Debug.LogWarning($"[Profile] {title}: {message}");
        }

        void BuildAvatar()
        {
            var section = AddSection("Avatar", 300f);
            var ring = UiFactory.Panel("AvatarRing", section, BinancePalette.SurfaceRaised);
            UiFactory.Anchor(ring.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(250f, 250f));
            var icon = UiFactory.Icon("AvatarIcon", ring.transform, ProceduralIcons.Profile(), BinancePalette.Yellow);
            UiFactory.Anchor(icon.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(150f, 150f));
        }

        void BuildName()
        {
            var section = AddSection("Name", 64f);
            string name = PlayerSession.User.displayName;
            if (string.IsNullOrEmpty(name)) name = "Guest";
            var label = UiFactory.Text("NameLabel", section, name, 44f,
                BinancePalette.TextPrimary, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
            UiFactory.Stretch(label.rectTransform);
        }

        void BuildProgressCard()
        {
            var card = AddCard("Progress", 230f);

            int level = _progress.Level;
            bool maxed = _progress.IsMaxLevel;

            var levelLabel = UiFactory.Text("LevelLabel", card.transform,
                $"Level {level}  /  {_progress.MaxLevel}", 30f, BinancePalette.TextPrimary, TextAlignmentOptions.Left);
            levelLabel.fontStyle = FontStyles.Bold;
            TopBand(levelLabel.rectTransform, 24f, -50f, -16f);

            // Level bar: how far through all 30 levels the player is.
            AddBar(card.transform, 64f, 26f, (float)level / _progress.MaxLevel, BinancePalette.Yellow);

            string xpText = maxed ? "Experience  —  MAX LEVEL"
                                  : $"Experience  {_progress.Xp} / {_progress.XpToNext}";
            var xpLabel = UiFactory.Text("XpLabel", card.transform, xpText, 26f,
                BinancePalette.TextSecondary, TextAlignmentOptions.Left);
            TopBand(xpLabel.rectTransform, 24f, -148f, -114f);

            // Experience bar: progress toward the next level.
            AddBar(card.transform, 162f, 26f, _progress.LevelProgress, BinancePalette.Positive);
        }

        void BuildPersonaCard()
        {
            var card = AddCard("Persona", 250f);
            PersonaInfo persona = PersonaInfo.Current();

            var caption = UiFactory.Text("Caption", card.transform, "YOUR TRAVEL STYLE", 20f,
                BinancePalette.TextSecondary, TextAlignmentOptions.Left);
            caption.fontStyle = FontStyles.Bold;
            caption.characterSpacing = 4f;
            TopBand(caption.rectTransform, 28f, -48f, -20f);

            var title = UiFactory.Text("PersonaTitle", card.transform, persona.Title, 36f,
                BinancePalette.Yellow, TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold;
            TopBand(title.rectTransform, 28f, -104f, -56f);

            var blurb = UiFactory.Text("PersonaBlurb", card.transform, persona.Blurb, 24f,
                BinancePalette.TextSecondary, TextAlignmentOptions.TopLeft);
            var brt = blurb.rectTransform;
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.offsetMin = new Vector2(28f, 24f);
            brt.offsetMax = new Vector2(-28f, -116f);
        }

        void BuildInventory()
        {
            AddHeader("Items");
            int regular = AddItemRows(ItemCategory.Regular);
            if (regular == 0) AddEmptyRow("No items yet — claim collectibles out on the map.");

            AddHeader("Premium Items");
            int premium = AddItemRows(ItemCategory.Premium);
            if (premium == 0) AddEmptyRow("No premium rewards yet — real-world vouchers appear here.");
        }

        int AddItemRows(ItemCategory category)
        {
            int shown = 0;
            var inv = _progress.Inventory;
            for (int i = 0; i < inv.Count; i++)
            {
                var stack = inv[i];
                var def = ItemCatalog.Get(stack != null ? stack.itemId : null);
                if (def == null || def.Category != category || stack.count <= 0) continue;
                BuildItemRow(def, stack.count);
                shown++;
            }
            return shown;
        }

        void BuildItemRow(ItemDefinition def, int count)
        {
            var row = AddCard($"Item_{def.Id}", 180f);

            var iconBg = UiFactory.Panel("IconBg", row.transform, BinancePalette.Surface);
            UiFactory.Anchor(iconBg.rectTransform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(26f, 0f), new Vector2(108f, 108f));
            var glyph = UiFactory.Icon("Glyph", iconBg.transform, IconFor(def.Icon),
                def.Category == ItemCategory.Premium ? BinancePalette.Yellow : BinancePalette.Positive);
            UiFactory.Anchor(glyph.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(64f, 64f));

            var name = UiFactory.Text("Name", row.transform, def.Name, 30f,
                BinancePalette.TextPrimary, TextAlignmentOptions.TopLeft);
            name.fontStyle = FontStyles.Bold;
            SideBand(name.rectTransform, 160f, 200f, -18f, -64f);

            var desc = UiFactory.Text("Desc", row.transform, def.Description, 22f,
                BinancePalette.TextSecondary, TextAlignmentOptions.TopLeft);
            SideBand(desc.rectTransform, 160f, 200f, -70f, -158f);

            var countLabel = UiFactory.Text("Count", row.transform, $"x{count}", 30f,
                BinancePalette.Yellow, TextAlignmentOptions.TopRight);
            countLabel.fontStyle = FontStyles.Bold;
            UiFactory.Anchor(countLabel.rectTransform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-26f, -22f), new Vector2(150f, 44f));

            if (def.Usable)
            {
                var btn = UiFactory.Panel("UseButton", row.transform, BinancePalette.Yellow);
                UiFactory.Anchor(btn.rectTransform,
                    new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-26f, 24f), new Vector2(168f, 76f));
                var button = btn.gameObject.AddComponent<Button>();
                button.targetGraphic = btn;
                string id = def.Id; // capture for the closure
                button.onClick.AddListener(() => OnUse(id));
                var lbl = UiFactory.Text("Label", btn.transform, "Use", 30f,
                    BinancePalette.OnYellow, TextAlignmentOptions.Center);
                lbl.fontStyle = FontStyles.Bold;
                UiFactory.Stretch(lbl.rectTransform);
            }
            else
            {
                var tag = UiFactory.Text("Tag", row.transform, "In-store reward", 22f,
                    BinancePalette.TextSecondary, TextAlignmentOptions.Right);
                UiFactory.Anchor(tag.rectTransform,
                    new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-26f, 30f), new Vector2(280f, 40f));
            }
        }

        void OnUse(string id)
        {
            var result = _progress.TryUseItem(id);
            var def = ItemCatalog.Get(id);
            switch (result)
            {
                case PlayerProgress.UseResult.Used:
                    _message.Show(def.Name, $"+{def.EnergyGain} energy. Nicely topped up!");
                    break;
                case PlayerProgress.UseResult.NoEffect:
                    _message.Show("Energy already full", "Save this for when you actually need it.");
                    break;
                // Empty / NotUsable shouldn't happen from a live Use button; ignore.
            }
            // OnProgressChanged rebuilds the list so the count/use button update.
        }

        // --- level up ---

        void OnLevelUp(int newLevel)
        {
            // When a delivery quest triggered this level-up, the energy was also refilled —
            // congratulate on both (the flag is still set while the XP is being granted).
            string message = QuestRewards.CelebratePending
                ? $"Congratulations — you've reached level {newLevel}!\nYour energy has been fully restored."
                : $"Congratulations — you've reached level {newLevel}!\nKeep exploring to climb even higher.";
            _levelUp.Show("Level Up!", message);
        }

        void OnProgressChanged()
        {
            if (_open) Refresh();
        }

        // --- small builders / layout helpers ---

        RectTransform AddSection(string name, float height)
        {
            var rt = UiFactory.Rect(name, _content);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return rt;
        }

        Image AddCard(string name, float height)
        {
            var img = UiFactory.Panel(name, _content, BinancePalette.SurfaceRaised);
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return img;
        }

        void AddHeader(string text)
        {
            var section = AddSection($"Header_{text}", 52f);
            var label = UiFactory.Text("HeaderLabel", section, text, 30f,
                BinancePalette.TextPrimary, TextAlignmentOptions.BottomLeft);
            label.fontStyle = FontStyles.Bold;
            var rt = label.rectTransform;
            UiFactory.Stretch(rt);
            rt.offsetMin = new Vector2(8f, 0f);
        }

        void AddEmptyRow(string text)
        {
            var card = AddCard("Empty", 96f);
            var label = UiFactory.Text("EmptyLabel", card.transform, text, 24f,
                BinancePalette.TextDisabled, TextAlignmentOptions.Center);
            UiFactory.Stretch(label.rectTransform, 24f);
        }

        /// <summary>A top-anchored horizontal band inside a card, by top/bottom inset from the card top.</summary>
        static void TopBand(RectTransform rt, float left, float topY, float bottomY)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, topY);
            rt.offsetMax = new Vector2(-left, bottomY);
        }

        /// <summary>A top-anchored band with independent left/right insets (for item rows).</summary>
        static void SideBand(RectTransform rt, float left, float right, float topY, float bottomY)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, bottomY);
            rt.offsetMax = new Vector2(-right, topY);
        }

        /// <summary>A full-width rounded progress bar inside a card, filled to <paramref name="fraction"/>.</summary>
        void AddBar(Transform card, float topInset, float height, float fraction, Color fill)
        {
            var track = UiFactory.Panel("BarTrack", card, BinancePalette.Line);
            var trt = track.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(24f, -(topInset + height));
            trt.offsetMax = new Vector2(-24f, -topInset);

            var bar = UiFactory.Panel("BarFill", track.transform, fill);
            var brt = bar.rectTransform;
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            brt.pivot = new Vector2(0f, 0.5f);
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
        }

        static Sprite IconFor(ItemIcon icon) => icon switch
        {
            ItemIcon.Booster => ProceduralIcons.Lightning(),
            ItemIcon.Voucher => ProceduralIcons.Gem(),
            ItemIcon.Scroll => ProceduralIcons.Scroll(),
            ItemIcon.Flower => ProceduralIcons.Flower(),
            _ => ProceduralIcons.Gem(),
        };

        // --- shared popup builder (matches MapMenu's "Coming soon" styling) ---

        PopupController BuildPopup(Transform parent, string name)
        {
            var root = UiFactory.Rect(name, parent);
            UiFactory.Stretch(root);

            var dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.7f);
            dim.raycastTarget = true;

            var card = UiFactory.Panel("Card", root, BinancePalette.Surface);
            UiFactory.Anchor(card.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(820f, 520f));

            var title = UiFactory.Text("Title", card.transform, "", 46f,
                BinancePalette.TextPrimary, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            UiFactory.Anchor(title.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -50f), new Vector2(-60f, 76f));

            var msg = UiFactory.Text("Message", card.transform, "", 30f,
                BinancePalette.TextSecondary, TextAlignmentOptions.Center);
            UiFactory.Anchor(msg.rectTransform,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            msg.rectTransform.offsetMin = new Vector2(56f, 150f);
            msg.rectTransform.offsetMax = new Vector2(-56f, -150f);

            var closeBg = UiFactory.Panel("CloseButton", card.transform, BinancePalette.Yellow);
            UiFactory.Anchor(closeBg.rectTransform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 44f), new Vector2(300f, 88f));
            var closeBtn = closeBg.gameObject.AddComponent<Button>();
            closeBtn.targetGraphic = closeBg;
            var closeLabel = UiFactory.Text("Label", closeBg.transform, "Got it", 32f,
                BinancePalette.OnYellow, TextAlignmentOptions.Center);
            closeLabel.fontStyle = FontStyles.Bold;
            UiFactory.Stretch(closeLabel.rectTransform);

            var popup = root.gameObject.AddComponent<PopupController>();
            popup.root = root.gameObject;
            popup.titleLabel = title;
            popup.messageLabel = msg;
            closeBtn.onClick.AddListener(popup.Close);

            root.gameObject.SetActive(false);
            return popup;
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

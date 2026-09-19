using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TalesTensor.Map
{
    /// <summary>
    /// One interactive map pin: a billboarded teardrop marker (tinted + glyphed by
    /// <see cref="PinType"/>) with a <see cref="BoxCollider"/> for tapping, plus a
    /// world-space "card" that animates open to the side to describe the experience.
    /// When the player is within range the card also shows the energy cost + a Spend
    /// button. Built entirely in code by <see cref="MapPinLayer"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class MapPin : MonoBehaviour
    {
        // Left padding for the card's text so it clears the pin head, which overlaps
        // the card's left edge (the card pivots out to the side from the pin).
        const float LeftTextMargin = 72f;
        // Card layout. Width is fixed; height is sized to the body text so the panel
        // doesn't leave dead space (or clip) as the blurb length varies by pin type.
        const float CardWidth = 360f;
        const float BodyRightMargin = 18f;
        const float BodyTopInset = 66f;     // space above the body for the header band
        const float BodyBottomInset = 64f;  // space below the body when the cost row shows
        const float CollapsedBottomInset = 18f; // small pad below the body when it's hidden
        const float MinBodyHeight = 36f;    // floor so short blurbs still read as a card
        const float PulseSpeed = 4.5f;       // radians/sec
        const float PulseAmplitude = 0.09f;  // ±9% scale when in range
        const float PulseFade = 0.3f;        // seconds to ease the pulse in/out of range
        // While open, the pin's own marker sprites jump above its card (which itself
        // sits above every other pin), so the card tucks behind only its own pin.
        const int CardSortingOrder = 25;
        const int MarkerOpenBoost = 7; // 19/20/21 -> 26/27/28, above CardSortingOrder
        // Scene entered when an AR Experience pin is accepted; must be in Build Settings.
        const string ArSceneName = "ARScene";
        // Video played inside the AR portal when a Portal pin is entered without a live
        // Time Portal render. Shipped inside the build via StreamingAssets, so the AR
        // portal has something to play offline; the path is resolved at runtime because
        // StreamingAssets URLs differ per platform (jar:file on Android, filesystem
        // path elsewhere).
        const string OfflinePortalVideoRelativePath = "Portals/StaraZeleznicka.mp4";
        static string OfflinePortalVideoUrl =>
            System.IO.Path.Combine(Application.streamingAssetsPath, OfflinePortalVideoRelativePath)
                .Replace('\\', '/');
        // A claimed timed event grants, at random, this much energy or an ice-cream voucher.
        const int TimedEnergyReward = 15;

        MapController _map;
        Camera _cam;
        PinDefinition _def;
        PinTypeInfo _info;

        // Marker
        Transform _marker;
        readonly List<(SpriteRenderer sr, int order)> _markerSprites = new();
        // Card
        RectTransform _card;
        CanvasGroup _cardGroup;
        GameObject _costRow;
        RectTransform _bodyRect;
        float _bodyHeight;     // measured height of the wrapped body text
        bool _costRowVisible;  // whether the card currently reserves room for the button

        bool _open;
        bool _inRange;
        Coroutine _anim;
        // Intro: pins stay hidden (scale 0) then bounce up to full size. Defaults keep
        // them invisible from the first frame until PlayIntro runs.
        bool _introDone;
        float _introScale;
        float _pulseWeight; // 0..1, eased toward in-range so the pulse never snaps on

        float _pinHeight;
        float _cardGap;
        float _interactRangeMeters;

        public PinDefinition Definition => _def;
        public bool IsOpen => _open;

        /// <summary>Raised when this pin is successfully claimed (and about to be
        /// consumed off the map) so its owner can drop its reference to it.</summary>
        public event Action<MapPin> Claimed;

        public void Init(MapController map, PinDefinition def, float pinHeight,
            float cardGap, float cardScale, float interactRangeMeters,
            Sprite teardropOverride, Sprite glyphOverride)
        {
            _map = map;
            _cam = map.mapCamera != null ? map.mapCamera : Camera.main;
            _def = def;
            _info = PinTypeInfo.For(def.type);
            _pinHeight = pinHeight;
            _cardGap = cardGap;
            _interactRangeMeters = interactRangeMeters;

            BuildMarker(teardropOverride, glyphOverride);
            BuildCard(cardScale);
        }

        // --- construction ---

        void BuildMarker(Sprite teardropOverride, Sprite glyphOverride)
        {
            var markerGo = new GameObject("Marker");
            _marker = markerGo.transform;
            _marker.SetParent(transform, false);
            _marker.localPosition = Vector3.zero;
            _marker.localScale = Vector3.one * _pinHeight;

            if (teardropOverride != null)
            {
                // Custom art supplies its own look; just tint it with the accent.
                AddSprite("Body", teardropOverride, _info.Accent, 20, 0f);
            }
            else
            {
                // White outer border + coloured inner teardrop drawn over it.
                AddSprite("Border", ProceduralIcons.Teardrop(), Color.white, 19, 0f);
                AddSprite("Inner", ProceduralIcons.TeardropInner(), _info.Accent, 20, -0.01f);
            }

            var glyph = AddSprite("Glyph", glyphOverride != null ? glyphOverride : _info.GlyphSprite(),
                Color.white, 21, -0.02f);
            glyph.transform.localPosition = new Vector3(0f, 0.645f, -0.02f); // sit in the head
            glyph.transform.localScale = Vector3.one * 0.34f;

            // Tappable box around the head, in marker-local units (sprite is 1 tall).
            var col = markerGo.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.6f, 0f);
            col.size = new Vector3(0.7f, 0.8f, 0.2f);
        }

        SpriteRenderer AddSprite(string name, Sprite sprite, Color color, int order, float z)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_marker, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            _markerSprites.Add((sr, order));
            return sr;
        }

        void BuildCard(float cardScale)
        {
            var canvasGo = new GameObject("Card", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup));
            _card = (RectTransform)canvasGo.transform;
            _card.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = _cam;
            // Above other pins and the map, but below THIS pin's own (boosted) marker
            // while open — see MarkerOpenBoost — so it tucks behind only its own pin.
            canvas.sortingOrder = CardSortingOrder;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;

            _cardGroup = canvasGo.GetComponent<CanvasGroup>();

            // Pivot on the left edge so it grows out to the side from the pin. Height is
            // set once the body text is known (see below); start at the width only.
            _card.sizeDelta = new Vector2(CardWidth, BodyTopInset + MinBodyHeight + BodyBottomInset);
            _card.pivot = new Vector2(0f, 0.5f);
            _card.localScale = Vector3.one * cardScale;

            // Background — white, so the card reads as the pin's white border extending out.
            var bg = UiFactory.Panel("Bg", _card, Color.white);
            UiFactory.Stretch(bg.rectTransform);

            // Accent header
            var header = UiFactory.Panel("Header", _card, _info.Accent);
            UiFactory.Anchor(header.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -6f), new Vector2(-12f, 56f));
            header.rectTransform.offsetMin = new Vector2(6f, header.rectTransform.offsetMin.y);
            header.rectTransform.offsetMax = new Vector2(-6f, header.rectTransform.offsetMax.y);

            var headerLabel = UiFactory.Text("HeaderLabel", header.transform, _info.Label, 30f,
                new Color(0.03f, 0.04f, 0.06f, 1f), TextAlignmentOptions.Center);
            UiFactory.Stretch(headerLabel.rectTransform, 8f);
            // Extra left inset so the label clears the pin head overlapping the card's
            // left edge (the card pivots out from the pin).
            headerLabel.rectTransform.offsetMin = new Vector2(LeftTextMargin, headerLabel.rectTransform.offsetMin.y);
            headerLabel.fontStyle = FontStyles.Bold;

            // Body text: quest name for AR, quest name + render state for a live portal,
            // availability window for timed events, otherwise the type blurb.
            string body;
            if (_def.type == PinType.ArChat && !string.IsNullOrEmpty(_def.questName))
                body = $"Quest: {_def.questName}";
            else if (_def.type == PinType.Portal && _def.IsLive)
            {
                string quest = string.IsNullOrEmpty(_def.questName) ? _info.Blurb : $"Quest: {_def.questName}";
                body = _def.portal.HasPlayableVideo
                    ? quest
                    : $"{quest}\nVideo still rendering — a preview plays for now.";
            }
            else if (_def.type == PinType.TimedEvent && _def.HasWindow)
                body = $"Available {FormatClock(_def.windowStartMinutes)} - " +
                       $"{FormatClock(_def.windowEndMinutes)}\n{_info.Blurb}";
            else
                body = _info.Blurb;
            var bodyLabel = UiFactory.Text("Body", _card, body, 26f,
                new Color(0.10f, 0.11f, 0.13f, 1f), TextAlignmentOptions.TopLeft);
            bodyLabel.enableWordWrapping = true;
            UiFactory.Anchor(bodyLabel.rectTransform,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            bodyLabel.rectTransform.offsetMax = new Vector2(-BodyRightMargin, -BodyTopInset);
            _bodyRect = bodyLabel.rectTransform;

            // Size the card height to fit the wrapped body text (width stays fixed), so
            // the panel hugs its content instead of using a one-size-fits-all box.
            float bodyWidth = CardWidth - LeftTextMargin - BodyRightMargin;
            _bodyHeight = Mathf.Max(bodyLabel.GetPreferredValues(body, bodyWidth, 0f).y, MinBodyHeight);

            BuildCostRow();
            // Start collapsed (no button); SetCostRowVisible expands the card when the
            // player is in range and the Spend button appears.
            ApplyCardHeight(showCostRow: false);

            _card.gameObject.SetActive(false);
            _card.localScale = new Vector3(0f, cardScale, cardScale);
            _cardGroup.alpha = 0f;
        }

        void BuildCostRow()
        {
            var row = UiFactory.Rect("CostRow", _card);
            UiFactory.Anchor(row, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 12f), new Vector2(-24f, 52f));
            row.offsetMin = new Vector2(12f, 12f);
            row.offsetMax = new Vector2(-12f, 64f);

            var btn = UiFactory.Panel("SpendButton", row, _info.Accent);
            UiFactory.Stretch(btn.rectTransform);
            var button = btn.gameObject.AddComponent<Button>();
            button.targetGraphic = btn;
            button.onClick.AddListener(OnSpend);

            bool free = _def.energyCost <= 0;

            // Free pins (timed events) read as a centred "Free" label with no energy icon;
            // priced pins show the lightning icon + cost.
            if (!free)
            {
                var icon = UiFactory.Icon("CostIcon", btn.transform, ProceduralIcons.Lightning(),
                    new Color(0.03f, 0.04f, 0.06f, 1f));
                UiFactory.Anchor(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(-34f, 0f), new Vector2(34f, 34f));
            }

            var label = UiFactory.Text("CostLabel", btn.transform,
                free ? "Free" : _def.energyCost.ToString(), 30f,
                new Color(0.03f, 0.04f, 0.06f, 1f),
                free ? TextAlignmentOptions.Center : TextAlignmentOptions.Left);
            label.fontStyle = FontStyles.Bold;
            if (free)
                UiFactory.Stretch(label.rectTransform);
            else
                UiFactory.Anchor(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, 0.5f), new Vector2(-6f, 0f), new Vector2(60f, 40f));

            _costRow = row.gameObject;
        }

        // --- per-frame billboarding & proximity ---

        void LateUpdate()
        {
            if (_cam == null) return;
            Quaternion face = _cam.transform.rotation;

            // While the claim bounce-out plays, the coroutine owns the marker transform.
            if (_claiming) return;

            // Distance from the player to this pin, in real-world metres.
            _inRange = false;
            if (_map.playerMarker != null)
            {
                float units = Vector3.Distance(_map.playerMarker.position, transform.position);
                double metres = units * _map.MetersPerUnit;
                _inRange = metres <= _interactRangeMeters;
            }

            if (_marker != null)
            {
                _marker.rotation = face;
                // Ease the pulse in/out of range so it grows from the resting size
                // instead of snapping to a mid-oscillation value.
                _pulseWeight = Mathf.MoveTowards(_pulseWeight, _inRange ? 1f : 0f,
                    Time.deltaTime / PulseFade);

                float s;
                if (!_introDone)
                    s = _pinHeight * _introScale;                // bouncing in (or hidden at 0)
                else
                    s = _pinHeight * (1f + _pulseWeight * PulseAmplitude * Mathf.Sin(Time.time * PulseSpeed));
                _marker.localScale = Vector3.one * s;
            }

            if (_open && _card != null)
            {
                // Anchor the card to the pin head itself (no camera-relative offset) so
                // it stays attached as the camera orbits/tilts; the card billboards to
                // face the camera and its left-pivot makes it extend out to the side.
                _card.position = transform.position + Vector3.up * (_pinHeight + _cardGap);
                _card.rotation = face;
                SetCostRowVisible(_inRange);
            }
        }

        /// <summary>Show/hide the Spend button, resizing the card so it only reserves
        /// bottom room for the button while it's actually visible. No-ops if unchanged.</summary>
        void SetCostRowVisible(bool visible)
        {
            if (_costRowVisible == visible) return;
            ApplyCardHeight(visible);
        }

        /// <summary>Toggle the cost row and set the card height to fit the body text plus
        /// the button band (when shown) or just a small pad (when hidden).</summary>
        void ApplyCardHeight(bool showCostRow)
        {
            _costRowVisible = showCostRow;
            if (_costRow != null) _costRow.SetActive(showCostRow);

            float bottomInset = showCostRow ? BodyBottomInset : CollapsedBottomInset;
            if (_bodyRect != null)
                _bodyRect.offsetMin = new Vector2(LeftTextMargin, bottomInset);
            if (_card != null)
                _card.sizeDelta = new Vector2(CardWidth, BodyTopInset + _bodyHeight + bottomInset);
        }

        // --- intro ---

        /// <summary>Keep the pin hidden for <paramref name="delay"/> seconds, then bounce it
        /// up to full size. Called once by the layer when pins first appear.</summary>
        public void PlayIntro(float delay)
        {
            StartCoroutine(IntroRoutine(delay));
        }

        IEnumerator IntroRoutine(float delay)
        {
            _introDone = false;
            _introScale = 0f;
            if (delay > 0f) yield return new WaitForSeconds(delay);

            const float dur = 0.45f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                _introScale = EaseOutBack(Mathf.Clamp01(t / dur));
                yield return null;
            }
            _introScale = 1f;
            _pulseWeight = 0f;   // let the in-range pulse ease in after the bounce, not snap
            _introDone = true;
        }

        /// <summary>Ease that overshoots past 1 then settles, for a springy "pop" in.</summary>
        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float p = t - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }

        // --- open / close ---

        public void Open()
        {
            if (_open) return;
            _open = true;
            _card.gameObject.SetActive(true);
            SetCostRowVisible(_inRange);
            SetMarkerOnTop(true); // lift this pin above its card + all other pins
            StartAnim(1f);
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            StartAnim(0f);
        }

        public void Toggle()
        {
            if (_open) Close(); else Open();
        }

        void SetMarkerOnTop(bool onTop)
        {
            foreach (var (sr, order) in _markerSprites)
                if (sr != null) sr.sortingOrder = onTop ? order + MarkerOpenBoost : order;
        }

        void StartAnim(float target)
        {
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(Animate(target));
        }

        IEnumerator Animate(float target)
        {
            float baseScale = _card.localScale.z; // y/z hold the card's world scale
            float start = _card.localScale.x / Mathf.Max(baseScale, 1e-5f);
            float startA = _cardGroup.alpha;
            const float dur = 0.18f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                float s = Mathf.Lerp(start, target, k);
                _card.localScale = new Vector3(s * baseScale, baseScale, baseScale);
                _cardGroup.alpha = Mathf.Lerp(startA, target, k);
                yield return null;
            }
            _card.localScale = new Vector3(target * baseScale, baseScale, baseScale);
            _cardGroup.alpha = target;
            if (target <= 0f)
            {
                _card.gameObject.SetActive(false);
                SetMarkerOnTop(false); // restore once fully closed, keeping the illusion mid-close
            }
            _anim = null;
        }

        /// <summary>Format minutes-from-midnight as a 12-hour clock time, e.g. 810 -> "1:30pm".</summary>
        static string FormatClock(int minutesOfDay)
        {
            int h = (minutesOfDay / 60) % 24;
            int m = minutesOfDay % 60;
            string suffix = h < 12 ? "am" : "pm";
            int h12 = h % 12;
            if (h12 == 0) h12 = 12;
            return $"{h12}:{m:00}{suffix}";
        }

        void OnSpend()
        {
            if (_claiming) return; // already being collected

            int cost = _def.energyCost;

            // AR Experience pins drop you into the AR scene rather than awarding an item.
            // Pay the energy cost first, then wipe into the scene.
            if (_def.type == PinType.ArChat)
            {
                // Make sure the scene can actually load before charging the player —
                // otherwise a missing Build Settings entry would burn their energy.
                if (!Application.CanStreamedLevelBeLoaded(ArSceneName))
                {
                    if (MapMenu.Instance != null) MapMenu.Instance.ShowComingSoon();
                    else Debug.LogWarning($"[Pin] AR scene '{ArSceneName}' is not in Build Settings.");
                    return;
                }
                if (!EnergyController.Instance.TrySpend(cost))
                {
                    ShowNotEnoughEnergy(cost);
                    return;
                }
                // Charged: remember which quest we entered so the AR scene can use
                // it, consume the pin off the map, and wipe into the AR experience.
                // A live ar_interaction portal carries the real character + scene brief;
                // an offline demo pin just carries a quest name.
                if (_def.IsLive)
                    ArSession.BeginLiveChat(_def.portal);
                else
                    ArSession.Begin(_def.questName);
                Claimed?.Invoke(this);
                SceneTransition.Load(ArSceneName);
                PlayClaim();
                return;
            }

            // Portal pins also drop into the AR scene, but in portal mode: same floor
            // placement, then an oval video gateway instead of the King + chat.
            if (_def.type == PinType.Portal)
            {
                if (!Application.CanStreamedLevelBeLoaded(ArSceneName))
                {
                    if (MapMenu.Instance != null) MapMenu.Instance.ShowComingSoon();
                    else Debug.LogWarning($"[Pin] AR scene '{ArSceneName}' is not in Build Settings.");
                    return;
                }
                if (!EnergyController.Instance.TrySpend(cost))
                {
                    ShowNotEnoughEnergy(cost);
                    return;
                }
                // A live portal_video scene plays its own rendered video once the Time Portal
                // has produced it (resolved into portal.video by TimePortalClient); while it's
                // still rendering, or when no Time Portal is configured, it falls back to the
                // demo reconstruction video.
                string portalVideo = _def.portal != null && _def.portal.HasPlayableVideo
                    ? _def.portal.video.url
                    : OfflinePortalVideoUrl;
                ArSession.BeginPortal(portalVideo, _def.portal);
                Claimed?.Invoke(this);
                SceneTransition.Load(ArSceneName);
                PlayClaim();
                return;
            }

            // Timed events are collectible like an item, but only while their availability
            // window is open. Claiming one grants — at random — 15 energy or a premium
            // ice-cream voucher.
            if (_def.type == PinType.TimedEvent)
            {
                if (!IsWithinWindow())
                {
                    ShowOutsideWindow();
                    return;
                }
                if (!EnergyController.Instance.TrySpend(cost))
                {
                    ShowNotEnoughEnergy(cost);
                    return;
                }

                string body;
                if (UnityEngine.Random.value < 0.5f)
                {
                    int gained = EnergyController.Instance.Gain(TimedEnergyReward);
                    body = gained > 0
                        ? $"You collected the timed event and gained {gained} energy!"
                        : "You collected the timed event, but your energy was already full.";
                }
                else
                {
                    PlayerProgress.Instance.AddItem(ItemCatalog.IceCreamVoucher, 1);
                    var voucher = ItemCatalog.Get(ItemCatalog.IceCreamVoucher);
                    body = $"You collected the timed event and won {voucher.Name}.\n" +
                           "Find it in your profile inventory.";
                }
                if (MapMenu.Instance != null) MapMenu.Instance.ShowMessage("Claimed!", body);

                Claimed?.Invoke(this);
                PlayClaim();
                return;
            }

            // Collectible (Chrono Booster) and Premium (voucher) pins have a real claim
            // flow; any remaining experience still surfaces the "Coming soon" popup.
            string itemId = _def.type switch
            {
                PinType.Collect => ItemCatalog.ChronoBooster,
                PinType.Premium => ItemCatalog.CoffeeVoucher,
                _ => null,
            };
            if (itemId == null)
            {
                if (MapMenu.Instance != null) MapMenu.Instance.ShowComingSoon();
                else Debug.Log($"[Pin] Spend pressed on '{_def.id}' — no MapMenu to show the popup.");
                return;
            }

            if (!EnergyController.Instance.TrySpend(cost))
            {
                ShowNotEnoughEnergy(cost);
                return;
            }

            // Claimed: drop the reward into the inventory and consume the pin.
            PlayerProgress.Instance.AddItem(itemId, 1);
            var def = ItemCatalog.Get(itemId);
            if (MapMenu.Instance != null)
                MapMenu.Instance.ShowMessage("Claimed!",
                    $"You spent {cost} energy and picked up {def.Name}.\nFind it in your profile inventory.");

            // Tell the layer to forget us now (clears its open/tap reference), then play
            // the bounce-out before the GameObject is actually destroyed.
            Claimed?.Invoke(this);
            PlayClaim();
        }

        /// <summary>True if a timed event's availability window is currently open. A pin
        /// with no window (zero-length) is always collectible.</summary>
        bool IsWithinWindow()
        {
            if (!_def.HasWindow) return true;
            int minutesOfDay = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
            return minutesOfDay >= _def.windowStartMinutes &&
                   minutesOfDay < _def.windowEndMinutes;
        }

        /// <summary>Explain that a timed event can't be collected right now, and when its
        /// window opens/closes.</summary>
        void ShowOutsideWindow()
        {
            if (MapMenu.Instance == null) return;
            int minutesOfDay = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
            string when = minutesOfDay < _def.windowStartMinutes
                ? $"It opens at {FormatClock(_def.windowStartMinutes)}."
                : $"It closed at {FormatClock(_def.windowEndMinutes)}.";
            MapMenu.Instance.ShowMessage("Not available now",
                $"{when}\nAvailable {FormatClock(_def.windowStartMinutes)} - " +
                $"{FormatClock(_def.windowEndMinutes)}.");
        }

        /// <summary>Tell the player they can't afford this pin — energy regenerates over
        /// time, so nudge them to come back.</summary>
        void ShowNotEnoughEnergy(int cost)
        {
            if (MapMenu.Instance != null)
                MapMenu.Instance.ShowMessage("Not enough energy",
                    $"This costs {cost} energy.\nEnergy refills over time — check back soon.");
        }

        // --- claim animation ---

        bool _claiming;

        /// <summary>Close the card, lock out further taps, and bounce the marker away
        /// before destroying the pin.</summary>
        void PlayClaim()
        {
            _claiming = true;
            Close();
            var col = _marker != null ? _marker.GetComponent<BoxCollider>() : null;
            if (col != null) col.enabled = false;
            StartCoroutine(ClaimRoutine());
        }

        IEnumerator ClaimRoutine()
        {
            const float dur = 0.5f;
            const float popPoint = 0.3f; // grow, then shrink away
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);

                float scale = k < popPoint
                    ? Mathf.SmoothStep(1f, 1.25f, k / popPoint)
                    : Mathf.SmoothStep(1.25f, 0f, (k - popPoint) / (1f - popPoint));

                if (_marker != null)
                {
                    _marker.rotation = _cam != null ? _cam.transform.rotation : _marker.rotation;
                    _marker.localScale = Vector3.one * (_pinHeight * scale);
                    // Float upward as it pops so it reads as leaping into the inventory.
                    _marker.localPosition = new Vector3(0f, k * _pinHeight * 0.6f, 0f);
                }
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}

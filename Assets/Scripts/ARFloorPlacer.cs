using BinanceTheme;
using TalesTensor.Chat;
using TalesTensor.Map;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// In the AR scene, scans for a horizontal floor plane and, the moment one is
/// found, drops a 3D model onto it and removes the on-screen prompt. A centered
/// "Searching for a floor…" message shows while scanning.
///
/// Self-bootstraps when ARScene loads (no scene wiring) and adds an
/// <see cref="ARPlaneManager"/> to the XR Origin at runtime, so the bare AR rig
/// needs no extra components. The scene's <see cref="ARSession"/> starts disabled;
/// this enables it only after camera permission is granted, so the camera prompt is
/// a single controlled request rather than ARCore grabbing it at load. Swap
/// <see cref="BuildModel"/> for the real quest content later — the accepted quest is
/// available via <see cref="ArSession.QuestName"/>.
///
/// Plane detection is left at AR Foundation's own defaults on purpose. We set no
/// detection mode, no plane prefab and no distance gate, so if a floor is never
/// found the cause is the device, the environment or the session — not a setting of
/// ours narrowing what counts. Anything added back here should be weighed against
/// that: it is the reason this file no longer configures the plane manager at all.
/// </summary>
[DisallowMultipleComponent]
public class ARFloorPlacer : MonoBehaviour
{
    public const string ArSceneName = "ARScene";

    // Placeholder model size (metres); lifted half this so it rests on the surface.
    const float ModelSize = 0.2f;

    ARPlaneManager _planes;
    GameObject _ui;
    GameObject _messagePanel;
    TMP_Text _message;
    CanvasGroup _introGroup;
    GameObject _model;
    Animator _kingAnimator;
    bool _placed;

    const string ScanPrompt = "Move your phone slowly to scan the area";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        // Registered before any scene loads so it also catches ARScene on a direct boot.
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != ArSceneName) return;
        if (FindAnyObjectByType<ARFloorPlacer>() != null) return; // already present
        new GameObject("ARFloorPlacer").AddComponent<ARFloorPlacer>();
    }

    System.Collections.IEnumerator Start()
    {
        BuildMessage();

        // Warm the chatbot's opening line now, in parallel with the intro animation and
        // floor scan, so the welcome is already loaded when the chat opens on placement.
        // Pass the accepted quest so the greeting also leads into it. The portal experience
        // has no chat, so skip the preload there.
        ChatIntro.Reset();
        if (ArSession.Experience == ArExperience.KingChat)
            ChatIntro.Preload(this, ArSession.QuestName);

        // Quest-name intro: present it centre-screen, hold, then fade out before we ask
        // the player to find a floor — a brief "your experience begins" moment.
        yield return PlayQuestIntro();

#if UNITY_ANDROID && !UNITY_EDITOR
        // ARCore can auto-request this, but asking up front lets us message a denial
        // instead of leaving a black camera feed. (iOS/ARKit prompts automatically and
        // only needs the Camera Usage Description, which is set in Player Settings.)
        while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                   UnityEngine.Android.Permission.Camera))
        {
            SetMessage("Requesting camera access…");
            yield return RequestAndroidCameraPermission();

            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.Camera))
            {
                // Denied (possibly permanently). Nudge and wait, then re-check — if they
                // grant it from Settings and return, we pick up and carry on.
                SetMessage("Camera access is needed for AR.\nEnable it in Settings to play.");
                yield return new WaitForSeconds(2f);
            }
        }
#endif

        SetMessage(ScanPrompt);

#if UNITY_EDITOR
        // There's no real AR plane detection in the editor, so simulate finding a floor
        // a short distance in front of the camera after a brief "searching" beat. This
        // lets the place + chat flow be tested on PC without a device.
        yield return new WaitForSeconds(EditorFloorDelay);
        if (!_placed) PlaceAt(FloorInFrontOfCamera());
#else
        // Resolve availability with the session still disabled. This is explicitly
        // supported (see ARSession.CheckAvailability: "users may want to check
        // availability before enabling the session") and it matters for ordering —
        // see the comment above the session-enable further down.
        //
        // Make sure AR is actually usable before we start scanning. Without this, an
        // unsupported device or a missing/outdated "Google Play Services for AR" (ARCore)
        // leaves the session in a non-tracking state — the camera may show but NO planes
        // ever arrive, and we'd "search" forever with nothing to say about why.
        // CheckAvailability resolves support; NeedsInstall means the ARCore APK has to be
        // installed/updated first (a Play Store handoff).
        if (ARSession.state == ARSessionState.None ||
            ARSession.state == ARSessionState.CheckingAvailability)
        {
            SetMessage("Preparing AR…");
            yield return ARSession.CheckAvailability();
        }

        if (ARSession.state == ARSessionState.Unsupported)
        {
            SetMessage("AR is not supported on this device.");
            yield break;
        }

        if (ARSession.state == ARSessionState.NeedsInstall)
        {
            SetMessage("Installing AR services…");
            yield return ARSession.Install();
            if (ARSession.state == ARSessionState.NeedsInstall ||
                ARSession.state == ARSessionState.Unsupported)
            {
                SetMessage("AR services are required.\nInstall \"Google Play Services for AR\" to play.");
                yield break;
            }
        }

        var origin = FindAnyObjectByType<XROrigin>();
        if (origin == null)
        {
            SetMessage("AR not available on this device");
            yield break;
        }

        // The bare rig has no plane manager; add one and then leave it alone. No
        // requestedDetectionMode and no planePrefab, so it runs on AR Foundation's own
        // defaults and detected planes are invisible.
        _planes = origin.GetComponent<ARPlaneManager>();
        if (_planes == null) _planes = origin.gameObject.AddComponent<ARPlaneManager>();
        _planes.enabled = true;

        // Only NOW start the session — after the plane manager exists.
        //
        // The session picks its ARCore configuration from the features the enabled
        // managers request. Start it first and it configures itself with no plane
        // feature, and attaching the manager a moment later does not re-request it —
        // the camera runs but nothing is ever detected.
        //
        // This is the second-entry bug: the ordering used to differ by accident. On the
        // first AR entry ARSession.state is None, so the availability check above yields
        // for a frame or more and the manager gets attached while the session is still
        // coming up. On every later entry the previous session left the state at Ready,
        // so ARSession.Initialize() reaches StartSubsystem() with no yield at all and the
        // session started synchronously, before any manager existed. Hence nothing being
        // found from the second AR scene onward. Resolving availability before enabling
        // makes the start synchronous — and therefore identical — on every entry.
        var session = FindAnyObjectByType<ARSession>(FindObjectsInactive.Include);
        if (session != null)
        {
            // Kept disabled in the scene so it can't grab the camera (and prompt) before
            // we've cleared permission. On iOS this is where ARKit shows its own prompt,
            // so it stays a single, controlled request.
            session.enabled = true;

            // Then throw the previous experience's world away. This is NOT tidiness, and
            // it is not one of the settings that got reverted to defaults — leave it out
            // and the second AR entry is visibly broken.
            //
            // The session belongs to the XR loader, not to this scene, so it survives the
            // scene change with its map and trackables intact. Without a reset the plane
            // manager re-reports the previous session's planes the moment we subscribe, so
            // OnPlanesChanged fires on the first frame and seats the King at coordinates
            // from the last experience — before the camera has looked at anything. ARCore
            // then re-localises against the room actually in front of the device, the world
            // shifts underneath him, and he appears to jump to a real surface.
            session.Reset();
        }

        _planes.trackablesChanged.AddListener(OnPlanesChanged);
#endif
    }

#if UNITY_EDITOR
    // Editor-only simulated floor placement (no real AR on PC).
    const float EditorFloorDelay = 1.2f;     // brief "searching" beat before placing
    const float EditorFloorDistance = 2.2f;  // metres in front of the camera
    const float EditorFloorDrop = 1.0f;      // metres below the camera (a "floor")

    /// <summary>A pretend floor point a couple of metres ahead of the camera and a bit
    /// below it, so the placed character is visible in the Game view.</summary>
    Vector3 FloorInFrontOfCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return new Vector3(0f, 0f, EditorFloorDistance);
        Vector3 forward = cam.transform.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude < 1e-4f ? Vector3.forward : forward.normalized;
        Vector3 pos = cam.transform.position + forward * EditorFloorDistance;
        pos.y = cam.transform.position.y - EditorFloorDrop;
        return pos;
    }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>Shows the Android camera-permission dialog and waits for the answer.</summary>
    System.Collections.IEnumerator RequestAndroidCameraPermission()
    {
        bool answered = false;
        var callbacks = new UnityEngine.Android.PermissionCallbacks();
        callbacks.PermissionGranted += _ => answered = true;
        callbacks.PermissionDenied += _ => answered = true;
        callbacks.PermissionDeniedAndDontAskAgain += _ => answered = true;
        UnityEngine.Android.Permission.RequestUserPermission(
            UnityEngine.Android.Permission.Camera, callbacks);

        // Guard in case no callback ever fires (e.g. the user backgrounds the app).
        float waited = 0f;
        while (!answered && waited < 30f) { waited += Time.unscaledDeltaTime; yield return null; }
    }
#endif

    void OnDestroy()
    {
        if (_planes != null) _planes.trackablesChanged.RemoveListener(OnPlanesChanged);
    }

    void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
    {
        if (_placed) return;
        // A floor can surface in either list depending on the device/provider.
        if (TryFloor(args.added, out var plane) || TryFloor(args.updated, out plane))
            PlaceOn(plane);
    }

    static bool TryFloor(System.Collections.Generic.IReadOnlyList<ARPlane> planes, out ARPlane floor)
    {
        for (int i = 0; i < planes.Count; i++)
        {
            if (planes[i].alignment == PlaneAlignment.HorizontalUp) { floor = planes[i]; return true; }
        }
        floor = null;
        return false;
    }

    // Only place the King when the floor is close and ahead, so he arrives right in
    // front of the player rather than across the room. We keep scanning until a floor
    // point within this distance (measured along the camera's horizontal forward) shows up.
    const float MaxPlaceDistance = 3.0f; // metres in front of the camera (2–3 m at most)

    // Only ever seat the King on the actual ground — never a table, seat or counter. On
    // devices that classify planes (ARKit, ARCore Depth) we trust a Floor tag and reject
    // furniture outright; where no classification is available we fall back to how far the
    // surface sits below the phone — a floor is well below it, a raised surface only a little.
    const float MinFloorDropBelowCamera = 0.9f; // metres the surface must sit below the camera

    void PlaceOn(ARPlane plane)
    {
        if (_placed) return;
        if (!IsGroundFloor(plane)) return;                  // a table/raised surface — keep scanning
        if (!WithinReach(plane.transform.position)) return; // too far / not in front — keep scanning
        PlaceAt(plane.transform.position);

        // We've claimed our spot — stop hunting for more planes and hide the ones already
        // found so the King has a clean stage.
        if (_planes != null)
        {
            _planes.requestedDetectionMode = PlaneDetectionMode.None;
            _planes.trackablesChanged.RemoveListener(OnPlanesChanged);
            foreach (var p in _planes.trackables) p.gameObject.SetActive(false);
        }
    }

    /// <summary>True only if <paramref name="plane"/> is real ground, not a raised surface
    /// like a table, seat or counter. Prefers the device's own plane classification when it
    /// has one (accept <c>Floor</c>, reject furniture); otherwise falls back to how far the
    /// surface sits below the camera — a floor is well below the phone, a table only a little.</summary>
    static bool IsGroundFloor(ARPlane plane)
    {
        if (plane.alignment != PlaneAlignment.HorizontalUp) return false;

        var c = plane.classifications;
        if ((c & PlaneClassifications.Floor) != 0) return true; // device says it's the floor

        // Any other explicit horizontal classification is furniture/other — not the ground.
        const PlaneClassifications raised =
            PlaneClassifications.Table | PlaneClassifications.Seat | PlaneClassifications.Couch |
            PlaneClassifications.Ceiling | PlaneClassifications.Other;
        if ((c & raised) != 0) return false;

        // No usable classification — use the drop below the camera to tell a floor from a table.
        Camera cam = Camera.main;
        if (cam == null) return true; // no camera to gate against — don't block placement
        float drop = cam.transform.position.y - plane.center.y;
        return drop >= MinFloorDropBelowCamera;
    }

    /// <summary>True if the floor point is in front of the camera and no further than
    /// <see cref="MaxPlaceDistance"/> away, measured along the camera's horizontal forward
    /// (ignoring the height drop to the floor).</summary>
    static bool WithinReach(Vector3 floorPosition)
    {
        Camera cam = Camera.main;
        if (cam == null) return true; // no camera to gate against — don't block placement

        Vector3 forward = cam.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) return true; // camera looking straight up/down
        forward.Normalize();

        Vector3 toFloor = floorPosition - cam.transform.position;
        toFloor.y = 0f;
        float distanceInFront = Vector3.Dot(toFloor, forward);
        return distanceInFront > 0f && distanceInFront <= MaxPlaceDistance;
    }

    /// <summary>Spawn + seat the character at a floor point and open the chat. Shared by the
    /// real plane-detection path and the editor's simulated floor.</summary>
    // Portal emergence.
    const float PortalBackDistance = 0.6f;  // how far behind the king the portal sits
    const float WalkOutSeconds = 2.2f;      // time to step out of the portal
    const float PortalLingerSeconds = 1.2f; // portal stays this long after he's through

    void PlaceAt(Vector3 floorPosition)
    {
        if (_placed) return;
        _placed = true;
        HideMessage();

        // Portal experience: stand an oval video gateway on the floor instead of the
        // King — same placement, different content.
        if (ArSession.Experience == ArExperience.Portal)
        {
            ARPortalVideoView.Create(floorPosition, ArSession.PortalVideoUrl);
            return;
        }

        _model = BuildModel();
        SeatOnPlane(_model, floorPosition); // final scale / facing / seated position

        StartCoroutine(EmergeThenChat());
    }

    /// <summary>Open a portal behind the placed character and walk it out toward the player,
    /// then fade the portal and open the chat — so the King arrives rather than popping in.</summary>
    System.Collections.IEnumerator EmergeThenChat()
    {
        Vector3 finalPos = _model.transform.position;

        // The portal sits a little behind the character (away from the camera); he steps
        // forward out of it toward the viewer.
        Camera cam = Camera.main;
        Vector3 away = cam != null ? finalPos - cam.transform.position : -_model.transform.forward;
        away.y = 0f;
        away = away.sqrMagnitude < 1e-4f ? -_model.transform.forward : away.normalized;
        Vector3 portalBase = finalPos + away * PortalBackDistance;
        Vector3 portalCenter = portalBase + Vector3.up * (TargetHeightMeters * 0.5f);

        var portal = ARPortal.Create(portalCenter, _model.transform.rotation, TargetHeightMeters * 0.5f);

        // Start him at the portal and ease him out to his spot (he idles — no walk clip yet).
        _model.transform.position = portalBase;
        float t = 0f;
        while (t < WalkOutSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / WalkOutSeconds));
            _model.transform.position = Vector3.Lerp(portalBase, finalPos, k);
            yield return null;
        }
        _model.transform.position = finalPos;

        // He's through — fade the portal out, then open the chat. The King's animator is
        // handed over so it gestures (Dismiss/Pointing) as it replies.
        portal.FadeOutAndDestroy(PortalLingerSeconds);
        ChatWindow.Create(_kingAnimator);
    }

    // The placed character: an animated King loaded from Resources, scaled to roughly
    // this height and seated on the floor. Falls back to a cube if the asset is missing.
    const string KingPrefabResource = "CH2_Edinburgh_LOD1"; // Assets/Characters/King/Resources/ (the FBX model)
    const string KingControllerResource = "EdinburghKingAnim";
    const string KingModelResource = "CH2_Edinburgh_LOD1";  // same FBX carries the (generic) avatar
    const float TargetHeightMeters = 1.8f; // a person-sized king (~1.8 m tall)
    // The model's forward may not be Unity's +Z; set to 180 if the King ends up facing
    // away from the player.
    const float FacingYawOffset = 0f;

    GameObject BuildModel()
    {
        var prefab = Resources.Load<GameObject>(KingPrefabResource);
        if (prefab == null)
        {
            Debug.LogWarning($"[AR] King prefab '{KingPrefabResource}' not found in Resources; using placeholder cube.");
            return BuildPlaceholderCube();
        }

        var go = Instantiate(prefab);
        go.name = "KingModel";

        // Drive it with the Edinburgh King animator controller (Idle default + gesture
        // states). The FBX is a Generic (mixamorig) rig that carries its own avatar, so the
        // instantiated model's Animator usually already has it; pull it off the FBX import as
        // a fallback if not.
        _kingAnimator = go.GetComponentInChildren<Animator>();
        if (_kingAnimator != null)
        {
            if (_kingAnimator.avatar == null)
            {
                var model = Resources.Load<GameObject>(KingModelResource);
                var src = model != null ? model.GetComponentInChildren<Animator>() : null;
                if (src != null && src.avatar != null) _kingAnimator.avatar = src.avatar;
            }
            var controller = Resources.Load<RuntimeAnimatorController>(KingControllerResource);
            if (controller != null) _kingAnimator.runtimeAnimatorController = controller;
            _kingAnimator.applyRootMotion = false;
        }

        // The King's materials are converted to URP ahead of time (Tools ▸ Tales Tensor ▸
        // Convert King Materials to URP), so nothing to do at runtime here.
        return go;
    }

    /// <summary>Scale the model to ~<see cref="TargetHeightMeters"/> tall, seat its lowest
    /// point on the floor point, and yaw it to face the camera.</summary>
    void SeatOnPlane(GameObject model, Vector3 floorPosition)
    {
        var tf = model.transform;
        tf.rotation = Quaternion.identity;
        tf.localScale = Vector3.one;

        // Scale by measured height so it works regardless of the model's import units.
        Bounds raw = ComputeBounds(model);
        float scale = raw.size.y > 1e-4f ? TargetHeightMeters / raw.size.y : 1f;
        tf.localScale = Vector3.one * scale;
        // If he still looks wrong-sized, this reveals whether the measured height is bad (e.g.
        // bounds computed in a collapsed bind pose) versus the scale factor not applying.
        Debug.Log($"[AR] King raw height={raw.size.y:0.###}m, scale={scale:0.###}, target={TargetHeightMeters}m");

        tf.rotation = FaceCameraYaw(floorPosition);
        tf.position = floorPosition;

        // Lift so the lowest point of the scaled + rotated model rests on the surface.
        Bounds seated = ComputeBounds(model);
        tf.position += new Vector3(0f, floorPosition.y - seated.min.y, 0f);
    }

    static Bounds ComputeBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    static Quaternion FaceCameraYaw(Vector3 atPosition)
    {
        Camera cam = Camera.main;
        if (cam == null) return Quaternion.identity;
        Vector3 toCam = cam.transform.position - atPosition;
        toCam.y = 0f;
        if (toCam.sqrMagnitude < 1e-4f) return Quaternion.identity;
        return Quaternion.LookRotation(toCam.normalized, Vector3.up) * Quaternion.Euler(0f, FacingYawOffset, 0f);
    }

    GameObject BuildPlaceholderCube()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "PlacedModel";
        go.transform.localScale = Vector3.one * ModelSize;

        // Primitives ship with the built-in material, which renders magenta under URP;
        // give it a URP-lit material so it's visible.
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        go.GetComponent<Renderer>().sharedMaterial =
            new Material(shader) { color = new Color(0.24f, 0.61f, 0.94f) };
        return go;
    }

    // --- on-screen prompt ---

    void BuildMessage()
    {
        var canvasGo = new GameObject("ARFloorPlacerCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _ui = canvasGo;

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150; // below the HUD banner (200), above the world
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        // Centered pill so the prompt reads over the camera feed. Hidden until the
        // quest-name intro has played.
        var panel = UiFactory.Panel("Backdrop", canvasGo.transform, new Color(0f, 0f, 0f, 0.55f));
        UiFactory.Anchor(panel.rectTransform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 0f), new Vector2(760f, 150f));
        panel.raycastTarget = false;
        _messagePanel = panel.gameObject;

        _message = UiFactory.Text("Message", panel.transform, ScanPrompt,
            38f, BinancePalette.TextPrimary, TextAlignmentOptions.Center);
        UiFactory.Stretch(_message.rectTransform, 24f);
        _messagePanel.SetActive(false);

        // Quest-name intro: a large, centred title that fades in then out before the
        // floor prompt.
        var introRt = UiFactory.Rect("QuestIntro", canvasGo.transform);
        UiFactory.Anchor(introRt,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 0f), new Vector2(980f, 460f));
        _introGroup = introRt.gameObject.AddComponent<CanvasGroup>();
        _introGroup.alpha = 0f;

        string quest = ArSession.Experience == ArExperience.Portal
            ? "Portal"
            : (string.IsNullOrEmpty(ArSession.QuestName) ? "AR Experience" : ArSession.QuestName);
        var introLabel = UiFactory.Text("QuestIntroLabel", introRt, quest, 78f,
            BinancePalette.TextPrimary, TextAlignmentOptions.Center);
        introLabel.fontStyle = FontStyles.Bold;
        UiFactory.Stretch(introLabel.rectTransform, 24f);
    }

    // Quest-name intro timing.
    const float IntroFadeIn = 0.5f;
    const float IntroHold = 2.0f;
    const float IntroFadeOut = 0.6f;

    System.Collections.IEnumerator PlayQuestIntro()
    {
        if (_introGroup == null) yield break;
        yield return FadeGroup(_introGroup, 0f, 1f, IntroFadeIn);
        yield return new WaitForSeconds(IntroHold);
        yield return FadeGroup(_introGroup, 1f, 0f, IntroFadeOut);
        _introGroup.gameObject.SetActive(false);
    }

    static System.Collections.IEnumerator FadeGroup(CanvasGroup group, float from, float to, float dur)
    {
        group.alpha = from;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur));
            yield return null;
        }
        group.alpha = to;
    }

    /// <summary>Show the floor-search pill (revealing it after the intro) with new text.</summary>
    void SetMessage(string text)
    {
        if (_messagePanel != null) _messagePanel.SetActive(true);
        if (_message != null) _message.text = text;
    }

    void HideMessage() { if (_ui != null) _ui.SetActive(false); }
}

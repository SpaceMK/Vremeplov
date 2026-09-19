using System.Collections;
using TalesTensor.Map;  // UiFactory, ProceduralIcons
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// The AR Portal experience: a person-sized oval gateway that stands on the detected
/// floor, facing the player. The oval shows a streamed video as if you were looking
/// through it (the ellipse mesh crops the video to the portal shape), with a ring of
/// glowing particles around its rim for the portal feel.
///
/// While the video buffers, a loading bar fills inside the oval over a dark interior;
/// once the stream is ready it reveals the video and fades the bar out.
///
/// Built entirely in code (no prefab), matching the rest of the AR scene's self-assembly.
/// Spawned by <see cref="ARFloorPlacer"/> when the player entered from a Portal pin.
/// </summary>
[DisallowMultipleComponent]
public class ARPortalVideoView : MonoBehaviour
{
    // Roughly a doorway: a touch under typical head height, a comfortable shoulder width.
    const float PortalHeight = 1.9f;
    const float PortalWidth = 1.0f;
    const int EllipseSegments = 72;
    const int FallbackTexSize = 1024;   // until the video reports its native size
    const float CreepSeconds = 6f;      // how long the buffering bar takes to ease toward full

    static readonly Color PortalAccent = new Color32(0x9B, 0x5D, 0xE5, 0xFF); // matches the pin
    static readonly Color InteriorColor = new Color(0.04f, 0.02f, 0.10f, 1f); // dark "inside" while loading

    Camera _cam; // cached so the per-frame billboard never calls Camera.main
    VideoPlayer _video;
    RenderTexture _rt;
    Material _surfaceMat;
    GameObject _loadingGo;
    CanvasGroup _loadingGroup;
    Image _fill;
    TMP_Text _label;

    bool _prepared;
    bool _revealed;
    float _elapsed;

    public static ARPortalVideoView Create(Vector3 floorPosition, string videoUrl)
    {
        var go = new GameObject("ARPortalVideoView");
        var view = go.AddComponent<ARPortalVideoView>();
        view.Build(floorPosition, videoUrl);
        return view;
    }

    void Build(Vector3 floorPosition, string videoUrl)
    {
        float halfW = PortalWidth * 0.5f;
        float halfH = PortalHeight * 0.5f;

        // Stand the oval upright on the floor, centred at half its height, facing the player.
        Vector3 center = floorPosition + Vector3.up * halfH;
        Quaternion facing = FaceCameraYaw(center);
        transform.SetPositionAndRotation(center, facing);

        BuildSurface(halfW, halfH);
        BuildLoading();
        BuildRim(center, facing, halfW, halfH);
        SetupVideo(videoUrl);

        StartCoroutine(ScaleIn());
    }

    // --- the oval video surface ---

    void BuildSurface(float halfW, float halfH)
    {
        var surface = new GameObject("PortalSurface", typeof(MeshFilter), typeof(MeshRenderer));
        surface.transform.SetParent(transform, false);
        surface.GetComponent<MeshFilter>().sharedMesh = BuildEllipseMesh(halfW, halfH, EllipseSegments);

        _surfaceMat = NewUnlitMaterial();
        SetMatColor(_surfaceMat, InteriorColor); // dark interior until the video is revealed

        var mr = surface.GetComponent<MeshRenderer>();
        mr.sharedMaterial = _surfaceMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    /// <summary>A filled ellipse mesh in the local XY plane (normal +Z). UVs map the disc
    /// to [0,1] so the video is shown through the oval, cropped to its shape.</summary>
    static Mesh BuildEllipseMesh(float halfW, float halfH, int seg)
    {
        var verts = new Vector3[seg + 1];
        var uvs = new Vector2[seg + 1];
        var tris = new int[seg * 3];

        verts[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < seg; i++)
        {
            float a = (float)i / seg * Mathf.PI * 2f;
            float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
            verts[i + 1] = new Vector3(cx * halfW, cy * halfH, 0f);
            uvs[i + 1] = new Vector2(0.5f + cx * 0.5f, 0.5f + cy * 0.5f);
        }
        for (int i = 0; i < seg; i++)
        {
            tris[i * 3] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 1) % seg + 1;
        }

        var mesh = new Mesh { name = "PortalEllipse" };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // --- glowing particle rim ---

    void BuildRim(Vector3 center, Quaternion facing, float halfW, float halfH)
    {
        // Reuse the existing portal VFX: a unit ring of particles, scaled non-uniformly
        // so it hugs the oval's edge. Kept alive for the duration (no FadeOutAndDestroy).
        var rim = ARPortal.Create(center, facing, 1f);
        rim.transform.SetParent(transform, true); // keep its world pose, then make it elliptical
        rim.transform.localScale = new Vector3(halfW, halfH, 1f);
    }

    // --- loading bar shown inside the oval while the stream buffers ---

    void BuildLoading()
    {
        _loadingGo = new GameObject("PortalLoading",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        _loadingGo.transform.SetParent(transform, false);

        var canvas = _loadingGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        _loadingGroup = _loadingGo.GetComponent<CanvasGroup>();

        var crt = (RectTransform)_loadingGo.transform;
        crt.sizeDelta = new Vector2(520f, 240f);
        // Scale the 520px-wide canvas so the bar spans ~62% of the portal width, and sit it
        // just in front of the oval surface (toward the camera, i.e. local -Z since +Z faces
        // away) so it reads clearly. Identity rotation inherits the portal's camera-facing yaw.
        crt.localScale = Vector3.one * (PortalWidth * 0.62f / 520f);
        crt.localPosition = new Vector3(0f, 0f, -0.02f);
        crt.localRotation = Quaternion.identity;

        _label = UiFactory.Text("Label", crt, "Loading…", 44f, Color.white, TextAlignmentOptions.Center);
        UiFactory.Anchor(_label.rectTransform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 72f), new Vector2(520f, 80f));

        var track = UiFactory.Panel("Track", crt, new Color(1f, 1f, 1f, 0.18f));
        track.raycastTarget = false;
        UiFactory.Anchor(track.rectTransform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -8f), new Vector2(460f, 34f));

        _fill = UiFactory.Panel("Fill", track.transform, PortalAccent);
        _fill.raycastTarget = false;
        _fill.type = Image.Type.Filled;
        _fill.fillMethod = Image.FillMethod.Horizontal;
        _fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _fill.fillAmount = 0f;
        UiFactory.Stretch(_fill.rectTransform, 4f);
    }

    // --- video streaming ---

    void SetupVideo(string videoUrl)
    {
        if (string.IsNullOrEmpty(videoUrl))
        {
            if (_label != null) _label.text = "No video set";
            return;
        }

        _rt = NewRenderTexture(FallbackTexSize, FallbackTexSize);

        _video = gameObject.AddComponent<VideoPlayer>();
        _video.source = VideoSource.Url;
        _video.url = videoUrl;
        _video.renderMode = VideoRenderMode.RenderTexture;
        _video.targetTexture = _rt;
        _video.isLooping = true;
        _video.playOnAwake = false;
        _video.waitForFirstFrame = true;
        _video.skipOnDrop = true;
        _video.audioOutputMode = VideoAudioOutputMode.Direct;
        _video.prepareCompleted += OnPrepared;
        _video.errorReceived += OnError;
        _video.Prepare(); // begins buffering; the loading bar runs until this completes
    }

    void OnPrepared(VideoPlayer vp)
    {
        // Re-target at the video's native resolution so it isn't squashed into a square.
        int w = (int)vp.width, h = (int)vp.height;
        if (w > 0 && h > 0)
        {
            var rt = NewRenderTexture(Mathf.Min(w, 2048), Mathf.Min(h, 2048));
            vp.targetTexture = rt;
            if (_rt != null) _rt.Release();
            _rt = rt;
        }
        _prepared = true;
        vp.Play();
    }

    void OnError(VideoPlayer vp, string message)
    {
        Debug.LogWarning($"[Portal] Video error: {message}");
        if (_label != null) _label.text = "Video unavailable";
    }

    void Update()
    {
        // Billboard: keep the portal facing the player as they walk around it. Yaw only,
        // so it stays upright on the floor and just turns in place to face the camera.
        transform.rotation = FaceCameraYaw(transform.position);

        if (_revealed)
        {
            // Video is showing — fade the loading bar out, then disable it.
            if (_loadingGroup != null && _loadingGroup.alpha > 0f)
            {
                _loadingGroup.alpha = Mathf.MoveTowards(_loadingGroup.alpha, 0f, Time.deltaTime / 0.3f);
                if (_loadingGroup.alpha <= 0f) _loadingGo.SetActive(false);
            }
            return;
        }

        if (_fill == null) return;

        _elapsed += Time.deltaTime;
        // Ease toward ~0.9 while buffering (slowing as it climbs); snap to full once ready.
        float goal = _prepared ? 1f : 0.9f * (1f - Mathf.Exp(-_elapsed / (CreepSeconds * 0.4f)));
        _fill.fillAmount = Mathf.MoveTowards(_fill.fillAmount, goal, Time.deltaTime * (_prepared ? 2.5f : 0.6f));

        if (_prepared && _fill.fillAmount >= 0.999f)
        {
            _revealed = true;
            RevealVideo();
        }
    }

    void RevealVideo()
    {
        SetMatColor(_surfaceMat, Color.white);
        SetMatTexture(_surfaceMat, _rt);
        if (_label != null) _label.text = string.Empty;
    }

    System.Collections.IEnumerator ScaleIn()
    {
        const float dur = 0.6f;
        Vector3 target = transform.localScale; // (1,1,1)
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            transform.localScale = target * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
            yield return null;
        }
        transform.localScale = target;
    }

    void OnDestroy()
    {
        if (_video != null)
        {
            _video.prepareCompleted -= OnPrepared;
            _video.errorReceived -= OnError;
        }
        if (_rt != null) { _rt.Release(); _rt = null; }
    }

    // --- helpers ---

    static RenderTexture NewRenderTexture(int w, int h)
    {
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
        {
            wrapMode = TextureWrapMode.Clamp,
        };
        rt.Create();
        return rt;
    }

    static Material NewUnlitMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        var m = new Material(shader);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f); // double-sided: visible from both faces
        return m;
    }

    static void SetMatColor(Material m, Color c)
    {
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    static void SetMatTexture(Material m, Texture t)
    {
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
        m.mainTexture = t;
    }

    /// <summary>Orient the portal so its +Z points the same way the camera looks (away from
    /// the camera). World-space UI is only readable when its forward matches the camera's view
    /// direction, and this also keeps the video the right way round rather than mirrored.
    /// Uses the cached camera, resolving it once if needed — never per frame.</summary>
    Quaternion FaceCameraYaw(Vector3 at)
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return transform.rotation; // no camera yet — hold current facing
        Vector3 viewDir = at - _cam.transform.position; // from the camera toward the portal
        viewDir.y = 0f;
        if (viewDir.sqrMagnitude < 1e-4f) return transform.rotation;
        return Quaternion.LookRotation(viewDir.normalized, Vector3.up);
    }
}

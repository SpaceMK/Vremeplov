using System;
using UnityEngine;

/// <summary>
/// Backs the player's progress up to Firestore in the background, without any
/// gameplay code having to know the cloud exists.
///
/// Listens to <see cref="PlayerSession.Saved"/> — which already fires on every XP
/// gain, energy tick, item pickup and level-up — and coalesces that firehose into
/// at most one write every <see cref="PushIntervalSeconds"/>. Movement alone awards
/// XP once per tick, so pushing on every save would mean a Firestore write per
/// second per player; debouncing is what keeps this affordable.
///
/// A lazily-created scene-independent singleton, matching <see cref="PlayerProgress"/>
/// and <see cref="EnergyController"/>, so nothing needs wiring in a scene.
/// </summary>
[DisallowMultipleComponent]
public class CloudSync : MonoBehaviour
{
    /// <summary>Minimum gap between cloud writes while playing.</summary>
    const float PushIntervalSeconds = 30f;

    /// <summary>Wait after the last change before writing, so bursts collapse into one push.</summary>
    const float QuietPeriodSeconds = 3f;

    static CloudSync _instance;

    /// <summary>The shared syncer, created on first access if it doesn't exist yet.</summary>
    public static CloudSync Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("CloudSync") { hideFlags = HideFlags.HideInHierarchy };
                _instance = go.AddComponent<CloudSync>();
            }
            return _instance;
        }
    }

    /// <summary>Raised when a push completes; true on success. For a "saved" indicator.</summary>
    public static event Action<bool> PushCompleted;

    bool _dirty;
    float _lastPushTime = -999f;
    float _dirtySince;
    bool _pushInFlight;

    /// <summary>UTC ticks of the last successful cloud write, 0 if none this session.</summary>
    public long LastPushedUtcTicks { get; private set; }

    /// <summary>
    /// Start syncing. Safe to call repeatedly; only has an effect while the player is
    /// signed in with Google.
    /// </summary>
    public static void Begin()
    {
        // Touching Instance is what creates it.
        _ = Instance;
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        PlayerSession.Saved += MarkDirty;
        AuthService.Changed += OnAuthChanged;
    }

    void OnDestroy()
    {
        PlayerSession.Saved -= MarkDirty;
        AuthService.Changed -= OnAuthChanged;
    }

    void OnAuthChanged()
    {
        // Signing out mid-session must not leave a queued write that would push the
        // next player's data under the previous player's uid.
        if (!AuthService.IsCloudBacked) _dirty = false;
    }

    void MarkDirty()
    {
        if (!AuthService.IsCloudBacked) return;
        if (!_dirty) _dirtySince = Time.unscaledTime;
        _dirty = true;
    }

    void Update()
    {
        if (!_dirty || _pushInFlight || !AuthService.IsCloudBacked) return;

        float now = Time.unscaledTime;
        if (now - _dirtySince < QuietPeriodSeconds) return;
        if (now - _lastPushTime < PushIntervalSeconds) return;

        Push();
    }

    // Backgrounding the app is the most likely moment to lose progress, so flush
    // immediately rather than waiting out the interval.
    void OnApplicationPause(bool paused) { if (paused) Flush(); }
    void OnApplicationFocus(bool focused) { if (!focused) Flush(); }
    void OnApplicationQuit() => Flush();

    /// <summary>Push right now if anything is pending, ignoring the debounce interval.</summary>
    public void Flush()
    {
        if (!_dirty || _pushInFlight || !AuthService.IsCloudBacked) return;
        Push();
    }

    async void Push()
    {
        // Capture the uid up front: if the player signs out mid-flight we must not
        // write this bundle under whatever uid is current when the await resumes.
        string uid = AuthService.Uid;
        if (string.IsNullOrEmpty(uid)) return;

        _pushInFlight = true;
        _dirty = false;
        _lastPushTime = Time.unscaledTime;

        var bundle = SaveBundle.CaptureLocal();
        bool ok = await CloudSaveService.PushAsync(uid, bundle);

        _pushInFlight = false;

        if (ok) LastPushedUtcTicks = bundle.savedUtcTicks;
        // A failed push must not be forgotten, or the change is lost until the next
        // unrelated save happens to mark things dirty again.
        else _dirty = true;

        PushCompleted?.Invoke(ok);
    }
}

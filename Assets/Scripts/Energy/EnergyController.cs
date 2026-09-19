using System;
using UnityEngine;

/// <summary>
/// Runtime owner of the player's energy. A lazily-created, scene-independent
/// singleton (<see cref="Instance"/>) so the map's pins and the energy HUD can
/// reach it without any editor wiring — touching <see cref="Instance"/> the first
/// time bootstraps a hidden <c>DontDestroyOnLoad</c> GameObject.
///
/// Energy is persisted through <see cref="UserDataStore"/> and reconciled against
/// real elapsed time on load, on resume, and once per second while running, so it
/// keeps regenerating even across app restarts (see <see cref="EnergyModel"/>).
/// </summary>
[DisallowMultipleComponent]
public class EnergyController : MonoBehaviour
{
    static EnergyController _instance;

    /// <summary>The shared controller, created on first access if it doesn't exist yet.</summary>
    public static EnergyController Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("EnergyController") { hideFlags = HideFlags.HideInHierarchy };
                _instance = go.AddComponent<EnergyController>();
            }
            return _instance;
        }
    }

    /// <summary>Raised whenever the current energy value changes.</summary>
    public event Action OnChanged;

    // The player record is owned by PlayerSession so energy, progression and
    // inventory all mutate one shared instance instead of clobbering each other.
    static UserData _user => PlayerSession.User;
    float _tickAccumulator;

    public int Current => _user?.energy ?? 0;
    public int Max => EnergyModel.Max;
    public bool IsFull => Current >= Max;

    /// <summary>Whole seconds until the next point regenerates (0 when full).</summary>
    public int SecondsUntilNext => EnergyModel.SecondsUntilNext(_user, DateTime.UtcNow);

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        // A cloud restore swaps the underlying record wholesale; re-run the energy
        // migration/regen against the new one and repaint.
        PlayerSession.Replaced += OnSessionReplaced;

        Load();
    }

    void OnDestroy()
    {
        PlayerSession.Replaced -= OnSessionReplaced;
    }

    void OnSessionReplaced()
    {
        Load();
        OnChanged?.Invoke();
    }

    void Load()
    {
        // PlayerSession guarantees a record exists; we only handle the energy-specific
        // migration here for accounts saved before energy existed.
        if (_user.energyUpdatedUtcTicks == 0)
        {
            _user.energy = EnergyModel.Starting;
            _user.energyUpdatedUtcTicks = DateTime.UtcNow.Ticks;
            PlayerSession.Save();
        }

        if (EnergyModel.Reconcile(_user, DateTime.UtcNow))
            PlayerSession.Save();
    }

    void Update()
    {
        // Cheap once-per-second reconcile so the live minute tick lands and the HUD
        // countdown stays truthful without per-frame work.
        _tickAccumulator += Time.unscaledDeltaTime;
        if (_tickAccumulator < 1f) return;
        _tickAccumulator = 0f;

        int before = _user.energy;
        if (EnergyModel.Reconcile(_user, DateTime.UtcNow))
        {
            PlayerSession.Save();
            if (_user.energy != before) OnChanged?.Invoke();
        }
    }

    void OnApplicationPause(bool paused)
    {
        if (!paused) ReconcileNow();
    }

    void OnApplicationFocus(bool focused)
    {
        if (focused) ReconcileNow();
    }

    void ReconcileNow()
    {
        if (_user == null) return;
        int before = _user.energy;
        if (EnergyModel.Reconcile(_user, DateTime.UtcNow))
        {
            PlayerSession.Save();
            if (_user.energy != before) OnChanged?.Invoke();
        }
    }

    /// <summary>
    /// Spend <paramref name="amount"/> energy if the player can afford it. Persists
    /// and notifies on success. Returns false (no change) if there isn't enough.
    /// </summary>
    public bool TrySpend(int amount)
    {
        if (amount <= 0) return true;
        // Make sure regen is up to date before judging affordability.
        ReconcileNow();
        if (_user.energy < amount) return false;

        // Spending below the cap restarts the regen clock from now.
        bool wasFull = _user.energy >= EnergyModel.Max;
        _user.energy -= amount;
        if (wasFull) _user.energyUpdatedUtcTicks = DateTime.UtcNow.Ticks;

        PlayerSession.Save();
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Grant <paramref name="amount"/> energy, clamped to the cap (e.g. a Chrono
    /// Booster). Persists and notifies if anything changed. Returns how much was
    /// actually added after clamping (0 if already full).
    /// </summary>
    public int Gain(int amount)
    {
        if (amount <= 0) return 0;
        // Reconcile first so the cap is judged against the true current value.
        ReconcileNow();
        int before = _user.energy;
        _user.energy = Mathf.Min(_user.energy + amount, EnergyModel.Max);
        int gained = _user.energy - before;
        if (gained > 0)
        {
            PlayerSession.Save();
            OnChanged?.Invoke();
        }
        return gained;
    }
}

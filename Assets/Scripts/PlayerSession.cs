using UnityEngine;

/// <summary>
/// The single live <see cref="UserData"/> for the running app. Every system that
/// reads or mutates the player record — energy, progression, inventory — goes
/// through here so they all share one in-memory instance and never clobber each
/// other's fields by loading/saving separate copies.
///
/// Loaded lazily on first access (seeding a guest if none exists) and written back
/// through <see cref="UserDataStore"/> on <see cref="Save"/>.
/// </summary>
public static class PlayerSession
{
    static UserData _user;

    /// <summary>
    /// Raised after every <see cref="Save"/>. <see cref="CloudSync"/> listens so
    /// cloud backup happens without every caller having to know it exists.
    /// </summary>
    public static event System.Action Saved;

    /// <summary>Raised when the live record is swapped wholesale (a cloud restore).</summary>
    public static event System.Action Replaced;

    /// <summary>The shared player record, loaded (or seeded) on first access.</summary>
    public static UserData User
    {
        get
        {
            if (_user == null) Load();
            return _user;
        }
    }

    static void Load()
    {
        _user = UserDataStore.Load();
        if (_user == null)
        {
            // No saved player yet (e.g. a gameplay scene was entered directly). Seed
            // one so everything works; the login flow reuses the same record.
            _user = UserData.NewGuest();
            UserDataStore.Save(_user);
        }

        // Records created before progression existed default level to 0 — treat that
        // as a fresh level 1 so the bars and level-ups behave.
        if (_user.level < LevelModel.StartLevel) _user.level = LevelModel.StartLevel;
        if (_user.inventory == null) _user.inventory = new System.Collections.Generic.List<InventoryStack>();
    }

    /// <summary>Persist the shared record.</summary>
    public static void Save()
    {
        UserDataStore.Save(User);
        Saved?.Invoke();
    }

    /// <summary>
    /// Replace the live record — used when a cloud save is restored, or when signing
    /// in/out switches which player this device is running as. Writes through to local
    /// storage immediately so a crash before the next save can't lose the swap.
    /// </summary>
    public static void Adopt(UserData user)
    {
        if (user == null) return;

        _user = user;
        if (_user.level < LevelModel.StartLevel) _user.level = LevelModel.StartLevel;
        if (_user.inventory == null) _user.inventory = new System.Collections.Generic.List<InventoryStack>();

        UserDataStore.Save(_user);
        Replaced?.Invoke();
        Saved?.Invoke();
    }

    /// <summary>Drop the in-memory record so the next access re-reads local storage.</summary>
    public static void Reset()
    {
        _user = null;
        Replaced?.Invoke();
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime owner of the player's progression (level + experience) and inventory.
/// A lazily-created, scene-independent singleton (<see cref="Instance"/>) in the same
/// spirit as <see cref="EnergyController"/>, so the profile UI, the movement XP
/// tracker and the pin claim flow can all reach it with no editor wiring.
///
/// All state lives on the shared <see cref="PlayerSession.User"/> record and is
/// persisted through it, so energy / XP / inventory never clobber one another.
/// </summary>
[DisallowMultipleComponent]
public class PlayerProgress : MonoBehaviour
{
    static PlayerProgress _instance;

    /// <summary>The shared controller, created on first access if it doesn't exist yet.</summary>
    public static PlayerProgress Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("PlayerProgress") { hideFlags = HideFlags.HideInHierarchy };
                _instance = go.AddComponent<PlayerProgress>();
            }
            return _instance;
        }
    }

    /// <summary>Raised whenever level, XP or inventory changes (for the profile UI to refresh).</summary>
    public event Action OnChanged;
    /// <summary>Raised when the player reaches a new level; carries the new level number.</summary>
    public event Action<int> OnLevelUp;

    static UserData User => PlayerSession.User;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        // A cloud restore swaps the underlying record wholesale, so anything showing
        // level / XP / inventory needs to repaint from the new one.
        PlayerSession.Replaced += OnSessionReplaced;

        EnsureSeeded();
    }

    void OnDestroy()
    {
        PlayerSession.Replaced -= OnSessionReplaced;
    }

    void OnSessionReplaced()
    {
        EnsureSeeded();
        OnChanged?.Invoke();
    }

    // --- progression ---

    public int Level => User.level;
    public int Xp => User.xp;
    public int XpToNext => LevelModel.XpToNext(User.level);
    public bool IsMaxLevel => LevelModel.IsMaxLevel(User.level);
    public int MaxLevel => LevelModel.MaxLevel;
    /// <summary>0..1 fill of the current level's XP bar.</summary>
    public float LevelProgress => LevelModel.Progress(User.level, User.xp);

    /// <summary>
    /// Award <paramref name="amount"/> experience, rolling over into as many levels as
    /// it earns (capped at <see cref="LevelModel.MaxLevel"/>). Fires <see cref="OnChanged"/>
    /// always and <see cref="OnLevelUp"/> once if at least one level was gained.
    /// </summary>
    public void AddXp(int amount)
    {
        if (amount <= 0 || IsMaxLevel) return;

        User.xp += amount;

        int levelsGained = 0;
        while (!LevelModel.IsMaxLevel(User.level))
        {
            int need = LevelModel.XpToNext(User.level);
            if (need <= 0 || User.xp < need) break;
            User.xp -= need;
            User.level++;
            levelsGained++;
        }
        if (LevelModel.IsMaxLevel(User.level)) User.xp = 0; // no bar to fill past the cap

        PlayerSession.Save();
        OnChanged?.Invoke();
        if (levelsGained > 0) OnLevelUp?.Invoke(User.level);
    }

    // --- inventory ---

    /// <summary>Live view of the owned stacks (Regular + Premium together).</summary>
    public IReadOnlyList<InventoryStack> Inventory => User.inventory;

    /// <summary>How many of <paramref name="id"/> the player holds.</summary>
    public int CountOf(string id)
    {
        var stack = FindStack(id);
        return stack?.count ?? 0;
    }

    /// <summary>Add <paramref name="count"/> of an item, stacking onto any existing entry.</summary>
    public void AddItem(string id, int count = 1)
    {
        if (count <= 0 || ItemCatalog.Get(id) == null) return;

        var stack = FindStack(id);
        if (stack == null)
        {
            stack = new InventoryStack(id, 0);
            User.inventory.Add(stack);
        }
        stack.count += count;

        PlayerSession.Save();
        OnChanged?.Invoke();
    }

    /// <summary>Outcome of trying to use an item, so the UI can show the right message.</summary>
    public enum UseResult { Used, Empty, NotUsable, NoEffect }

    /// <summary>
    /// Apply an item's effect and consume one from its stack. A booster that would
    /// overflow full energy is left untouched (<see cref="UseResult.NoEffect"/>) so the
    /// player doesn't waste it.
    /// </summary>
    public UseResult TryUseItem(string id)
    {
        var def = ItemCatalog.Get(id);
        if (def == null || !def.Usable) return UseResult.NotUsable;
        if (CountOf(id) <= 0) return UseResult.Empty;

        if (def.EnergyGain > 0)
        {
            int gained = EnergyController.Instance.Gain(def.EnergyGain);
            if (gained <= 0) return UseResult.NoEffect; // already full — keep the item
        }

        RemoveItem(id, 1);
        PlayerSession.Save();
        OnChanged?.Invoke();
        return UseResult.Used;
    }

    void RemoveItem(string id, int count)
    {
        var stack = FindStack(id);
        if (stack == null) return;
        stack.count -= count;
        if (stack.count <= 0) User.inventory.Remove(stack);
    }

    InventoryStack FindStack(string id)
    {
        var inv = User.inventory;
        for (int i = 0; i < inv.Count; i++)
            if (inv[i] != null && inv[i].itemId == id) return inv[i];
        return null;
    }

    /// <summary>Grant the one-time starter inventory so both shelves have something to show.</summary>
    void EnsureSeeded()
    {
        if (User.inventorySeeded) return;
        User.inventorySeeded = true;
        AddItem(ItemCatalog.ChronoBooster, 2);
        AddItem(ItemCatalog.CoffeeVoucher, 1);
        PlayerSession.Save();
    }
}

using System;
using System.Collections.Generic;

/// <summary>Which shelf of the inventory an item lives on.</summary>
public enum ItemCategory
{
    /// <summary>An in-game consumable (e.g. a Chrono Booster).</summary>
    Regular,
    /// <summary>A real-world reward such as a redeemable voucher.</summary>
    Premium,
}

/// <summary>Which procedural glyph represents an item in the UI (resolved in the view layer).</summary>
public enum ItemIcon { Booster, Voucher, Scroll }

/// <summary>
/// Static description of one kind of item. Stacks in the inventory reference these
/// by <see cref="Id"/>; the live owned counts live on <see cref="UserData.inventory"/>.
/// </summary>
public class ItemDefinition
{
    public readonly string Id;
    public readonly string Name;
    public readonly string Description;
    public readonly ItemCategory Category;
    /// <summary>True if the item shows a "Use" button that consumes one from the stack.</summary>
    public readonly bool Usable;
    /// <summary>Energy restored when used (0 if the item doesn't grant energy).</summary>
    public readonly int EnergyGain;
    public readonly ItemIcon Icon;

    public ItemDefinition(string id, string name, string description, ItemCategory category,
        bool usable, int energyGain, ItemIcon icon)
    {
        Id = id;
        Name = name;
        Description = description;
        Category = category;
        Usable = usable;
        EnergyGain = energyGain;
        Icon = icon;
    }
}

/// <summary>
/// The known item kinds and their static data. Greenfield for now — two items —
/// but shaped so a backend-driven catalog could replace <see cref="Get"/> later.
/// </summary>
public static class ItemCatalog
{
    public const string ChronoBooster = "chrono_booster";
    public const string CoffeeVoucher = "coffee_voucher";
    public const string KingsScroll = "kings_scroll";
    public const string CupcakeVoucher = "cupcake_voucher";
    public const string IceCreamVoucher = "icecream_voucher";

    static readonly Dictionary<string, ItemDefinition> Items = new Dictionary<string, ItemDefinition>
    {
        [KingsScroll] = new ItemDefinition(
            KingsScroll, "King's Scroll",
            "A sealed letter from His Majesty. Deliver it to the keeper of its quest.",
            ItemCategory.Regular, usable: false, energyGain: 0, ItemIcon.Scroll),

        [CupcakeVoucher] = new ItemDefinition(
            CupcakeVoucher, "10% Off at Awesome Cupcakes",
            "A real-world reward. Show this voucher in-store for 10% off at Awesome Cupcakes.",
            ItemCategory.Premium, usable: false, energyGain: 0, ItemIcon.Voucher),

        [ChronoBooster] = new ItemDefinition(
            ChronoBooster, "Chrono Booster",
            "Restores 10 energy instantly.",
            ItemCategory.Regular, usable: true, energyGain: 10, ItemIcon.Booster),

        [CoffeeVoucher] = new ItemDefinition(
            CoffeeVoucher, "20% Off at Awesome Coffee Shop",
            "A real-world reward. Show this voucher in-store for 20% off any drink at Awesome Coffee Shop.",
            ItemCategory.Premium, usable: false, energyGain: 0, ItemIcon.Voucher),

        [IceCreamVoucher] = new ItemDefinition(
            IceCreamVoucher, "5% Off Premium Ice Cream",
            "A real-world reward. Show this voucher in-store for 5% off premium ice cream.",
            ItemCategory.Premium, usable: false, energyGain: 0, ItemIcon.Voucher),
    };

    /// <summary>Look up an item definition by id, or null if unknown.</summary>
    public static ItemDefinition Get(string id) =>
        id != null && Items.TryGetValue(id, out var def) ? def : null;
}

/// <summary>
/// One stacked entry in the player's inventory: an item id and how many they hold.
/// Kept <c>[Serializable]</c> with plain fields so Unity's JsonUtility can persist
/// it as part of <see cref="UserData"/>.
/// </summary>
[Serializable]
public class InventoryStack
{
    public string itemId;
    public int count;

    public InventoryStack() { }
    public InventoryStack(string itemId, int count)
    {
        this.itemId = itemId;
        this.count = count;
    }
}

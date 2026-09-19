using System;

/// <summary>
/// Pure, side-effect-free rules for the player's energy currency. Energy gates
/// entering experiences from the map. It regenerates over real time — including
/// while the app is closed — which is modelled purely as timestamp arithmetic
/// applied whenever the game next loads or resumes (see <see cref="Reconcile"/>),
/// so no background process is required.
/// </summary>
public static class EnergyModel
{
    /// <summary>Energy never exceeds this.</summary>
    public const int Max = 100;
    /// <summary>What a brand-new player starts with.</summary>
    public const int Starting = 25;
    /// <summary>Real seconds to regenerate one point of energy (1 per minute).</summary>
    public const int RegenSeconds = 60;

    /// <summary>
    /// Credit any whole regen periods that have elapsed since the energy was last
    /// reconciled, clamped to <see cref="Max"/>, and advance the stored timestamp by
    /// exactly the time consumed so the partial period carries forward (keeping the
    /// "next energy" countdown accurate). When already full, the timestamp snaps to
    /// <paramref name="nowUtc"/> so a full player doesn't bank time.
    /// Mutates <paramref name="u"/> in place. Returns true if anything changed.
    /// </summary>
    public static bool Reconcile(UserData u, DateTime nowUtc)
    {
        if (u == null) return false;

        long nowTicks = nowUtc.Ticks;

        // Guard against a missing/garbage timestamp (e.g. data saved before energy existed).
        if (u.energyUpdatedUtcTicks <= 0 || u.energyUpdatedUtcTicks > nowTicks)
        {
            u.energyUpdatedUtcTicks = nowTicks;
            return false;
        }

        if (u.energy >= Max)
        {
            bool moved = u.energyUpdatedUtcTicks != nowTicks;
            u.energyUpdatedUtcTicks = nowTicks;
            u.energy = Max;
            return moved;
        }

        double elapsedSeconds = (nowTicks - u.energyUpdatedUtcTicks) / (double)TimeSpan.TicksPerSecond;
        int periods = (int)(elapsedSeconds / RegenSeconds);
        if (periods <= 0) return false;

        int gained = Math.Min(periods, Max - u.energy);
        u.energy += gained;
        u.energyUpdatedUtcTicks += (long)gained * RegenSeconds * TimeSpan.TicksPerSecond;

        if (u.energy >= Max)
        {
            u.energy = Max;
            u.energyUpdatedUtcTicks = nowTicks; // full -> stop banking time
        }
        return true;
    }

    /// <summary>
    /// Whole seconds until the next point regenerates, for display. Returns 0 when
    /// energy is already full. Call <see cref="Reconcile"/> first so the timestamp
    /// reflects credited periods.
    /// </summary>
    public static int SecondsUntilNext(UserData u, DateTime nowUtc)
    {
        if (u == null || u.energy >= Max) return 0;

        double elapsed = (nowUtc.Ticks - u.energyUpdatedUtcTicks) / (double)TimeSpan.TicksPerSecond;
        if (elapsed < 0) elapsed = 0;
        double remaining = RegenSeconds - (elapsed % RegenSeconds);
        return (int)Math.Ceiling(remaining);
    }
}

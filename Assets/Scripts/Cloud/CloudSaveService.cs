using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Reads and writes a player's <see cref="SaveBundle"/> to Cloud Firestore at
/// <c>users/{uid}</c>.
///
/// The save itself travels as a JSON string in the <c>payload</c> field rather than
/// as mapped Firestore fields. That keeps <see cref="UserData"/> free of Firestore
/// attributes, means adding a field to the save never needs a schema change here,
/// and sidesteps Firestore's inability to represent Unity's parallel-list types.
/// The handful of denormalised fields alongside it exist so the document is
/// readable in the Firebase console and queryable later — they are never read back.
/// </summary>
public static class CloudSaveService
{
    const string Collection = "users";

    // Field names, kept in one place so the security rules and this file agree.
    const string FieldPayload = "payload";
    const string FieldSchema = "schemaVersion";
    const string FieldUpdatedTicks = "updatedUtcTicks";

    static bool _settingsApplied;

    /// <summary>
    /// The Firestore instance, with settings applied on first touch.
    ///
    /// Settings can only be changed before the first operation — afterwards the SDK
    /// throws — so this deliberately goes through one accessor that every call site
    /// uses, rather than an init method someone could forget to call first.
    /// </summary>
    static Firebase.Firestore.FirebaseFirestore Db
    {
        get
        {
            var db = Firebase.Firestore.FirebaseFirestore.DefaultInstance;
            if (!_settingsApplied)
            {
                _settingsApplied = true;
                try
                {
                    // On by default for mobile, but stated explicitly: it's what lets a
                    // player keep playing through a dead signal, with writes replayed
                    // on reconnect.
                    db.Settings.PersistenceEnabled = true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Cloud] Couldn't apply Firestore settings: {e.Message}");
                }
            }
            return db;
        }
    }

    /// <summary>
    /// Fetch the cloud save for a user. Returns null when they have none yet, and
    /// also null on failure — callers treat both the same way (keep playing locally),
    /// so check <paramref name="failed"/> only when you need to tell them apart.
    /// </summary>
    public static async Task<SaveBundle> FetchAsync(string uid)
    {
        var (bundle, _) = await TryFetchAsync(uid);
        return bundle;
    }

    /// <summary>
    /// As <see cref="FetchAsync"/>, but distinguishes "no save yet" (null, false)
    /// from "couldn't reach Firestore" (null, true). The difference matters: the
    /// first means it's safe to upload local progress, the second very much doesn't.
    /// </summary>
    public static async Task<(SaveBundle bundle, bool failed)> TryFetchAsync(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return (null, true);
        if (!await FirebaseInit.EnsureAsync()) return (null, true);

        try
        {
            var snapshot = await Db.Collection(Collection).Document(uid).GetSnapshotAsync();
            if (!snapshot.Exists) return (null, false);

            if (!snapshot.TryGetValue(FieldPayload, out string payload) || string.IsNullOrEmpty(payload))
            {
                Debug.LogWarning($"[Cloud] users/{uid} exists but has no payload.");
                return (null, false);
            }

            var bundle = JsonUtility.FromJson<SaveBundle>(payload);
            if (bundle == null || bundle.user == null)
            {
                Debug.LogWarning($"[Cloud] users/{uid} payload didn't parse into a save.");
                return (null, false);
            }

            if (bundle.schemaVersion > SaveBundle.CurrentSchemaVersion)
            {
                // Written by a newer build. Applying it could silently drop fields
                // this build doesn't know about, so refuse rather than corrupt it.
                Debug.LogWarning(
                    $"[Cloud] users/{uid} was saved by a newer version " +
                    $"(schema {bundle.schemaVersion} > {SaveBundle.CurrentSchemaVersion}); ignoring.");
                return (null, true);
            }

            return (bundle, false);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Cloud] Fetch failed for {uid}: {e}");
            return (null, true);
        }
    }

    /// <summary>
    /// Write a bundle to the cloud, overwriting whatever was there. Returns false on
    /// failure — the local save is always already written by this point, so a failed
    /// push costs nothing but a retry.
    /// </summary>
    public static async Task<bool> PushAsync(string uid, SaveBundle bundle)
    {
        if (string.IsNullOrEmpty(uid) || bundle?.user == null) return false;
        if (!await FirebaseInit.EnsureAsync()) return false;

        try
        {
            var document = new Dictionary<string, object>
            {
                [FieldPayload] = JsonUtility.ToJson(bundle),
                [FieldSchema] = bundle.schemaVersion,
                [FieldUpdatedTicks] = bundle.savedUtcTicks,

                // Denormalised for console readability only — never read back.
                ["displayName"] = bundle.user.displayName ?? string.Empty,
                ["level"] = bundle.user.level,
                ["xp"] = bundle.user.xp,
                ["energy"] = bundle.user.energy,
                ["updatedAt"] = Firebase.Firestore.FieldValue.ServerTimestamp,
            };

            await Db.Collection(Collection).Document(uid).SetAsync(document);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Cloud] Push failed for {uid}: {e}");
            return false;
        }
    }
}

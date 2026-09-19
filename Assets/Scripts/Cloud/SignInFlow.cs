using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Orchestrates what happens around signing in — the part that isn't auth and isn't
/// storage, but decides which save the player ends up with.
///
/// The interesting case is the collision: someone played as a guest, built up
/// progress, and then signs in to an account that already has a save. Neither record
/// is obviously right, so rather than picking silently we ask. Everything else
/// (no cloud save, no local progress) resolves without bothering the player.
/// </summary>
public static class SignInFlow
{
    /// <summary>What a completed sign-in did to the player's save.</summary>
    public enum Outcome
    {
        /// <summary>Sign-in didn't happen (cancelled or failed); nothing changed.</summary>
        NotSignedIn,
        /// <summary>The cloud save was applied to this device.</summary>
        RestoredFromCloud,
        /// <summary>This device's save was uploaded to a previously empty account.</summary>
        UploadedLocal,
        /// <summary>Signed in, but the cloud was unreachable — playing locally for now.</summary>
        SignedInOffline,
    }

    public readonly struct Result
    {
        public readonly Outcome Outcome;
        public readonly bool Cancelled;
        public readonly string Error;

        public Result(Outcome outcome, bool cancelled = false, string error = null)
        {
            Outcome = outcome; Cancelled = cancelled; Error = error;
        }

        public bool SignedIn => Outcome != Outcome.NotSignedIn;
    }

    /// <summary>
    /// Sign in with Google and reconcile local and cloud saves, prompting the player
    /// if they genuinely conflict. Never throws.
    /// </summary>
    public static async Task<Result> SignInWithGoogleAsync()
    {
        var auth = await AuthService.SignInWithGoogleAsync();
        if (auth.Cancelled) return new Result(Outcome.NotSignedIn, cancelled: true);
        if (!auth.Success) return new Result(Outcome.NotSignedIn, error: auth.Error);

        var outcome = await ReconcileAsync(AuthService.Uid);

        // Only start syncing once the save question is settled, so the debounced
        // pusher can't race the decision and upload the record we're about to replace.
        CloudSync.Begin();

        // We've just reconciled; a later ResumeAsync must not do it a second time.
        _resume = Task.FromResult(true);

        return new Result(outcome);
    }

    /// <summary>The shared resume attempt, so two callers can't reconcile concurrently.</summary>
    static Task<bool> _resume;

    /// <summary>
    /// Re-establish a remembered Google session at launch, with no UI. Returns true if
    /// the player is signed in and can go straight into the game.
    ///
    /// Unlike a fresh sign-in this never prompts: it's the same account on the same
    /// device, so whichever copy was written last is simply the right one. That's what
    /// makes progress from another device show up on this one.
    ///
    /// Both the login screen and <see cref="AppBootstrap"/> call this — entering a
    /// gameplay scene directly in the Editor skips the login screen entirely — so the
    /// attempt is shared rather than run twice against itself.
    /// </summary>
    public static Task<bool> ResumeAsync() => _resume ??= ResumeOnceAsync();

    static async Task<bool> ResumeOnceAsync()
    {
        var state = await AuthService.RestoreAsync();
        if (state != AccountState.Google || string.IsNullOrEmpty(AuthService.Uid)) return false;

        string uid = AuthService.Uid;
        var (cloud, failed) = await CloudSaveService.TryFetchAsync(uid);

        if (!failed)
        {
            var local = SaveBundle.CaptureLocal();
            if (cloud == null) await PushLocalAsync(uid, local);
            else if (cloud.IsNewerThan(local)) ApplyCloud(cloud);
        }

        CloudSync.Begin();
        return true;
    }

    /// <summary>
    /// Decide whether this device keeps its save, takes the cloud's, or asks.
    /// Split out so it can also run on a silent restore at launch.
    /// </summary>
    static async Task<Outcome> ReconcileAsync(string uid)
    {
        var (cloud, failed) = await CloudSaveService.TryFetchAsync(uid);

        if (failed)
        {
            // Do NOT upload in this case. "Couldn't read" is not "nothing there",
            // and overwriting an unread cloud save would destroy real progress.
            Debug.LogWarning("[Cloud] Cloud save unreadable; continuing with local data.");
            return Outcome.SignedInOffline;
        }

        var local = SaveBundle.CaptureLocal();

        if (cloud == null)
        {
            // Fresh account: this device's progress becomes the account's save.
            await PushLocalAsync(uid, local);
            return Outcome.UploadedLocal;
        }

        // A brand-new guest record isn't worth prompting over — take the cloud.
        if (!HasMeaningfulProgress(local))
        {
            ApplyCloud(cloud);
            return Outcome.RestoredFromCloud;
        }

        // Same save, continued on this device since the last upload: nothing to decide.
        // Only skips the prompt when local is genuinely the newer copy — matching
        // userIds alone isn't enough, or a device left idle for a month would silently
        // overwrite progress made elsewhere yesterday.
        if (local.user != null && cloud.user != null &&
            local.user.userId == cloud.user.userId &&
            local.savedUtcTicks >= cloud.savedUtcTicks)
        {
            await PushLocalAsync(uid, local);
            return Outcome.UploadedLocal;
        }

        bool keepCloud = await AskWhichSaveAsync(local, cloud);

        if (keepCloud)
        {
            ApplyCloud(cloud);
            return Outcome.RestoredFromCloud;
        }

        await PushLocalAsync(uid, local);
        return Outcome.UploadedLocal;
    }

    /// <summary>
    /// Put the decision to the player. Returns true to keep the cloud save.
    /// Wraps the callback-style dialog so the flow above can stay linear.
    /// </summary>
    static Task<bool> AskWhichSaveAsync(SaveBundle local, SaveBundle cloud)
    {
        var choice = new TaskCompletionSource<bool>();

        ChoiceDialog.Show(
            "Two saves found",
            $"This account already has progress saved.\n\n" +
            $"<b>In the cloud</b>\n{cloud.Describe()}\n\n" +
            $"<b>On this device</b>\n{local.Describe()}\n\n" +
            "Whichever you don't keep will be replaced.",
            primaryText: "Keep cloud save",
            secondaryText: "Keep this device",
            onChoice: keepCloud => choice.TrySetResult(keepCloud));

        return choice.Task;
    }

    static void ApplyCloud(SaveBundle cloud)
    {
        cloud.ApplyToLocal();
        Debug.Log($"[Cloud] Restored cloud save: {cloud.Describe()}");
    }

    static async Task PushLocalAsync(string uid, SaveBundle local)
    {
        // Stamp the signed-in identity onto the record so it stops looking like a
        // guest and the account's display name follows the player across devices.
        if (local.user != null)
        {
            local.user.isGuest = false;
            if (!string.IsNullOrEmpty(AuthService.DisplayName))
                local.user.displayName = AuthService.DisplayName;
            PlayerSession.Save();
        }

        await CloudSaveService.PushAsync(uid, local);
        Debug.Log($"[Cloud] Uploaded local save: {local.Describe()}");
    }

    /// <summary>
    /// Whether a save represents real play, as opposed to a guest record that was
    /// auto-created seconds ago and never used. Prompting about an empty save is
    /// just a confusing question with no wrong answer.
    /// </summary>
    static bool HasMeaningfulProgress(SaveBundle bundle)
    {
        if (bundle?.user == null) return false;

        // Inventory is deliberately not a signal: the starter items are granted
        // automatically, so every brand-new guest would look like real progress.
        return bundle.user.level > LevelModel.StartLevel
            || bundle.user.xp > 0
            || bundle.questionnaireCompleted;
    }

    /// <summary>
    /// Continue without an account. Purely local: no Firebase, no network, and the
    /// existing on-device save is left exactly as it is.
    /// </summary>
    public static void ContinueAsGuest()
    {
        AuthService.ContinueAsGuest();
        if (!UserDataStore.Exists())
        {
            UserDataStore.Save(UserData.NewGuest());
            Debug.Log("[Cloud] Created guest user data.");
        }
    }

    /// <summary>
    /// Sign out. Flushes any pending backup first so the last few minutes of play
    /// aren't lost, then clears this device's save — otherwise the next person to
    /// sign in would start on top of the previous account's progress.
    /// </summary>
    public static async Task SignOutAsync()
    {
        if (AuthService.IsCloudBacked)
        {
            string uid = AuthService.Uid;
            await CloudSaveService.PushAsync(uid, SaveBundle.CaptureLocal());
        }

        await AuthService.SignOutAsync();

        // Let a later sign-in reconcile again rather than reusing this session's answer.
        _resume = null;

        UserDataStore.Clear();
        QuestionnaireStore.Clear();
        PlayerSession.Reset();
    }
}

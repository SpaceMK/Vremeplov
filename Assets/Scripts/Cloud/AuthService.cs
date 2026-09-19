using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>How this device is currently playing.</summary>
public enum AccountState
{
    /// <summary>Nobody has chosen yet — the login screen's starting state.</summary>
    SignedOut,
    /// <summary>Local-only play. No Firebase, no cloud, no network needed.</summary>
    Guest,
    /// <summary>Signed in with Google; progress is backed up to Firestore.</summary>
    Google,
}

/// <summary>
/// The app's single source of truth for who is playing. Wraps Firebase Auth for
/// the Google path and simply records a flag for the guest path.
///
/// The guest path never touches Firebase — that's the whole point of the split.
/// Only <see cref="SignInWithGoogleAsync"/> brings the SDK up.
/// </summary>
public static class AuthService
{
    /// <summary>Remembers the choice across launches so returning players skip the picker.</summary>
    const string StateKey = "authState";

    /// <summary>Raised whenever <see cref="State"/> or the identity fields change.</summary>
    public static event Action Changed;

    public static AccountState State { get; private set; } = AccountState.SignedOut;

    /// <summary>Firebase UID when signed in with Google; null otherwise.</summary>
    public static string Uid { get; private set; }

    public static string DisplayName { get; private set; }
    public static string Email { get; private set; }
    public static string PhotoUrl { get; private set; }

    /// <summary>True when progress should be syncing to the cloud.</summary>
    public static bool IsCloudBacked => State == AccountState.Google && !string.IsNullOrEmpty(Uid);

    /// <summary>
    /// What this device chose last launch, read straight from local storage. Cheap and
    /// synchronous — lets callers skip the whole async restore path for guests.
    /// </summary>
    public static AccountState RememberedState =>
        (AccountState)PlayerPrefs.GetInt(StateKey, (int)AccountState.SignedOut);

    /// <summary>The outcome of a sign-in attempt, so the UI can react without exceptions.</summary>
    public readonly struct SignInResult
    {
        public readonly bool Success;
        public readonly bool Cancelled;
        public readonly string Error;

        SignInResult(bool success, bool cancelled, string error)
        {
            Success = success; Cancelled = cancelled; Error = error;
        }

        public static SignInResult Ok() => new SignInResult(true, false, null);
        public static SignInResult Cancel() => new SignInResult(false, true, null);
        public static SignInResult Fail(string error) => new SignInResult(false, false, error);
    }

    /// <summary>
    /// Restore the remembered account on launch. For Google this reconnects to the
    /// cached Firebase session (no UI, no network round-trip needed if the token is
    /// still valid); for guests it's just a flag. Returns the resulting state.
    /// </summary>
    public static async Task<AccountState> RestoreAsync()
    {
        var remembered = (AccountState)PlayerPrefs.GetInt(StateKey, (int)AccountState.SignedOut);

        if (remembered != AccountState.Google)
        {
            // Guest, or nothing chosen yet. Either way: no Firebase.
            SetState(remembered, null, null, null, null);
            return State;
        }

        if (!await FirebaseInit.EnsureAsync())
        {
            // Firebase is unreachable — fall back to local play rather than
            // stranding the player on a login screen they can't get past.
            Debug.LogWarning("[Cloud] Couldn't restore Google session; continuing locally.");
            SetState(AccountState.Guest, null, null, null, null);
            return State;
        }

        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null)
        {
            SetState(AccountState.SignedOut, null, null, null, null);
            return State;
        }

        AdoptFirebaseUser(user);
        return State;
    }

    /// <summary>Record that this device is playing locally. Does not clear any saved data.</summary>
    public static void ContinueAsGuest()
    {
        SetState(AccountState.Guest, null, null, null, null);
    }

    /// <summary>
    /// Full Google sign-in: account picker → Google ID token → Firebase credential.
    /// Never throws; inspect the result.
    /// </summary>
    public static async Task<SignInResult> SignInWithGoogleAsync()
    {
        if (!GoogleCredentialProvider.IsSupported)
            return SignInResult.Fail("Google sign-in only works in a device build, not in the Editor.");

        if (!await FirebaseInit.EnsureAsync())
            return SignInResult.Fail(FirebaseInit.LastError ?? "Couldn't reach Firebase.");

        var google = await GoogleCredentialProvider.RequestIdTokenAsync();
        if (google.Cancelled) return SignInResult.Cancel();
        if (!google.Success) return SignInResult.Fail(google.Error);

        try
        {
            // Google's ID token is exchanged for a Firebase session. The access token
            // is only needed for calling Google APIs directly, which we don't.
            var credential = Firebase.Auth.GoogleAuthProvider.GetCredential(google.IdToken, null);
            var result = await Firebase.Auth.FirebaseAuth.DefaultInstance
                .SignInAndRetrieveDataWithCredentialAsync(credential);

            AdoptFirebaseUser(result.User, google.DisplayName, google.Email);
            Debug.Log($"[Cloud] Signed in as {DisplayName} ({Uid}).");
            return SignInResult.Ok();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Cloud] Firebase credential exchange failed: {e}");

            // Google accepted the account but Firebase refused the token. Distinguishing
            // this from a failure in the picker itself matters, and without USB debugging
            // the popup is the only place that distinction can be read.
            var inner = e is AggregateException agg && agg.InnerException != null
                ? agg.InnerException : e;
            string detail = inner.Message?.Replace("\r", " ").Replace("\n", " ").Trim();
            if (!string.IsNullOrEmpty(detail) && detail.Length > 160)
                detail = detail.Substring(0, 160) + "…";

            return SignInResult.Fail(
                "Couldn't complete sign-in. Please try again.\n\n" +
                $"Details: Firebase credential exchange — {inner.GetType().Name}: {detail}");
        }
    }

    /// <summary>
    /// Sign out of Google and drop back to signed-out. Local save data is left alone —
    /// the caller decides whether to clear it (see <see cref="SignInFlow.SignOutAsync"/>).
    /// </summary>
    public static async Task SignOutAsync()
    {
        if (State == AccountState.Google)
        {
            try
            {
                if (await FirebaseInit.EnsureAsync())
                    Firebase.Auth.FirebaseAuth.DefaultInstance.SignOut();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Cloud] Firebase sign-out failed: {e.Message}");
            }
            GoogleCredentialProvider.SignOut();
        }

        SetState(AccountState.SignedOut, null, null, null, null);
    }

    static void AdoptFirebaseUser(Firebase.Auth.FirebaseUser user, string fallbackName = null, string fallbackEmail = null)
    {
        // Firebase's profile can lag a first-time sign-in, so prefer it but fall
        // back to what Google just told us.
        string name = !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : fallbackName;
        string email = !string.IsNullOrEmpty(user.Email) ? user.Email : fallbackEmail;

        SetState(AccountState.Google, user.UserId, name, email, user.PhotoUrl?.ToString());
    }

    static void SetState(AccountState state, string uid, string name, string email, string photo)
    {
        State = state;
        Uid = uid;
        DisplayName = name;
        Email = email;
        PhotoUrl = photo;

        PlayerPrefs.SetInt(StateKey, (int)state);
        PlayerPrefs.Save();

        Changed?.Invoke();
    }
}

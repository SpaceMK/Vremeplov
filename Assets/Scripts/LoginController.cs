using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the login screen: greeting text and the two entry buttons.
///
/// Both entry paths:
/// <list type="bullet">
/// <item>"Sign in as Guest" — entirely local, no network and no Firebase.</item>
/// <item>"Continue with Google" — Firebase Auth, with progress backed up to and
/// restored from Firestore (see <see cref="SignInFlow"/>).</item>
/// </list>
/// There is deliberately no email/password form: an account is either a Google
/// account or it is local-only, so there is nothing for us to hold a password for.
/// </summary>
public class LoginController : MonoBehaviour
{
    [Header("Greeting")]
    public TMP_Text titleLabel;
    public string returningTitle = "Welcome Back!";
    public string newUserTitle = "Welcome to Tales Tensor";

    [Header("Guest button")]
    public TMP_Text guestLabel;
    public string guestNewText = "Sign in as Guest";
    public string guestReturningText = "Sign back in as Guest";

    [Header("Popup")]
    public PopupController popup;

    [Header("Sign-in state")]
    [Tooltip("Optional. Buttons disabled while a sign-in is in flight, so it can't be started twice.")]
    public Button googleButton;
    public Button guestButton;
    [Tooltip("Optional. Shows 'Signing in…' while waiting on Google/Firebase.")]
    public TMP_Text statusLabel;

    [Header("Navigation")]
    [Tooltip("Onboarding questionnaire, shown to players who haven't completed it. Must be in Build Settings.")]
    public string questionnaireSceneName = "QuestionScene";
    [Tooltip("Main scene, loaded once onboarding is done (or for returning players). Must be in Build Settings.")]
    public string mapSceneName = "MapScene";

    bool _busy;

    void Start()
    {
        bool returning = UserDataStore.Exists();
        if (titleLabel != null)
            titleLabel.text = returning ? returningTitle : newUserTitle;
        if (guestLabel != null)
            guestLabel.text = returning ? guestReturningText : guestNewText;

        if (statusLabel != null) statusLabel.text = string.Empty;

        TryResumeSession();
    }

    /// <summary>
    /// A player who signed in with Google last time shouldn't have to do it again.
    /// Reconnects to the cached Firebase session and goes straight in; silently does
    /// nothing for guests, or if the session has lapsed.
    /// </summary>
    async void TryResumeSession()
    {
        if (!GoogleCredentialProvider.IsSupported) return;

        // Guests and first-time players shouldn't sit behind a "restoring" spinner for
        // a session that was never going to exist.
        if (AuthService.RememberedState != AccountState.Google) return;

        SetBusy(true, "Restoring your progress…");

        bool resumed;
        try { resumed = await SignInFlow.ResumeAsync(); }
        finally { SetBusy(false); }

        // Destroyed while awaiting — the scene moved on without us.
        if (this == null || !resumed) return;

        Enter();
    }

    /// <summary>
    /// Sign in with Google, then restore or back up progress before entering.
    /// </summary>
    public async void OnGoogle()
    {
        if (_busy) return;

        if (!GoogleCredentialProvider.IsSupported)
        {
            // The plugin is native-only, so the Editor can't do this at all.
            ShowMessage("Not available here",
                "Google sign-in needs a phone build.\nUse 'Sign in as Guest' in the Editor.");
            return;
        }

        if (!CloudConfig.IsConfigured)
        {
            ShowMessage("Not configured",
                "Google sign-in isn't set up for this build yet.\nUse 'Sign in as Guest' for now.");
            return;
        }

        SetBusy(true, "Signing in…");

        SignInFlow.Result result;
        try
        {
            result = await SignInFlow.SignInWithGoogleAsync();
        }
        finally
        {
            SetBusy(false);
        }

        if (result.Cancelled) return; // player backed out; stay put, say nothing

        if (!result.SignedIn)
        {
            ShowMessage("Couldn't sign in", result.Error ?? "Please try again.");
            return;
        }

        if (result.Outcome == SignInFlow.Outcome.SignedInOffline)
            Debug.LogWarning("[Login] Signed in but the cloud save was unreachable.");

        Enter();
    }

    /// <summary>Local-only entry: create local data if needed, then enter.</summary>
    public void OnGuest()
    {
        if (_busy) return;
        SignInFlow.ContinueAsGuest();
        Enter();
    }

    void ShowMessage(string title, string message)
    {
        if (popup != null) popup.Show(title, message);
        else Debug.LogWarning($"[Login] {title}: {message}");
    }

    /// <summary>
    /// Lock the entry buttons while a sign-in is in flight. Sign-in involves a native
    /// account picker plus two network round-trips, which is plenty of time to tap
    /// "Guest" as well and end up racing two entry paths into the same scene load.
    /// </summary>
    void SetBusy(bool busy, string status = null)
    {
        _busy = busy;

        if (googleButton != null) googleButton.interactable = !busy;
        if (guestButton != null) guestButton.interactable = !busy;
        if (statusLabel != null) statusLabel.text = busy ? (status ?? string.Empty) : string.Empty;
    }

    void Enter()
    {
        // New players still need onboarding; everyone else goes straight to the map.
        string next = QuestionnaireStore.IsComplete() ? mapSceneName : questionnaireSceneName;
        if (string.IsNullOrEmpty(next)) return;
        SceneTransition.Load(next); // animated wipe; warns itself if not in Build Settings
    }
}

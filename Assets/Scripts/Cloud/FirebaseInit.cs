using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Brings up the Firebase native SDK exactly once, and tells the rest of the app
/// whether it succeeded.
///
/// Deliberately lazy: nothing here runs until something actually wants the cloud
/// (i.e. the player taps "Continue with Google"). A guest never initialises
/// Firebase at all, so guest play has no dependency on Play Services, network,
/// or a valid <c>google-services.json</c>.
/// </summary>
public static class FirebaseInit
{
    /// <summary>Null until the first <see cref="EnsureAsync"/>; then the shared attempt.</summary>
    static Task<bool> _attempt;

    /// <summary>True once Firebase reported all dependencies available.</summary>
    public static bool IsReady { get; private set; }

    /// <summary>Why initialisation failed, for surfacing in the UI. Null when fine.</summary>
    public static string LastError { get; private set; }

    /// <summary>
    /// Initialise Firebase if it hasn't been already. Safe to await from anywhere and
    /// from multiple callers — they all share one attempt. Returns false rather than
    /// throwing, because every caller's fallback is the same: stay local.
    /// </summary>
    public static Task<bool> EnsureAsync() => _attempt ??= InitAsync();

    static async Task<bool> InitAsync()
    {
        try
        {
            var status = await Firebase.FirebaseApp.CheckAndFixDependenciesAsync();
            if (status != Firebase.DependencyStatus.Available)
            {
                LastError = $"Firebase dependencies unavailable: {status}";
                Debug.LogError($"[Cloud] {LastError}");
                IsReady = false;
                return false;
            }

            // Touching DefaultInstance is what actually creates the native app object.
            _ = Firebase.FirebaseApp.DefaultInstance;

            // ...and the app object alone is not enough. Auth's native layer is brought
            // up as a side effect of constructing the first FirebaseAuth instance, and
            // until that happens the *static* GoogleAuthProvider.GetCredential throws
            // "Firebase Auth was not initialized, unable to create a Credential" —
            // before any network call, so it looks exactly like a sign-in failure even
            // though the Google account picker has already succeeded.
            _ = Firebase.Auth.FirebaseAuth.DefaultInstance;

            IsReady = true;
            LastError = null;
            Debug.Log("[Cloud] Firebase ready.");
            return true;
        }
        catch (Exception e)
        {
            // Missing google-services.json shows up here, as do Play Services problems.
            LastError = e.Message;
            Debug.LogError($"[Cloud] Firebase init failed: {e}");
            IsReady = false;
            return false;
        }
    }
}

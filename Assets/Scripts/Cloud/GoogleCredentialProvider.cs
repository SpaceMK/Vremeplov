using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Wraps the native Google account picker and hands back the Google ID token that
/// Firebase Auth needs. Isolated behind this one class so the (archived, 2018)
/// Google Sign-In plugin can be swapped for a maintained one — or for Firebase's
/// own <c>FederatedOAuthProvider</c> — by rewriting this file alone.
///
/// Only real devices are supported: the plugin talks to Play Services / the iOS
/// Google SDK through native interop, neither of which exists in the Editor.
/// </summary>
public static class GoogleCredentialProvider
{
    /// <summary>The result of asking the player to pick a Google account.</summary>
    public readonly struct Result
    {
        public readonly string IdToken;
        public readonly string DisplayName;
        public readonly string Email;
        public readonly bool Cancelled;
        public readonly string Error;

        Result(string idToken, string displayName, string email, bool cancelled, string error)
        {
            IdToken = idToken;
            DisplayName = displayName;
            Email = email;
            Cancelled = cancelled;
            Error = error;
        }

        public bool Success => !string.IsNullOrEmpty(IdToken);

        public static Result Ok(string idToken, string name, string email) =>
            new Result(idToken, name, email, false, null);
        public static Result Cancel() => new Result(null, null, null, true, null);
        public static Result Fail(string error) => new Result(null, null, null, false, error);
    }

    /// <summary>True on platforms where the native sign-in UI actually exists.</summary>
    public static bool IsSupported
    {
        get
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Show the Google account picker and return an ID token. Never throws — a
    /// cancel, a misconfiguration and a network failure all come back as a
    /// <see cref="Result"/> the UI can describe.
    /// </summary>
    public static async Task<Result> RequestIdTokenAsync()
    {
        if (!CloudConfig.IsConfigured)
            return Result.Fail("Google sign-in isn't configured for this build.");

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        try
        {
            Google.GoogleSignIn.Configuration = new Google.GoogleSignInConfiguration
            {
                WebClientId = CloudConfig.WebClientId,
                // The ID token is the whole point — it's what Firebase exchanges for a session.
                RequestIdToken = true,
                RequestEmail = true,
                RequestProfile = true,
                // This is plain Google Sign-In, not Play Games sign-in.
                UseGameSignIn = false,
            };

            Google.GoogleSignInUser user = await Google.GoogleSignIn.DefaultInstance.SignIn();

            if (user == null || string.IsNullOrEmpty(user.IdToken))
                return Result.Fail("Google returned no ID token. Check the web client ID and SHA-1 fingerprint.");

            return Result.Ok(user.IdToken, user.DisplayName, user.Email);
        }
        catch (Google.GoogleSignIn.SignInException e)
        {
            if (e.Status == Google.GoogleSignInStatusCode.Canceled) return Result.Cancel();
            return FailWithDetail(e.Status, e.Message);
        }
        catch (AggregateException e) when (e.InnerException is Google.GoogleSignIn.SignInException inner)
        {
            // Task.Exception wraps whatever the plugin's coroutine threw.
            if (inner.Status == Google.GoogleSignInStatusCode.Canceled) return Result.Cancel();
            return FailWithDetail(inner.Status, inner.Message);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Cloud] Google sign-in failed: {e}");
            // The type matters here: an AndroidJavaException from the plugin's native
            // glue is a very different problem from a socket timeout.
            return Result.Fail(
                "Couldn't reach Google. Check your connection and try again.\n\n" +
                $"Details: {e.GetType().Name}: {Shorten(e.Message)}");
        }
#else
        await Task.CompletedTask;
        return Result.Fail("Google sign-in only works in a device build, not in the Editor.");
#endif
    }

    /// <summary>Forget the picked account so the next sign-in prompts again.</summary>
    public static void SignOut()
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        try { Google.GoogleSignIn.DefaultInstance.SignOut(); }
        catch (Exception e) { Debug.LogWarning($"[Cloud] Google sign-out failed: {e.Message}"); }
#endif
    }

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
    /// <summary>
    /// Fail with the player-facing description *and* the raw status attached.
    ///
    /// The numeric code and the native message are the only things that actually
    /// identify a misconfiguration, and they're unreachable without USB debugging —
    /// so they go on screen rather than only into logcat. Cheap to keep: it costs one
    /// extra line in a popup the player only sees when sign-in has already failed.
    /// </summary>
    static Result FailWithDetail(Google.GoogleSignInStatusCode status, string nativeMessage)
    {
        Debug.LogError($"[Cloud] Google sign-in failed ({status}/{(int)status}): {nativeMessage}");
        return Result.Fail(
            $"{DescribeStatus(status)}\n\n" +
            $"Details: {status} (code {(int)status}){Shorten(nativeMessage, prefix: " — ")}");
    }

    /// <summary>Keep a native message readable inside a popup.</summary>
    static string Shorten(string message, string prefix = "", int max = 160)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        message = message.Replace("\r", " ").Replace("\n", " ").Trim();
        if (message.Length > max) message = message.Substring(0, max) + "…";
        return prefix + message;
    }

    /// <summary>Turn a plugin status code into something worth showing a player.</summary>
    static string DescribeStatus(Google.GoogleSignInStatusCode status)
    {
        switch (status)
        {
            case Google.GoogleSignInStatusCode.NetworkError:
                return "No connection. Check your network and try again.";
            case Google.GoogleSignInStatusCode.Timeout:
                return "Google took too long to respond. Please try again.";
            case Google.GoogleSignInStatusCode.DeveloperError:
                // Nearly always a signing-certificate mismatch on Android.
                return "Sign-in isn't set up correctly for this build.";
            case Google.GoogleSignInStatusCode.InvalidAccount:
                return "That Google account can't be used to sign in.";
            case Google.GoogleSignInStatusCode.ApiNotConnected:
                return "Google Play Services is unavailable on this device.";
            default:
                return "Google sign-in didn't complete. Please try again.";
        }
    }
#endif
}

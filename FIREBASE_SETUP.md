# Firebase setup

Player progress is backed up to Cloud Firestore when someone signs in with Google.
Guests stay entirely local — no Firebase, no network, no account.

The code is all in place. What's left is the console work that needs your Google
account, plus dropping two config files into the project.

---

## What's already done

| | |
|---|---|
| Firebase Unity SDK 13.14.0 | `Assets/Firebase/` — Auth + Firestore only |
| External Dependency Manager 1.2.188 | `Assets/ExternalDependencyManager/` |
| Google Sign-In plugin 1.0.4 | `Assets/GoogleSignIn/`, `Assets/Plugins/iOS/GoogleSignIn/` |
| Integration code | `Assets/Scripts/Cloud/` |
| Security rules | `firestore.rules` |
| Bundle ID | `com.talestensor.talestensor` (was the Unity template default) |

**`com.talestensor.talestensor` is the project's bundle ID — don't change it.** It's
baked into the Firebase app registration, so changing it means redoing steps 2–4.

---

## 1. Create the Firebase project

1. <https://console.firebase.google.com> → **Add project** → name it (e.g. `tales-tensor`).
2. Google Analytics is optional; the game doesn't use it.

## 2. Register the Android app

1. Project settings → **Your apps** → Android.
2. Package name: `com.talestensor.talestensor` — must match `Player Settings ▸ Identification` exactly.
3. **Add your SHA-1 debug fingerprint.** Google Sign-In returns `DeveloperError` (status 6)
   without it, with no other clue as to why:
   ```
   keytool -list -v -keystore ~/.android/debug.keystore -alias androiddebugkey -storepass android -keypass android
   ```
   On Windows the keystore is at `%USERPROFILE%\.android\debug.keystore`.
   Add the release keystore's SHA-1 too before you ship.
4. Download **`google-services.json`** → put it at `Assets/google-services.json`.

## 3. Register the iOS app (skip if Android-only)

1. Project settings → **Your apps** → iOS, bundle ID `com.talestensor.talestensor`.
2. Download **`GoogleService-Info.plist`** → put it at `Assets/GoogleService-Info.plist`.

## 4. Turn on Google sign-in

Authentication → **Sign-in method** → enable **Google**, set a support email, save.

This is what creates the *web* OAuth client that the ID token flow depends on.
Do this **before** downloading `google-services.json` — if you already downloaded it,
download it again afterwards, or it won't contain the web client entry.

## 5. Create the Firestore database

1. Firestore Database → **Create database** → production mode.
2. Replace the rules with the contents of `firestore.rules`, or:
   ```
   firebase deploy --only firestore:rules
   ```

Test mode rules let any signed-in user read every other player's save. Don't ship them.

## 6. Wire the config into Unity

1. Open the project in Unity (it will import the SDK and run the dependency resolver).
2. **Tools ▸ Tales Tensor ▸ Sync Firebase Config**

   This pulls the web OAuth client ID out of `google-services.json` and writes
   `Assets/Resources/CloudConfig.txt`, which is what the runtime reads. Until you run
   it, the Google button says "not configured" and the game stays guest-only.

3. Android: **Assets ▸ External Dependency Manager ▸ Android Resolver ▸ Resolve**.

## 7. Wire the login scene (optional but recommended)

In `LoginScene`, on the `LoginController` component, assign:

- **Google Button** → the existing `GoogleButton`
- **Guest Button** → the existing `GuestButton`
- **Status Label** → any TMP text, or leave empty

These only drive the busy state — sign-in works without them, but the buttons stay
tappable during the several seconds a sign-in takes, so it's possible to start two
entry paths at once.

---

## Testing

**Google sign-in cannot be tested in the Editor.** The plugin talks to Play Services
and the iOS Google SDK through native interop, neither of which exists in the Editor.
The button explains this and the Editor stays guest-only. Test on a device.

Worth checking on device:

- Guest → play → sign in from the profile sheet → progress uploads.
- Sign in on a second device with existing progress → the "Two saves found" prompt.
- Sign out → local save clears; sign back in → progress returns.
- Airplane mode while signed in → play continues; changes push on reconnect.

Useful during testing:

- **Tools ▸ Tales Tensor ▸ Sign Out of Google (clear local auth)** — forget the
  remembered account without clearing progress.
- **Tools ▸ Tales Tensor ▸ Reset ALL PlayerPrefs** — wipe the device clean.

---

## How it fits together

```
LoginController ─┬─ "Sign in as Guest" ──► SignInFlow.ContinueAsGuest()   (local only)
                 │
                 └─ "Continue with Google"
                        └► SignInFlow.SignInWithGoogleAsync()
                             ├─ GoogleCredentialProvider  → Google ID token
                             ├─ AuthService               → Firebase session (uid)
                             ├─ CloudSaveService.Fetch    → users/{uid}
                             ├─ conflict? ChoiceDialog    → keep cloud / keep device
                             └─ CloudSync.Begin()         → debounced background backup
```

- `SaveBundle` — what gets stored: `UserData` (level, XP, energy, inventory) plus the
  questionnaire `UserProfile`, as one JSON blob in the document's `payload` field.
- `CloudSync` — listens to `PlayerSession.Saved` and pushes at most once every 30s,
  plus immediately on pause/focus-loss/quit. Movement awards XP every tick, so an
  un-debounced push would be a Firestore write per second per player.
- Guests never call `FirebaseInit`, so nothing Firebase-related runs for them at all.

### Conflict handling

Signing in when both the cloud and the device have real progress shows a prompt with
both saves described (`Level 7 · 1,240 XP · 3 days ago`) and lets the player choose.
It resolves silently when there's nothing to decide: no cloud save (upload), a
brand-new guest record (restore), or the same account already newer locally (upload).

If Firestore can't be *read*, the code will not upload — "couldn't read" is not
"nothing there", and treating them the same would overwrite a real save with a
fresh one. It signs in and plays locally instead.

---

## Known risks

**The Google Sign-In plugin is archived.** `googlesamples/google-signin-unity` was
last released in 2018 and the repo is archived. It was imported with two bundled
pieces removed, because both break on Unity 6:

- `Assets/Parse/` — a `System.Threading.Tasks` shim for .NET 3.5 that now collides
  with the runtime's own TPL.
- `Assets/PlayServicesResolver/` — EDM4U 1.2.89, superseded by the 1.2.188 that
  ships with Firebase.

`Assets/GoogleSignIn/Editor/GoogleSignInDependencies.xml` pins the **iOS pod only**.
Upstream asked for `GoogleSignIn >= 4.0.2`, which today resolves to a release the
plugin's own 2018 native glue can't build against — **GoogleSignIn 6.0 removed the
`GIDSignIn` delegate API** that `Assets/Plugins/iOS/GoogleSignIn/*.mm` is written
against. It's pinned to `~> 5.0`.

**Android must stay on the upstream `play-services-auth:16+`.** That is not a
`>= 16` constraint: `+` is a Gradle *prefix match*, so it selects the highest
version whose string starts with `16` — 16.0.1 — which is the generation
`google-signin-support-1.0.4.srcaar` was built against.

> Pinning Android to `21.3.0` (on the assumption that `16+` meant "≥ 16" and would
> drift to something too new, as the iOS pod does) was the cause of Google sign-in
> failing on device with `DeveloperError` / status code 10 — the account picker
> appeared, then failed immediately on selection — while package name, SHA-1
> fingerprint and web client ID were all verified correct. `firebase-auth` does not
> pull `play-services-auth` transitively, so the declared version is what ships.
> The working reference implementation in `D:\Development\SkyesPetCare` uses the
> identical plugin, Firebase versions and debug-keystore SHA-1, and differs only in
> leaving this dependency unpinned.

Practical consequences:

- **Android** works on 16.x. Do not "modernise" this to 21.x — `GoogleSignInClient`
  is still present there, but the plugin's bundled native glue does not work with it.
- **iOS is the risk.** The pinned 5.x pod is old; verify an iOS build early rather
  than at submission time.

If either platform gives trouble, `GoogleCredentialProvider` is the only file that
touches the plugin — it exists to be swapped. The two alternatives are a maintained
community fork of the plugin, or Firebase's own `FederatedOAuthProvider`, which does
the whole OAuth flow in a Custom Tab / `SFSafariViewController` and needs no plugin,
no SHA-1, and no web client ID at all.

**Firestore in the Editor** works via the desktop natives in
`Assets/Firebase/Plugins/x86_64/`, but since Google sign-in can't run there, the
cloud path is only reachable on device.

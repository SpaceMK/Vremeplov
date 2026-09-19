using UnityEngine;

/// <summary>
/// App-wide startup settings applied once, before any scene loads. Lives here (rather
/// than on a scene object) via <see cref="RuntimeInitializeOnLoadMethodAttribute"/> so
/// it runs no matter which scene the app boots into and needs no wiring.
/// </summary>
public static class AppBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        // Apply the saved frame-rate preference (defaults to uncapped / display max).
        FrameRateSetting.Apply();

        // Reconnect a remembered Google session so cloud backup resumes even when the
        // login screen is skipped — entering MapScene directly from the Editor, or a
        // deep link straight into gameplay. Guests do nothing here and touch no network.
        SignInFlow.ResumeAsync().Forget();
    }
}

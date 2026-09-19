using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// One-click Android APK build that bumps the version first.
///
/// Every build increments <c>bundleVersionCode</c>. That is the number Play and the
/// device package manager actually compare, and installing an APK whose code isn't
/// higher than the one already on the device fails with an error that never mentions
/// versions - so it's worth never having to remember. The patch digit of
/// <c>bundleVersion</c> moves with it, so the human-readable string and the code can't
/// drift apart and leave you guessing which build a tester is holding.
///
/// A failed build rolls the version back, so a broken attempt doesn't burn a number
/// and leave a gap in the sequence.
///
/// Found under <c>Tools ▸ Tales Tensor</c>.
/// </summary>
public static class BuildMenu
{
    const string OutputDir = "Builds/Android";

    [MenuItem("Tools/Tales Tensor/Build Android APK", priority = 40)]
    public static void BuildAndroidApk()
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Nothing to build",
                "No scenes are enabled in Build Settings, so the APK would have nothing to load.",
                "OK");
            return;
        }

        if (!EnsureAndroidTarget()) return;

        int previousCode = PlayerSettings.Android.bundleVersionCode;
        string previousVersion = PlayerSettings.bundleVersion;

        PlayerSettings.Android.bundleVersionCode = previousCode + 1;
        PlayerSettings.bundleVersion = BumpPatch(previousVersion);
        AssetDatabase.SaveAssets();

        Directory.CreateDirectory(OutputDir);
        string apkPath = Path.Combine(
            OutputDir,
            $"TalesTensor-{PlayerSettings.bundleVersion}-{PlayerSettings.Android.bundleVersionCode}.apk");

        // An .aab would be silently produced instead if this is left on from a previous
        // Play upload build, and the filename above would then be a lie.
        EditorUserBuildSettings.buildAppBundle = false;

        Debug.Log($"[Tales Tensor] Building {apkPath} from {scenes.Length} scene(s)…");

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = apkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None,
        });

        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log(
                $"[Tales Tensor] Built {apkPath}\n" +
                $"version {PlayerSettings.bundleVersion}, code {PlayerSettings.Android.bundleVersionCode}, " +
                $"{summary.totalSize / 1024f / 1024f:F1} MB in {summary.totalTime:mm\\:ss}");
            EditorUtility.RevealInFinder(apkPath);
            return;
        }

        PlayerSettings.Android.bundleVersionCode = previousCode;
        PlayerSettings.bundleVersion = previousVersion;
        AssetDatabase.SaveAssets();

        Debug.LogError(
            $"[Tales Tensor] Build {summary.result} with {summary.totalErrors} error(s). " +
            $"Version rolled back to {previousVersion} (code {previousCode}). See the errors above.");
    }

    /// <summary>
    /// Switching platform reimports every asset, which on this project is minutes, not
    /// seconds - so ask rather than start it from a menu click that looked cheap.
    /// </summary>
    static bool EnsureAndroidTarget()
    {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) return true;

        if (!EditorUtility.DisplayDialog(
                "Switch to Android?",
                $"The active build target is {EditorUserBuildSettings.activeBuildTarget}. " +
                "Switching reimports assets and can take several minutes.",
                "Switch", "Cancel"))
            return false;

        if (EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            return true;

        Debug.LogError("[Tales Tensor] Couldn't switch to Android. Is the Android Build Support module installed?");
        return false;
    }

    /// <summary>
    /// Increment the last dot-separated component: <c>0.0.1</c> becomes <c>0.0.2</c>.
    ///
    /// Left untouched if that component isn't a plain number. Anything else - a
    /// <c>1.0-beta</c>, a date stamp - is a scheme this doesn't understand well enough
    /// to rewrite, and mangling it silently would be worse than not bumping it.
    /// </summary>
    static string BumpPatch(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "0.0.1";

        string[] parts = version.Split('.');
        if (!int.TryParse(parts[parts.Length - 1], out int patch))
        {
            Debug.LogWarning(
                $"[Tales Tensor] bundleVersion '{version}' doesn't end in a number; " +
                "leaving it as-is and bumping only the version code.");
            return version;
        }

        parts[parts.Length - 1] = (patch + 1).ToString();
        return string.Join(".", parts);
    }
}

using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor helper that pulls the web OAuth client ID out of <c>google-services.json</c>
/// and writes it where the runtime can read it (<see cref="CloudConfig"/>).
///
/// Google Sign-In needs the <em>web</em> client ID specifically — the Android and iOS
/// client IDs in the same file will produce an ID token Firebase rejects — and the
/// native SDKs don't expose it to C#. Rather than have someone copy a 70-character
/// string by hand and debug a silent auth failure when they grab the wrong one, this
/// picks the right entry out of the file.
///
/// Found under <c>Tools ▸ Tales Tensor</c>.
/// </summary>
public static class FirebaseConfigMenu
{
    const string GoogleServicesPath = "Assets/google-services.json";
    const string OutputDir = "Assets/Resources";
    const string OutputPath = OutputDir + "/" + CloudConfig.ResourceName + ".txt";

    /// <summary>OAuth client_type 3 is the web client; 1 is Android, 2 is iOS.</summary>
    const string WebClientType = "3";

    [MenuItem("Tools/Tales Tensor/Sync Firebase Config", priority = 20)]
    public static void SyncFirebaseConfig()
    {
        if (!File.Exists(GoogleServicesPath))
        {
            EditorUtility.DisplayDialog(
                "google-services.json not found",
                $"Expected it at {GoogleServicesPath}.\n\n" +
                "Download it from the Firebase console (Project settings > Your apps > Android) " +
                "and drop it into the Assets folder.",
                "OK");
            return;
        }

        string json = File.ReadAllText(GoogleServicesPath);
        string webClientId = ExtractWebClientId(json);

        if (string.IsNullOrEmpty(webClientId))
        {
            EditorUtility.DisplayDialog(
                "No web client ID",
                "google-services.json has no OAuth client of type 3 (web).\n\n" +
                "In the Firebase console, enable Authentication > Sign-in method > Google, " +
                "then download google-services.json again.",
                "OK");
            return;
        }

        Directory.CreateDirectory(OutputDir);
        File.WriteAllText(OutputPath, webClientId);
        AssetDatabase.ImportAsset(OutputPath);
        CloudConfig.Invalidate();

        Debug.Log($"[Tales Tensor] Wrote web client ID to {OutputPath}:\n{webClientId}");
    }

    [MenuItem("Tools/Tales Tensor/Sign Out of Google (clear local auth)", priority = 21)]
    public static void ClearAuthState()
    {
        PlayerPrefs.DeleteKey("authState");
        PlayerPrefs.Save();
        Debug.Log("[Tales Tensor] Cleared the remembered account; the next launch starts signed out.");
    }

    /// <summary>
    /// Find the <c>client_id</c> of the OAuth client whose <c>client_type</c> is 3.
    ///
    /// Uses a regex rather than JsonUtility because google-services.json is deeply
    /// nested with arrays of objects, which JsonUtility can't express without a
    /// mirror of Google's entire schema — a schema they change.
    /// </summary>
    static string ExtractWebClientId(string json)
    {
        // Match a { ... } block containing both client_id and client_type: "3",
        // in either order, tolerating whitespace and trailing fields.
        var entry = new Regex(
            @"\{[^{}]*?""client_id""\s*:\s*""(?<id>[^""]+)""[^{}]*?""client_type""\s*:\s*""?" + WebClientType + @"""?[^{}]*?\}",
            RegexOptions.Singleline);

        var match = entry.Match(json);
        if (match.Success) return match.Groups["id"].Value;

        // client_type can precede client_id depending on how the file was generated.
        var reversed = new Regex(
            @"\{[^{}]*?""client_type""\s*:\s*""?" + WebClientType + @"""?[^{}]*?""client_id""\s*:\s*""(?<id>[^""]+)""[^{}]*?\}",
            RegexOptions.Singleline);

        match = reversed.Match(json);
        return match.Success ? match.Groups["id"].Value : null;
    }
}

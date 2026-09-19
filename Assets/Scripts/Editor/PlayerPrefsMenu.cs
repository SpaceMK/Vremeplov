using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor menu helpers for clearing locally stored data during development.
/// Found under <c>Tools ▸ Tales Tensor</c>.
/// </summary>
public static class PlayerPrefsMenu
{
    [MenuItem("Tools/Tales Tensor/Reset User Data", priority = 0)]
    public static void ResetUserData()
    {
        if (!UserDataStore.Exists())
        {
            Debug.Log("[Tales Tensor] No user data to reset.");
            return;
        }
        if (!EditorUtility.DisplayDialog(
                "Reset User Data",
                "Delete the locally stored user profile? The next launch will be treated as a first-time user.",
                "Delete", "Cancel"))
            return;

        UserDataStore.Clear();
        PlayerPrefs.Save();
        Debug.Log("[Tales Tensor] User data cleared.");
    }

    [MenuItem("Tools/Tales Tensor/Reset Questionnaire", priority = 1)]
    public static void ResetQuestionnaire()
    {
        if (!QuestionnaireStore.IsComplete())
        {
            Debug.Log("[Tales Tensor] No questionnaire result to reset.");
            return;
        }
        if (!EditorUtility.DisplayDialog(
                "Reset Questionnaire",
                "Delete the stored questionnaire profile? The next entry will show the onboarding questions again.",
                "Delete", "Cancel"))
            return;

        QuestionnaireStore.Clear();
        Debug.Log("[Tales Tensor] Questionnaire result cleared.");
    }

    [MenuItem("Tools/Tales Tensor/Reset Map Pins", priority = 2)]
    public static void ResetMapPins()
    {
        if (!TalesTensor.Map.MapPinStore.Exists())
        {
            Debug.Log("[Tales Tensor] No saved map pins to reset.");
            return;
        }
        if (!EditorUtility.DisplayDialog(
                "Reset Map Pins",
                "Delete the saved points of interest? A fresh set will be scattered the next time the map loads.",
                "Delete", "Cancel"))
            return;

        TalesTensor.Map.MapPinStore.Clear();
        Debug.Log("[Tales Tensor] Saved map pins cleared.");
    }

    [MenuItem("Tools/Tales Tensor/Reset ALL PlayerPrefs", priority = 3)]
    public static void ResetAllPlayerPrefs()
    {
        if (!EditorUtility.DisplayDialog(
                "Reset ALL PlayerPrefs",
                "Delete every PlayerPref for this project? This cannot be undone.",
                "Delete All", "Cancel"))
            return;

        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("[Tales Tensor] All PlayerPrefs cleared.");
    }
}

using TMPro;
using UnityEngine;

/// <summary>
/// Minimal reusable modal popup. Toggles a root overlay on/off and fills in a
/// title + message. Wire the Close button's OnClick to <see cref="Close"/>.
/// Safe to call <see cref="Show"/> while the root starts inactive in the scene.
/// </summary>
public class PopupController : MonoBehaviour
{
    [Tooltip("The overlay GameObject to show/hide. Should start inactive in the scene.")]
    public GameObject root;
    public TMP_Text titleLabel;
    public TMP_Text messageLabel;

    public void Show(string title, string message)
    {
        if (titleLabel != null) titleLabel.text = title;
        if (messageLabel != null) messageLabel.text = message;
        if (root != null) root.SetActive(true);
    }

    /// <summary>Convenience for the shared "feature not ready" message.</summary>
    public void ShowComingSoon() =>
        Show("Coming soon", "This feature isn't available yet.\nCheck back in a future update.");

    public void Close()
    {
        if (root != null) root.SetActive(false);
    }
}

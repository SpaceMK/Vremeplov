using TalesTensor.Chat.Speech;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TalesTensor.Chat
{
    /// <summary>
    /// Drives speech-to-text into a chat input field, ported from the original
    /// TalesTensor project's MicButton. Tap to toggle listening; partial and final
    /// transcripts are written into the target input as the user speaks.
    ///
    /// Adapted for this app's code-built UI: instead of serialized prefab references it
    /// is wired up via <see cref="Initialize"/> by <see cref="ChatWindow"/>.
    /// </summary>
    public class MicButton : MonoBehaviour
    {
        Button _button;
        Graphic _tint;          // button background to colour by state
        TMP_Text _label;        // "Mic" / "Stop"
        TMP_InputField _target; // where transcribed text is written

        Color _idleColor;
        Color _listeningColor;

        ISpeechRecognizer _recognizer;
        string _baseText;

        public bool IsListening => _recognizer != null && _recognizer.IsListening;

        public void Initialize(Button button, Graphic tint, TMP_Text label, TMP_InputField target,
            Color idleColor, Color listeningColor)
        {
            _button = button;
            _tint = tint;
            _label = label;
            _target = target;
            _idleColor = idleColor;
            _listeningColor = listeningColor;

            _recognizer = SpeechRecognizerFactory.Create();
            _recognizer.PartialResult += HandlePartial;
            _recognizer.FinalResult += HandleFinal;
            _recognizer.Error += HandleError;
            _recognizer.Initialize();

            if (_button != null) _button.onClick.AddListener(Toggle);

            // Hide the button entirely where speech isn't supported (e.g. non-Windows
            // editor, or a device without the service) so it isn't a dead control.
            if (!_recognizer.IsSupported) gameObject.SetActive(false);

            ApplyIdleVisual();
        }

        void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(Toggle);
            if (_recognizer != null)
            {
                _recognizer.PartialResult -= HandlePartial;
                _recognizer.FinalResult -= HandleFinal;
                _recognizer.Error -= HandleError;
                _recognizer.Dispose();
                _recognizer = null;
            }
        }

        public void Toggle()
        {
            if (_recognizer == null) return;
            if (_recognizer.IsListening) StopListening();
            else StartListening();
        }

        public void StartListening()
        {
            if (_recognizer == null) return;
            if (!_recognizer.IsSupported)
            {
                Debug.LogWarning("[MicButton] Speech recognition not supported on this platform.");
                return;
            }
            _baseText = _target != null ? _target.text : string.Empty;
            ApplyListeningVisual();
            _recognizer.StartListening();
        }

        public void StopListening()
        {
            if (_recognizer == null) return;
            _recognizer.StopListening();
            ApplyIdleVisual();
        }

        public void SetInteractable(bool interactable)
        {
            if (_button != null) _button.interactable = interactable;
            if (!interactable && IsListening) StopListening();
        }

        void HandlePartial(string text) => WriteToInput(text);

        void HandleFinal(string text)
        {
            WriteToInput(text);
            // Promote the final result so subsequent partials append rather than overwrite.
            if (_target != null) _baseText = _target.text;
        }

        void HandleError(string error)
        {
            Debug.LogWarning("[MicButton] " + error);
            if (!IsListening) ApplyIdleVisual();
        }

        void WriteToInput(string text)
        {
            if (_target == null) return;
            _target.text = string.IsNullOrEmpty(_baseText) ? text : _baseText + " " + text;
        }

        void ApplyIdleVisual()
        {
            SetTint(_idleColor);
            if (_label != null) _label.text = "Mic";
        }

        void ApplyListeningVisual()
        {
            SetTint(_listeningColor);
            if (_label != null) _label.text = "Stop";
        }

        void SetTint(Color color)
        {
            // Drive the button's color block too, so its own state transitions don't
            // overwrite a direct tint on the next pointer event.
            if (_button != null)
            {
                ColorBlock c = _button.colors;
                c.normalColor = color;
                c.highlightedColor = color;
                c.selectedColor = color;
                _button.colors = c;
            }
            if (_tint != null) _tint.color = color;
        }
    }
}

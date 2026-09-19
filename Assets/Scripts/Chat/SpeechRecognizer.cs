using System;
using UnityEngine;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using UnityEngine.Windows.Speech;
#endif

namespace TalesTensor.Chat.Speech
{
    /// <summary>
    /// Platform-agnostic speech-to-text, ported from the original TalesTensor project.
    /// Windows (editor/standalone) uses Unity's built-in <c>DictationRecognizer</c>;
    /// Android/iOS device builds use the <c>com.yasirkula.speechtotext</c> package;
    /// everything else falls back to a no-op so the chat still builds and runs.
    /// </summary>
    public interface ISpeechRecognizer : IDisposable
    {
        bool IsSupported { get; }
        bool IsListening { get; }
        event Action<string> PartialResult;
        event Action<string> FinalResult;
        event Action<string> Error;
        void Initialize();
        void StartListening();
        void StopListening();
    }

    public static class SpeechRecognizerFactory
    {
        public static ISpeechRecognizer Create()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return new WindowsSpeechRecognizer();
#elif (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            return new MobileSpeechRecognizer();
#else
            return new NullSpeechRecognizer();
#endif
        }
    }

    public sealed class NullSpeechRecognizer : ISpeechRecognizer
    {
        public bool IsSupported => false;
        public bool IsListening => false;
        public event Action<string> PartialResult { add { } remove { } }
        public event Action<string> FinalResult { add { } remove { } }
        public event Action<string> Error;
        public void Initialize() { }
        public void StartListening() { Error?.Invoke("Speech recognition is not supported on this platform."); }
        public void StopListening() { }
        public void Dispose() { }
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    public sealed class WindowsSpeechRecognizer : ISpeechRecognizer
    {
        private DictationRecognizer m_recognizer;
        private bool m_listening;
        private bool m_initialized;

        public bool IsSupported => true;
        public bool IsListening => m_listening;

        public event Action<string> PartialResult;
        public event Action<string> FinalResult;
        public event Action<string> Error;

        public void Initialize()
        {
            if (m_initialized) return;
            m_initialized = true;
            try
            {
                m_recognizer = new DictationRecognizer();
                m_recognizer.DictationHypothesis += OnHypothesis;
                m_recognizer.DictationResult += OnResult;
                m_recognizer.DictationError += OnError;
                m_recognizer.DictationComplete += OnComplete;
            }
            catch (Exception e)
            {
                Error?.Invoke("Failed to initialize Windows speech: " + e.Message + ". Enable dictation in Settings > Privacy & security > Speech.");
            }
        }

        public void StartListening()
        {
            if (m_recognizer == null) { Error?.Invoke("Recognizer not initialized."); return; }
            if (m_listening) return;
            try
            {
                if (m_recognizer.Status != SpeechSystemStatus.Running)
                    m_recognizer.Start();
                m_listening = true;
            }
            catch (Exception e) { Error?.Invoke("Failed to start dictation: " + e.Message); }
        }

        public void StopListening()
        {
            if (m_recognizer == null) return;
            m_listening = false;
            try
            {
                if (m_recognizer.Status == SpeechSystemStatus.Running)
                    m_recognizer.Stop();
            }
            catch (Exception e) { Debug.LogWarning("Stop dictation error: " + e.Message); }
        }

        public void Dispose()
        {
            if (m_recognizer == null) return;
            m_recognizer.DictationHypothesis -= OnHypothesis;
            m_recognizer.DictationResult -= OnResult;
            m_recognizer.DictationError -= OnError;
            m_recognizer.DictationComplete -= OnComplete;
            try { m_recognizer.Dispose(); } catch { }
            m_recognizer = null;
        }

        private void OnHypothesis(string text) => PartialResult?.Invoke(text);
        private void OnResult(string text, ConfidenceLevel confidence) => FinalResult?.Invoke(text);
        private void OnError(string error, int hresult) => Error?.Invoke($"{error} (hresult {hresult})");

        private void OnComplete(DictationCompletionCause cause)
        {
            // Auto-restart on silence/timeout so the toggle keeps listening until the user stops.
            if (cause == DictationCompletionCause.Complete) return;
            if (!m_listening) return;
            try { m_recognizer.Start(); }
            catch (Exception e) { Error?.Invoke("Restart failed: " + e.Message); m_listening = false; }
        }
    }
#endif

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
    public sealed class MobileSpeechRecognizer : ISpeechRecognizer, ISpeechToTextListener
    {
        private bool m_listening;
        private bool m_initialized;
        private bool m_userWantsToListen;

        public bool IsSupported => SpeechToText.IsServiceAvailable();
        public bool IsListening => m_listening;

        public event Action<string> PartialResult;
        public event Action<string> FinalResult;
        public event Action<string> Error;

        public void Initialize()
        {
            if (m_initialized) return;
            m_initialized = true;
            try { SpeechToText.Initialize(); }
            catch (Exception e) { Error?.Invoke("Speech init failed: " + e.Message); }
        }

        public void StartListening()
        {
            if (m_userWantsToListen) return;
            m_userWantsToListen = true;
            SpeechToText.RequestPermissionAsync(permission =>
            {
                if (!m_userWantsToListen) return;
                if (permission != SpeechToText.Permission.Granted)
                {
                    Error?.Invoke("Microphone/Speech permission denied.");
                    m_userWantsToListen = false;
                    return;
                }
                BeginSession();
            });
        }

        public void StopListening()
        {
            m_userWantsToListen = false;
            m_listening = false;
            try { SpeechToText.ForceStop(); } catch { }
        }

        public void Dispose() => StopListening();

        private void BeginSession()
        {
            if (SpeechToText.IsBusy()) return;
            if (!SpeechToText.IsServiceAvailable())
            {
                Error?.Invoke("Speech service unavailable.");
                m_userWantsToListen = false;
                return;
            }
            if (!SpeechToText.Start(this))
            {
                Error?.Invoke("Couldn't start speech recognition session.");
                m_userWantsToListen = false;
                return;
            }
            m_listening = true;
        }

        void ISpeechToTextListener.OnReadyForSpeech() { }
        void ISpeechToTextListener.OnBeginningOfSpeech() { }
        void ISpeechToTextListener.OnVoiceLevelChanged(float normalizedVoiceLevel) { }
        void ISpeechToTextListener.OnPartialResultReceived(string spokenText) => PartialResult?.Invoke(spokenText);

        void ISpeechToTextListener.OnResultReceived(string spokenText, int? errorCode)
        {
            m_listening = false;

            // 0 = cancelled by Cancel(), 6 = no-speech timeout — both benign.
            if (errorCode.HasValue && errorCode.Value != 0 && errorCode.Value != 6)
                Error?.Invoke("Speech error code " + errorCode.Value);

            if (!string.IsNullOrEmpty(spokenText))
                FinalResult?.Invoke(spokenText);

            if (m_userWantsToListen)
                BeginSession();
        }
    }
#endif
}

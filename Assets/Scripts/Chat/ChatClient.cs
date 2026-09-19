using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace TalesTensor.Chat
{
    /// <summary>
    /// Talks to the TalesTensor character chatbot backend (ported from the original
    /// TalesTensor project's ChatManager). POSTs a question for a character and gets
    /// back a text reply plus optional base64 TTS audio.
    ///
    /// Protocol (unchanged from the source app):
    ///   POST {Endpoint}
    ///   headers: Content-Type/Accept application/json, X-SDK-Key: {SdkKey}
    ///   body:    { character_id, question, session_id, audio }
    ///
    /// Example response (kept here as a reference to work from — most fields aren't
    /// wired into gameplay yet; see <see cref="Result"/> for what's surfaced):
    /// <code>
    /// {
    ///   "reply": "I am King Charles II of England. What brings you to my court today?",
    ///   "audio_base64": null,            // base64 TTS (null when audio:false requested)
    ///   "character_id": "charles-ii",
    ///   "from_cache": false,
    ///   "emotion": "talking3",           // per-reply animation/expression cue
    ///   "state": {                        // relationship/progression with the player
    ///     "bucket": "warm_stranger",      // relationship tier
    ///     "emotion": 24,                  // numeric emotion
    ///     "mood": 24,                     // current mood
    ///     "reputation": 15                // player's standing with the character
    ///   }
    /// }
    /// </code>
    ///
    /// Networking only — no UI. <see cref="ChatWindow"/> drives it and renders the
    /// conversation. The coroutine is run by a host MonoBehaviour.
    /// </summary>
    public class ChatClient
    {
        // Defaults carried over verbatim from the source project so this hits the same
        // deployed Charles II bot with the same SDK key.
        public string Endpoint = "https://charles-ii-chatbot-production.up.railway.app/sdk/chat";
        public string SdkKey = "Russelltakethisforunity2842026";
        // When the player entered a live Quest Engine ar_interaction portal, ArSession
        // carries the character that scene calls for; otherwise fall back to Charles II.
        public string CharacterId =
            string.IsNullOrEmpty(ArSession.CharacterId) ? "charles-ii" : ArSession.CharacterId;
        /// <summary>Human-readable name for the character, shown at the top of the chat.</summary>
        public string CharacterName =
            string.IsNullOrEmpty(ArSession.CharacterName) ? "Charles II" : ArSession.CharacterName;
        public bool RequestAudio = true;

        /// <summary>Conversation/session key so the bot keeps context across turns.
        /// Defaults to a stable per-install id.</summary>
        public string SessionId =
            string.IsNullOrEmpty(SystemInfo.deviceUniqueIdentifier)
                ? Guid.NewGuid().ToString("N")
                : SystemInfo.deviceUniqueIdentifier;

        public struct Result
        {
            public bool ok;
            public string reply;
            public string audioBase64;
            public string error;
            // Surfaced for upcoming use (not consumed yet): the per-reply emotion cue and
            // the character's evolving relationship state with the player.
            public string emotion;
            public ChatState state;
        }

        /// <summary>Send <paramref name="question"/> and invoke <paramref name="onComplete"/>
        /// with the reply (or an error). Run via <c>StartCoroutine</c>.</summary>
        public IEnumerator Send(string question, Action<Result> onComplete)
        {
            var payload = new ChatRequest
            {
                character_id = CharacterId,
                question = question,
                session_id = SessionId,
                audio = RequestAudio,
            };
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));

            using var req = new UnityWebRequest(Endpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "application/json");
            req.SetRequestHeader("X-SDK-Key", SdkKey);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Chat] request failed: {req.responseCode} {req.error}\n{req.downloadHandler?.text}");
                onComplete?.Invoke(new Result { ok = false, error = req.error });
                yield break;
            }

            ChatResponse response;
            try
            {
                response = JsonUtility.FromJson<ChatResponse>(req.downloadHandler.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Chat] failed to parse reply: {e.Message}\n{req.downloadHandler.text}");
                onComplete?.Invoke(new Result { ok = false, error = "could not parse reply" });
                yield break;
            }

            if (response == null || string.IsNullOrEmpty(response.reply))
            {
                onComplete?.Invoke(new Result { ok = false, error = "empty reply" });
                yield break;
            }

            onComplete?.Invoke(new Result
            {
                ok = true,
                reply = response.reply,
                audioBase64 = response.audio_base64,
                emotion = response.emotion,
                state = response.state,
            });
        }

        [Serializable]
        private class ChatRequest
        {
            public string character_id;
            public string question;
            public string session_id;
            public bool audio;
        }

        [Serializable]
        private class ChatResponse
        {
            public string reply;
            public string audio_base64;
            public string emotion;
            public string character_id;
            public bool from_cache;
            public ChatState state;
        }

        /// <summary>The character's evolving relationship with the player, returned on
        /// every reply. Captured now for use shortly (mood/reputation UI, gated content,
        /// emotion-driven animation). JsonUtility leaves fields at 0/null if absent.</summary>
        [Serializable]
        public class ChatState
        {
            public string bucket;   // relationship tier, e.g. "warm_stranger"
            public int emotion;     // numeric emotion
            public int mood;        // current mood
            public int reputation;  // player's standing with the character
        }
    }
}

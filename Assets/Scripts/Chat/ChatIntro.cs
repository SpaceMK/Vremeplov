using System;
using UnityEngine;

namespace TalesTensor.Chat
{
    /// <summary>
    /// Warms the character's opening line so it's ready the instant the chat window
    /// opens. The AR scene kicks off <see cref="Preload"/> on entry (in parallel with
    /// the intro animation and floor scan); when <see cref="ChatWindow"/> later opens it
    /// calls <see cref="Request"/> and gets the reply immediately if it has arrived,
    /// or as soon as it does otherwise.
    ///
    /// Holds the shared <see cref="ChatClient"/> too, so the live conversation continues
    /// from the same session as the preloaded intro.
    /// </summary>
    public static class ChatIntro
    {
        /// <summary>The opening question the character is asked so it greets the player.</summary>
        public const string Message = "Can you introduce yourself to me";

        enum State { Idle, Loading, Ready }
        static State _state = State.Idle;
        static ChatClient _client;
        static ChatClient.Result _result;
        static Action<ChatClient.Result> _pending;

        /// <summary>The accepted quest, remembered so the chat can auto-ask about it as a
        /// follow-up right after the character introduces itself (kept separate so the
        /// intro reply is a clean introduction, not skipped in favour of the quest).</summary>
        public static string QuestName { get; private set; }

        /// <summary>True when the player has entered the quest a King's Scroll was for, so
        /// the opening line thanks them for the delivery instead of introducing.</summary>
        public static bool IsDelivery { get; private set; }

        /// <summary>Shared client (and session) for the whole AR conversation.</summary>
        public static ChatClient Client => _client ??= new ChatClient();

        /// <summary>Begin fetching the character's opening line now, if not already started,
        /// and remember <paramref name="questName"/> for the follow-up question.</summary>
        public static void Preload(MonoBehaviour host, string questName = null)
        {
            QuestName = questName;
            IsDelivery = !string.IsNullOrEmpty(questName) &&
                         QuestRewards.LetterQuest == questName;
            // A live ar_interaction portal picks the character to converse with; point the
            // shared client at it (overriding any stale cached client from a prior entry).
            if (!string.IsNullOrEmpty(ArSession.CharacterId)) Client.CharacterId = ArSession.CharacterId;
            if (!string.IsNullOrEmpty(ArSession.CharacterName)) Client.CharacterName = ArSession.CharacterName;
            if (_state != State.Idle || host == null) return;
            Begin(host);
        }

        static string OpeningPrompt() => IsDelivery
            ? "I have just delivered to you the sealed letter you were waiting for. Greet me " +
              "warmly, thank me for bringing it, and say it is most welcome."
            : Message;

        /// <summary>Deliver the intro reply: right away if ready, when it arrives if still
        /// loading, or kick off the request now if it was never preloaded.</summary>
        public static void Request(MonoBehaviour host, Action<ChatClient.Result> onReady)
        {
            if (onReady == null) return;
            switch (_state)
            {
                case State.Ready: onReady(_result); break;
                case State.Loading: _pending += onReady; break;
                default: _pending += onReady; Begin(host); break;
            }
        }

        static void Begin(MonoBehaviour host)
        {
            if (host == null) return;
            _state = State.Loading;
            host.StartCoroutine(Client.Send(OpeningPrompt(), r =>
            {
                _result = r;
                _state = State.Ready;
                var cb = _pending;
                _pending = null;
                cb?.Invoke(r);
            }));
        }

        /// <summary>Forget any cached intro/session so the next AR entry starts fresh.</summary>
        public static void Reset()
        {
            _state = State.Idle;
            _client = null;
            _result = default;
            _pending = null;
            QuestName = null;
        }
    }
}

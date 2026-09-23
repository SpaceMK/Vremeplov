namespace TalesTensor.Quests
{
    /// <summary>
    /// A single clue in a <see cref="ScriptedQuest"/>: the quick-ask chip the player taps,
    /// the character's fixed reply, and the short token revealed once it's uncovered.
    /// </summary>
    public class ScriptedQuestClue
    {
        public string chip;        // e.g. "What was your name?"
        public string answer;      // the character's fixed reply
        public string discovered;  // e.g. "GONXHE" — shown as a "CLUE DISCOVERED" banner

        public ScriptedQuestClue(string chip, string answer, string discovered)
        {
            this.chip = chip;
            this.answer = answer;
            this.discovered = discovered;
        }
    }

    /// <summary>
    /// A fully authored, fixed-text AR quest: unlike the live King chat (which streams
    /// replies from the chatbot backend), every line here is scripted, so the quest plays
    /// out deterministically. Presented by <c>ScriptedChatWindow</c> and looked up by quest
    /// name via <see cref="ScriptedQuestCatalog"/>; the AR scene spawns the matching
    /// character model and runs the scripted flow instead of the live chat.
    /// </summary>
    public class ScriptedQuest
    {
        public string questName;      // matches PinDefinition.questName / ArSession.QuestName
        public string characterName;  // shown at the top of the chat
        public string introTitle;     // large centred title on AR entry
        public string introSubtitle;  // one line under the title on AR entry
        public string opening;        // the character's self-introduction
        public string questFollowUp;  // sets up the clue hunt
        public ScriptedQuestClue[] clues;
        public string mystery;        // the reveal once every clue is found
        public string objective;      // the real-world objective banner
        public string objectiveButton;// the chip that confirms the objective is met
        public string completion;     // spoken on completing the objective
        public string rewardItemId;   // ItemCatalog id granted on completion
        public string rewardTitle;    // reward popup headline
        public string rewardDesc;     // reward popup subtitle
        public string handOff;        // the next-quest letter hand-off line ({0} = next quest)
        public string nextQuest;      // the quest the sealed letter is addressed to
    }

    /// <summary>The known scripted quests, keyed by quest name. Greenfield (one quest for
    /// now) but shaped so more can be added or a data-driven source can replace it.</summary>
    public static class ScriptedQuestCatalog
    {
        /// <summary>"The Skopje Calling" — Mother Teresa's origin mystery in Skopje.</summary>
        public static readonly ScriptedQuest SkopjeCalling = new ScriptedQuest
        {
            questName = "The Skopje Calling",
            characterName = "Mother Teresa",
            introTitle = "THE SKOPJE CALLING",
            introSubtitle = "Before she became known to the world as Mother Teresa, " +
                            "she was a girl from Skopje named Gonxhe.",
            opening =
                "My name was Anjeze Gonxhe Bojaxhiu. I was born here in Skopje on 26 August 1910, " +
                "and I was baptized the following day. Long before the world knew me as Mother Teresa, " +
                "this city was my home.",
            questFollowUp =
                "Then you must discover where my journey truly began.\n" +
                "Many remember Calcutta when they hear my name, but my story began here, in Skopje.\n" +
                "There are three clues you must uncover: my name, my faith, and my departure.\n" +
                "Find them, and you will understand how a girl from Skopje began a journey that carried " +
                "her across the world.",
            clues = new[]
            {
                new ScriptedQuestClue(
                    "What was your name?",
                    "I was born Anjeze Gonxhe Bojaxhiu.\n" +
                    "Gonxhe means flower bud in Albanian.\n" +
                    "But that is only the first clue. A name tells you where a story starts — " +
                    "not where it will lead.",
                    "GONXHE"),
                new ScriptedQuestClue(
                    "Where did your faith begin?",
                    "My family were Catholics, and I was baptized here in Skopje on 27 August 1910, " +
                    "the day after I was born.\n" +
                    "As a young girl, my life revolved around my family and the church community. " +
                    "Years later, I would say that from around the age of twelve I began to feel a " +
                    "calling toward missionary life.",
                    "27 AUGUST 1910"),
                new ScriptedQuestClue(
                    "When did you leave Skopje?",
                    "I was eighteen when I left my home in 1928.\n" +
                    "I travelled to join the Sisters of Loreto and eventually went to India.\n" +
                    "I did not know then where that road would take me. But my journey into the wider " +
                    "world began with my departure from Skopje.",
                    "1928"),
            },
            mystery =
                "Now you have the pieces.\n" +
                "Gonxhe.\n27 August 1910.\n1928.\n" +
                "People often search distant places for the beginning of my story. But sometimes the " +
                "answer is beneath their feet.\n" +
                "Find the place in modern Skopje that remembers the girl who once lived here.",
            objective = "Find the Mother Teresa Memorial House.",
            objectiveButton = "I found the Memorial House",
            completion =
                "You found it.\n" +
                "The Skopje of my childhood has changed greatly, but my connection to this city remains.\n" +
                "Remember that extraordinary journeys do not always begin in extraordinary places. " +
                "Sometimes they begin at home.",
            rewardItemId = ItemCatalog.GonxheFlower,
            rewardTitle = "Gonxhe's Flower",
            rewardDesc = "A symbol of the young Gonxhe Bojaxhiu and the beginning of a journey from " +
                         "Skopje to the wider world.",
            handOff =
                "But Skopje holds stories much older than mine.\n" +
                "I have something I would like you to carry.\n" +
                "Take this sealed letter to the keeper of {0}.\n" +
                "Perhaps they can show you another chapter hidden within this city.",
            nextQuest = "the Stone Bridge",
        };

        /// <summary>The scripted quest for <paramref name="questName"/>, or null if none.</summary>
        public static ScriptedQuest Get(string questName)
        {
            if (string.IsNullOrEmpty(questName)) return null;
            if (questName == SkopjeCalling.questName) return SkopjeCalling;
            return null;
        }

        /// <summary>True if <paramref name="questName"/> is a scripted quest.</summary>
        public static bool Has(string questName) => Get(questName) != null;
    }
}

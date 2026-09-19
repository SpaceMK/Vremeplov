/// <summary>Which AR experience the player entered from the map — decides what the
/// AR scene presents once it finds a floor.</summary>
public enum ArExperience
{
    /// <summary>The King conversation: places the King and opens the chat.</summary>
    KingChat,
    /// <summary>The Portal: spawns a person-sized oval gateway playing a video.</summary>
    Portal,
}

/// <summary>
/// Carries the details of the AR experience the player just accepted on the map
/// across the scene load into <c>ARScene</c>. Static so the value survives the
/// load (statics persist across scene changes); the AR scene reads these on start
/// to decide what to present.
///
/// Mirrors how <see cref="PlayerSession"/> hands a single shared record around,
/// but holds only the transient "what did we just enter" context.
/// </summary>
public static class ArSession
{
    /// <summary>Which kind of experience was entered. Defaults to the King chat so a
    /// direct editor boot behaves as before.</summary>
    public static ArExperience Experience { get; private set; } = ArExperience.KingChat;

    /// <summary>Quest name of the AR pin the player accepted, or null if the AR
    /// scene was entered directly (e.g. opened in the editor). Only meaningful for
    /// <see cref="ArExperience.KingChat"/>.</summary>
    public static string QuestName { get; private set; }

    /// <summary>Web URL of the video to play inside the portal. Only meaningful for
    /// <see cref="ArExperience.Portal"/>.</summary>
    public static string PortalVideoUrl { get; private set; }

    /// <summary>For a live <c>ar_interaction</c> portal, the character to converse with
    /// (e.g. "charles-ii"). Null ⇒ the AR scene uses its default character. Only
    /// meaningful for <see cref="ArExperience.KingChat"/>.</summary>
    public static string CharacterId { get; private set; }

    /// <summary>Display name for the live character, shown at the top of the chat.
    /// Null ⇒ default.</summary>
    public static string CharacterName { get; private set; }

    /// <summary>The live Quest Engine scene the player entered, or null for an offline
    /// demo experience. Carries the full scene brief (opening line, clue, dialogue, etc.)
    /// for the AR scene to use.</summary>
    public static TalesTensor.Quests.NearbyPortal Portal { get; private set; }

    /// <summary>Record a King-chat experience being entered (offline demo pin). Called by
    /// the AR pin just before it wipes into the AR scene.</summary>
    public static void Begin(string questName)
    {
        Experience = ArExperience.KingChat;
        QuestName = questName;
        PortalVideoUrl = null;
        CharacterId = null;
        CharacterName = null;
        Portal = null;
    }

    /// <summary>Record a live <c>ar_interaction</c> portal being entered, carrying the real
    /// character and scene brief.</summary>
    public static void BeginLiveChat(TalesTensor.Quests.NearbyPortal portal)
    {
        Experience = ArExperience.KingChat;
        QuestName = portal?.quest != null ? portal.quest.name : null;
        PortalVideoUrl = null;
        CharacterId = portal?.data != null ? portal.data.characterId : null;
        CharacterName = null; // resolved from the character system; name isn't in the brief
        Portal = portal;
    }

    /// <summary>Record a Portal (reconstruction) experience being entered, carrying the
    /// video to play and — for a live portal — its scene brief.</summary>
    public static void BeginPortal(string videoUrl, TalesTensor.Quests.NearbyPortal portal = null)
    {
        Experience = ArExperience.Portal;
        PortalVideoUrl = videoUrl;
        QuestName = null;
        CharacterId = null;
        CharacterName = null;
        Portal = portal;
    }

    /// <summary>Forget the current experience (e.g. when returning to the map).</summary>
    public static void Clear()
    {
        Experience = ArExperience.KingChat;
        QuestName = null;
        PortalVideoUrl = null;
        CharacterId = null;
        CharacterName = null;
        Portal = null;
    }
}

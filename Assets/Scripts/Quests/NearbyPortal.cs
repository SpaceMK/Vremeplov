using System;

namespace TalesTensor.Quests
{
    /// <summary>
    /// Typed model of the <c>GET /portals/nearby</c> response from the Quest Engine
    /// (see the backend's <c>UNITY_INTEGRATION.md</c>). One <see cref="NearbyPortal"/> is
    /// one scene/stop within a live, portal-ready quest.
    ///
    /// These are plain <c>[Serializable]</c> DTOs so Unity's built-in <c>JsonUtility</c>
    /// can parse them without the Newtonsoft package. Because JsonUtility has no
    /// polymorphism, <see cref="SceneData"/> is a SUPERSET of both the
    /// <c>portal_video</c> and <c>ar_interaction</c> shapes — fields for the mode that
    /// doesn't apply simply stay null/default. Always branch on <see cref="NearbyPortal.mode"/>
    /// before reading mode-specific fields.
    /// </summary>
    [Serializable]
    public class NearbyPortalsResponse
    {
        public NearbyPortal[] portals;
    }

    [Serializable]
    public class NearbyPortal
    {
        public string sceneId;
        public float distanceMeters;
        public string mode;              // "portal_video" | "ar_interaction"
        public string historicalPeriod;
        public string historicalBasis;
        public PortalLocation location;
        public PortalStage stage;
        public PortalAttempt attempt;
        public PortalQuest quest;
        public SceneData data;
        /// <summary>Render state of this scene's reconstruction video. Not part of the
        /// documented <c>/portals/nearby</c> response today — filled in client-side by
        /// <see cref="TimePortalClient"/>, and parsed directly once the Quest Engine starts
        /// returning a <c>video</c> object. Null ⇒ no video known; play the demo fallback.</summary>
        public PortalVideo video;

        public bool IsArInteraction => mode == "ar_interaction";
        public bool IsPortalVideo   => mode == "portal_video";
        /// <summary>True when this is a <c>portal_video</c> scene whose rendered video is ready to play.</summary>
        public bool HasPlayableVideo => IsPortalVideo && video != null && video.IsReady;
    }

    [Serializable]
    public class PortalLocation
    {
        public double lat;
        public double lng;
        public double standingPointLat;
        public double standingPointLng;
        public float heading;
        public string name;
        public string address;
    }

    [Serializable]
    public class PortalStage
    {
        public int orderIndex;
        public int totalScenes;
    }

    [Serializable]
    public class PortalAttempt
    {
        public string proposalId;
        public string title;
    }

    [Serializable]
    public class PortalQuest
    {
        public string id;
        public string name;
        public string city;
        public string storyTheme;
        public string visitorInterest;
        public QuestSpine spine;
    }

    [Serializable]
    public class QuestSpine
    {
        public string hook;
        public string playerRole;
        public string primaryGoal;
        public string resolution;
        public string reward;
        public string reflection;
    }

    /// <summary>Superset of both scene payload shapes — see the class remark above.</summary>
    [Serializable]
    public class SceneData
    {
        // --- portal_video ---
        public string visualReconstruction;
        public string[] charactersPresent;
        public string action;
        public DialogueLine[] dialogue;
        public string narration;
        public string clue;
        public string transition;

        // --- ar_interaction ---
        public string characterId;
        public string conversationalGoal;
        public string openingLine;
        public string knowledgeBoundary;
        public string[] canReveal;
        public string[] willHide;
        public string tone;
        public string[] fallbackResponses;
    }

    [Serializable]
    public class DialogueLine
    {
        public string speaker;
        public string line;
        public float timingSec;
    }
}

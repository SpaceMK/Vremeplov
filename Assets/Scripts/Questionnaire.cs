using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data model for the onboarding questionnaire shown in <c>QuestionScene</c>.
///
/// Answers are never right or wrong — each one contributes weight to one or more
/// profile <em>categories</em> (see <see cref="QuestionBank"/>). Summing those
/// contributions across the whole questionnaire produces a <see cref="UserProfile"/>
/// that we persist locally and use to tailor the experience.
/// </summary>
[Serializable]
public class CategoryWeight
{
    public string category;
    public int weight = 1;

    public CategoryWeight() { }
    public CategoryWeight(string category, int weight) { this.category = category; this.weight = weight; }
}

/// <summary>One selectable answer: a label plus the profile weights it adds.</summary>
[Serializable]
public class ProfileAnswer
{
    [TextArea] public string text;
    public List<CategoryWeight> contributions = new List<CategoryWeight>();

    public ProfileAnswer() { }
    public ProfileAnswer(string text, params CategoryWeight[] contributions)
    {
        this.text = text;
        this.contributions = new List<CategoryWeight>(contributions);
    }
}

/// <summary>A single question with 2–4 answers.</summary>
[Serializable]
public class ProfileQuestion
{
    [TextArea] public string text;
    public List<ProfileAnswer> answers = new List<ProfileAnswer>();

    public ProfileQuestion() { }
    public ProfileQuestion(string text, params ProfileAnswer[] answers)
    {
        this.text = text;
        this.answers = new List<ProfileAnswer>(answers);
    }
}

/// <summary>
/// The holiday-interest profile axes for Tales Tensor. Each answer nudges the
/// player along one or more of these, and the strongest at the end becomes their
/// <see cref="UserProfile.dominant"/> travel style. Kept as plain string constants
/// so the data and storage stay simple and JSON-friendly.
/// </summary>
public static class ProfileCategory
{
    public const string Adventure  = "Adventure";  // travel, exploring, getting out there
    public const string History    = "History";    // heritage, landmarks, the past
    public const string Food        = "Food";       // restaurants, markets, cuisine
    public const string Drink       = "Drink";      // wine, cocktails, breweries
    public const string Nightlife   = "Nightlife";  // clubs, live music, going out
    public const string Nature      = "Nature";     // outdoors, hikes, wildlife
    public const string Relaxation  = "Relaxation"; // beaches, spas, slow days
    public const string Culture     = "Culture";    // art, museums, local markets

    public static readonly string[] All =
    {
        Adventure, History, Food, Drink, Nightlife, Nature, Relaxation, Culture
    };
}

/// <summary>
/// The default set of 10 questions, profiling what a player likes to do on
/// holiday. Authored in code so the scene works with zero setup, but
/// <see cref="QuestionnaireController.questions"/> can override it from the
/// inspector. Deliberately varied: a mix of 2-, 3- and 4-answer questions.
/// </summary>
public static class QuestionBank
{
    static CategoryWeight W(string category, int weight) => new CategoryWeight(category, weight);

    public static List<ProfileQuestion> Default() => new List<ProfileQuestion>
    {
        new ProfileQuestion(
            "You've just landed somewhere new. What's first on the agenda?",
            new ProfileAnswer("Find the best local restaurant", W(ProfileCategory.Food, 2)),
            new ProfileAnswer("Head straight for a famous landmark", W(ProfileCategory.History, 2)),
            new ProfileAnswer("Set off and explore on foot", W(ProfileCategory.Adventure, 2)),
            new ProfileAnswer("Track down a cool rooftop bar", W(ProfileCategory.Drink, 1), W(ProfileCategory.Nightlife, 1))),

        new ProfileQuestion(
            "Pick your ideal evening abroad.",
            new ProfileAnswer("A long dinner with too many courses", W(ProfileCategory.Food, 2)),
            new ProfileAnswer("Bar-hopping and live music", W(ProfileCategory.Nightlife, 2)),
            new ProfileAnswer("A quiet glass of wine with a view", W(ProfileCategory.Drink, 1), W(ProfileCategory.Relaxation, 1)),
            new ProfileAnswer("A sunset hike to round off the day", W(ProfileCategory.Nature, 1), W(ProfileCategory.Adventure, 1))),

        new ProfileQuestion(
            "What's your dream view from the hotel window?",
            new ProfileAnswer("Bustling, neon-lit city streets", W(ProfileCategory.Nightlife, 1), W(ProfileCategory.Culture, 1)),
            new ProfileAnswer("Mountains or deep forest", W(ProfileCategory.Nature, 2)),
            new ProfileAnswer("A calm, empty beach", W(ProfileCategory.Relaxation, 2))),

        new ProfileQuestion(
            "A free day, no plans. You…",
            new ProfileAnswer("Join a guided history tour", W(ProfileCategory.History, 2)),
            new ProfileAnswer("Graze your way through a food market", W(ProfileCategory.Food, 2)),
            new ProfileAnswer("Rent a bike and just ride", W(ProfileCategory.Adventure, 2)),
            new ProfileAnswer("Find a spa and switch off", W(ProfileCategory.Relaxation, 2))),

        new ProfileQuestion(
            "Souvenir budget — where does it go?",
            new ProfileAnswer("Local crafts, art and little boutiques", W(ProfileCategory.Culture, 2)),
            new ProfileAnswer("Bottles of regional wine or spirits", W(ProfileCategory.Drink, 2))),

        new ProfileQuestion(
            "Which tour would you actually book?",
            new ProfileAnswer("A street-food crawl", W(ProfileCategory.Food, 2)),
            new ProfileAnswer("A brewery or vineyard tasting", W(ProfileCategory.Drink, 2)),
            new ProfileAnswer("A ghosts-and-legends walking tour", W(ProfileCategory.History, 1), W(ProfileCategory.Adventure, 1))),

        new ProfileQuestion(
            "Your perfect holiday soundtrack is…",
            new ProfileAnswer("A buzzing nightclub", W(ProfileCategory.Nightlife, 2)),
            new ProfileAnswer("Waves rolling onto the shore", W(ProfileCategory.Relaxation, 2)),
            new ProfileAnswer("Birdsong out on a trail", W(ProfileCategory.Nature, 2)),
            new ProfileAnswer("Chatter in a busy plaza", W(ProfileCategory.Culture, 1), W(ProfileCategory.Food, 1))),

        new ProfileQuestion(
            "How do you like to get around a new city?",
            new ProfileAnswer("On foot, happily getting a little lost", W(ProfileCategory.Adventure, 2)),
            new ProfileAnswer("Hopping between cafés and bars", W(ProfileCategory.Food, 1), W(ProfileCategory.Drink, 1)),
            new ProfileAnswer("Following a museum-and-monument route", W(ProfileCategory.History, 1), W(ProfileCategory.Culture, 1))),

        new ProfileQuestion(
            "It's raining all day. What's Plan B?",
            new ProfileAnswer("Gallery and museum hopping", W(ProfileCategory.Culture, 1), W(ProfileCategory.History, 1)),
            new ProfileAnswer("A cosy wine bar or old pub", W(ProfileCategory.Drink, 2)),
            new ProfileAnswer("Feasting through an indoor food hall", W(ProfileCategory.Food, 2)),
            new ProfileAnswer("Spa, a good book, and a nap", W(ProfileCategory.Relaxation, 2))),

        new ProfileQuestion(
            "Last night of the trip — how do you send it off?",
            new ProfileAnswer("A big night out dancing", W(ProfileCategory.Nightlife, 2)),
            new ProfileAnswer("A long, memorable farewell dinner", W(ProfileCategory.Food, 2))),
    };
}

/// <summary>
/// The local result of the questionnaire: a score per category, the dominant
/// archetype, and when it was completed. Serialized to PlayerPrefs as JSON by
/// <see cref="QuestionnaireStore"/>. Parallel lists are used (rather than a
/// Dictionary) because Unity's <see cref="JsonUtility"/> can't serialize maps.
/// </summary>
[Serializable]
public class UserProfile
{
    public List<string> categories = new List<string>();
    public List<int> scores = new List<int>();
    public string dominant;
    public long completedUtcTicks;

    public int Get(string category)
    {
        int i = categories.IndexOf(category);
        return i >= 0 ? scores[i] : 0;
    }

    public void Set(string category, int value)
    {
        int i = categories.IndexOf(category);
        if (i >= 0) { scores[i] = value; return; }
        categories.Add(category);
        scores.Add(value);
    }

    public void Add(string category, int delta) => Set(category, Get(category) + delta);

    /// <summary>The highest-scoring category (first one wins ties); null if empty.</summary>
    public string Dominant()
    {
        string best = null;
        int bestScore = int.MinValue;
        for (int i = 0; i < categories.Count; i++)
        {
            if (scores[i] > bestScore) { bestScore = scores[i]; best = categories[i]; }
        }
        return best;
    }
}

/// <summary>
/// Reads/writes the questionnaire result to PlayerPrefs. <see cref="IsComplete"/>
/// is what the app checks to decide whether a player still needs onboarding.
/// Mirrors the pattern of <see cref="UserDataStore"/>.
/// </summary>
public static class QuestionnaireStore
{
    const string CompletedKey = "questionnaireCompleted";
    const string ProfileKey   = "userProfile";

    /// <summary>True once the player has finished the questionnaire on this device.</summary>
    public static bool IsComplete() => PlayerPrefs.GetInt(CompletedKey, 0) == 1;

    public static UserProfile Load()
    {
        if (!PlayerPrefs.HasKey(ProfileKey)) return null;
        try { return JsonUtility.FromJson<UserProfile>(PlayerPrefs.GetString(ProfileKey)); }
        catch { return null; }
    }

    /// <summary>
    /// Persist the profile. <paramref name="markDirty"/> also schedules a cloud backup,
    /// which is what you want for a real answer from the player; pass false when the
    /// profile is arriving *from* the cloud (see <see cref="SaveBundle.ApplyToLocal"/>).
    ///
    /// The backup goes through <see cref="PlayerSession.Save"/> rather than poking
    /// <see cref="CloudSync"/> directly, because the questionnaire lives in its own
    /// PlayerPrefs keys and so never moves the save timestamp that decides which copy
    /// wins a conflict. Without this the answers were captured in the bundle but only
    /// ever uploaded when some unrelated change happened to trigger a push.
    /// </summary>
    public static void Save(UserProfile profile, bool markDirty = true)
    {
        if (profile == null) return;
        profile.completedUtcTicks = DateTime.UtcNow.Ticks;
        profile.dominant = profile.Dominant();
        PlayerPrefs.SetString(ProfileKey, JsonUtility.ToJson(profile));
        PlayerPrefs.SetInt(CompletedKey, 1);
        PlayerPrefs.Save();

        if (markDirty) PlayerSession.Save();
    }

    public static void Clear()
    {
        PlayerPrefs.DeleteKey(ProfileKey);
        PlayerPrefs.DeleteKey(CompletedKey);
        PlayerPrefs.Save();
    }
}

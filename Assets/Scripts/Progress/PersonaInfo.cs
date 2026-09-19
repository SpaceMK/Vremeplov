/// <summary>
/// Turns the onboarding questionnaire's dominant <see cref="ProfileCategory"/> into
/// a friendly "what kind of traveller you are" label + blurb for the profile screen.
/// The quiz result is persisted separately by <see cref="QuestionnaireStore"/>; this
/// is purely presentation over that stored category string.
/// </summary>
public readonly struct PersonaInfo
{
    public readonly string Title;
    public readonly string Blurb;

    PersonaInfo(string title, string blurb)
    {
        Title = title;
        Blurb = blurb;
    }

    /// <summary>The persona for a stored category string (one of <see cref="ProfileCategory"/>).</summary>
    public static PersonaInfo For(string category) => category switch
    {
        ProfileCategory.Adventure => new PersonaInfo("The Explorer",
            "You live for getting out there — new streets, new routes, the thrill of not quite knowing what's around the corner."),
        ProfileCategory.History => new PersonaInfo("The Historian",
            "Heritage, landmarks and the stories behind them. You travel to walk where history happened."),
        ProfileCategory.Food => new PersonaInfo("The Foodie",
            "You map a place by its kitchens and markets. The best memories are the ones you can taste."),
        ProfileCategory.Drink => new PersonaInfo("The Connoisseur",
            "A great vineyard, brewery or rooftop bar is your kind of landmark. You savour the local pour."),
        ProfileCategory.Nightlife => new PersonaInfo("The Night Owl",
            "Live music, busy bars and going out — you come alive after dark and chase the buzz of the night."),
        ProfileCategory.Nature => new PersonaInfo("The Naturalist",
            "Trails, wildlife and wide-open outdoors. You'd take a forest path over a city block any day."),
        ProfileCategory.Relaxation => new PersonaInfo("The Unwinder",
            "Slow days, calm beaches and switching off. For you, the best trip is a restful one."),
        ProfileCategory.Culture => new PersonaInfo("The Culture Seeker",
            "Art, museums and local markets. You travel to soak up what makes each place its own."),
        _ => new PersonaInfo("The Traveller",
            "Finish the intro quiz to discover your travel style."),
    };

    /// <summary>The current player's persona from their saved questionnaire result.</summary>
    public static PersonaInfo Current()
    {
        var profile = QuestionnaireStore.Load();
        return For(profile != null ? profile.dominant : null);
    }
}

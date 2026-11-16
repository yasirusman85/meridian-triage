namespace Meridian.Domain.Rules;

/// <summary>
/// The phrase data behind <see cref="EmergencyRules"/>, as .NET regular-expression fragments matched
/// case-insensitively against the message text exactly as received. Broad on purpose: a false positive is a person
/// lowering a job, a miss is a gas leak nobody looked at (eval README, "How the labels follow the rules").
/// Each list is tried in order; the first match is the phrase recorded on the job.
/// </summary>
internal static class EmergencyPhrases
{
    /// <summary>Separator between two words inside one sentence (spaces, commas, apostrophes, but not . ! ? or a new line).</summary>
    private const string S = @"[^\w.!?\n]+";

    /// <summary>Up to <paramref name="max"/> extra words between two parts of a phrase, inside one sentence.</summary>
    private static string Gap(int max) => $@"(?:{S}\w+){{0,{max}}}?{S}";

    // ---- gas -------------------------------------------------------------------------------------------------

    /// <summary>"gas" and its spellings: English, misspelt, Polish (gaz, gazu, gazem, gazie), Romanian (gaz, gazul,
    /// gaze), Urdu in Latin script (gas, gais) and Urdu script (گیس, with Arabic or Farsi kaf and yeh).</summary>
    private const string GasWord =
        @"\b(?:gas|gass|gasses|gases|gassy|gaz|gazu|gazem|gazie|gazul|gazului|gaze|gais)\b" +
        @"|(?<!\w)[گك][یي]س(?!\w)";

    private const string SmellWord = @"(?:smell\w*|smel|smelt|stink\w*|stench|whiff|odou?r\w*|reek\w*|pong\w*)";

    public static readonly IReadOnlyList<string> Gas = new[]
    {
        GasWord,
        // Gas described without the word: rotten eggs, sulphur.
        @"\brotten" + Gap(1) + @"eggs?\b",
        @"\beggy\b",
        @"\b" + SmellWord + Gap(4) + @"(?:eggs?|sulph?ur\w*|sulfur\w*)\b",
        @"\b(?:sulph?ur|sulfur)" + Gap(2) + SmellWord + @"\b",
    };

    // ---- carbon monoxide -------------------------------------------------------------------------------------

    public static readonly IReadOnlyList<string> CarbonMonoxide = new[]
    {
        @"\bcarbon" + S + @"?mono?x\w*",
        @"\bmono?xid\w*",
        // "CO alarm", "C.O. detector", and the common slip "CO2 alarm".
        @"\bc[\W_]?o2?[\W_]{0,3}(?:alarm|detector|monitor|sensor|alert|meter)s?\b",
        // Polish: czad (carbon monoxide), czujnik czadu, tlenek węgla.
        @"\bczad\w*",
        @"\btlen\w*" + S + @"w[eę]gla\b",
    };

    // ---- water through a ceiling -----------------------------------------------------------------------------

    private const string WaterWord =
        @"(?:water\w*|wet|leak\w*|leek\w*|drip\w*|pour\w*|flood\w*|gush\w*|seep\w*|trickl\w*|cascad\w*|running" +
        // Polish (woda, cieknie, kapie, przecieka), Romanian (apă, curge, picură), Urdu in Latin script (pani).
        @"|wod[aąęy]|ciek\w*|kap(?:ie|i[aą])\w*|przeciek\w*|ap[aă]|curge|picur\w*|pani)";

    /// <summary>"ceiling" and its common misspellings (cieling, celing, ceilling, ceilin), Polish sufit, Romanian
    /// tavan and plafon, Urdu in Latin script chhat; and a light fitting, which hangs from a ceiling.</summary>
    private const string CeilingWord =
        @"(?:c(?:ei|ie|ea|ee|e|i)l{1,2}(?:i|e|y)?n[gk]?s?|sufit\w*|tavan\w*|plafon\w*|chhatt?" +
        @"|(?:light|lite|lamp)" + S + @"?(?:fitting|fiting|fittin|fitin|fixture)s?)";

    public static readonly IReadOnlyList<string> WaterThroughCeiling = new[]
    {
        @"\b" + WaterWord + Gap(6) + CeilingWord + @"\b",
        @"\b" + CeilingWord + Gap(6) + WaterWord + @"\b",
    };

    // ---- no heating or hot water (October to April only) -----------------------------------------------------

    private const string HeatingWord =
        @"(?:h(?:ea|ee|e)t(?:ing|in|ng|er|ers)?|boilers?|radiators?|rads|hot" + S + @"?water|central" + S + "heating)";

    private const string NoWord =
        @"(?:no|without|zero|lost" + S + @"(?:the|all|our|my)|lost|lack" + S + "of|have" + S + "no|got" + S + "no" +
        @"|don\W?t" + S + "have|haven\\W?t" + S + "got|haven\\W?t" + S + "had)";

    private const string BrokenWord =
        @"(?:not|isn\W?t|isnt|aren\W?t|arent|won\W?t|wont|doesn\W?t|doesnt|don\W?t|dont|didn\W?t|didnt|hasn\W?t|hasnt" +
        @"|stopped|stoped|packed|broken|broke|died|dead|failed|failing|gone|cold|kaput|bust)";

    public static readonly IReadOnlyList<string> NoHeating = new[]
    {
        // "no heating", "no hot water", "without heating", "no proper heat".
        @"\b" + NoWord + S + @"(?:\w+" + S + @")?" + HeatingWord + @"\b",
        // "heating isn't working", "boiler's stopped", "radiators stone cold", "heating has packed in".
        @"\b" + HeatingWord + Gap(3) + BrokenWord + @"\b",
        // Polish: nie ma / brak / bez ogrzewania, ciepłej wody; ogrzewanie / piec / kaloryfery nie działa(ją).
        @"\b(?:nie" + S + "ma|brak|bez)" + S + @"(?:\w+" + S + @")?(?:ogrzewani\w*|ciep[łl]ej" + S + @"wody)",
        @"\b(?:ogrzewani\w*|piec\w*|kaloryfer\w*|grzejnik\w*)" + Gap(2) + @"nie\b",
        // Romanian: nu avem / fără căldură, apă caldă, încălzire; centrala nu merge.
        @"\b(?:nu" + S + @"(?:avem|am|este|e|mai" + S + @"avem)|f[aă]r[aă])" + S + @"(?:\w+" + S + @")?" +
            @"(?:c[aă]ldur[aă]|ap[aă]" + S + @"cald[aă]|[iî]nc[aă]lzire)",
        @"\b(?:central[aei]\w*|calorifer\w*|[iî]nc[aă]lzirea)" + Gap(2) + @"nu\b",
    };
}

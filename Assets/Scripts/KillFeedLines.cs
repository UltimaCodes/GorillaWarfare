using UnityEngine;

/// <summary>
/// What the kill feed actually says.
///
/// "Killer «Weapon» Victim" is what a serious shooter prints, and this is a game about gorillas
/// shooting each other with fruit. A line that reads like a sentence is funnier, gets read, and
/// costs nothing.
///
/// Every line is picked from a roll the dying player generates and sends with their death, so
/// everyone in the room reads the same sentence. Choosing locally would mean the same kill said
/// something different on each screen, which is exactly the sort of detail people notice when
/// they're all shouting about it.
/// </summary>
public static class KillFeedLines
{
    // {0} is the killer, {1} is whoever they got.
    static readonly string[] Generic =
    {
        "{0} peeled {1}",
        "{0} turned {1} into a smoothie",
        "{1} got mulched by {0}",
        "{0} slipped {1} a bad one",
        "{1} caught {0}'s five a day",
        "{0} bruised {1} beyond recognition",
        "{1} got composted by {0}",
        "{0} sent {1} back to the bunch",
        "{1} was blended by {0}",
        "{0} put {1} in the fruit bowl",
        "{1} got pulped by {0}",
        "{0} made {1} go brown",
        "{1} found out what {0}'s banana does",
        "{0} split {1}",
        "{1} got potassium poisoning courtesy of {0}",
        "{0} har­vested {1}",
    };

    static readonly string[] Headshot =
    {
        "{0} took {1}'s head clean off",
        "{1} lost the top of the banana to {0}",
        "{0} de-stemmed {1}",
        "{1} got a face full of fruit from {0}",
        "{0} cored {1}",
    };

    // Named per weapon key rather than per display name, so renaming a banana doesn't silently
    // drop it back to the generic list.
    static readonly string[] Peel =
    {
        "{0} beat {1} to death with the packaging",
        "{1} slipped on {0}",
        "{0} slapped {1} into next week",
        "{1} got got by a piece of litter",
    };

    static readonly string[] Sniper =
    {
        "{0} reached across the map and got {1}",
        "{1} never saw the banana coming",
        "{0} sniped {1} with a piece of fruit, somehow",
    };

    static readonly string[] Shotgun =
    {
        "{0} gave {1} both barrels of breakfast",
        "{1} got double-bananaed by {0}",
        "{0} deleted {1} at conversational distance",
    };

    static readonly string[] SelfInflicted =
    {
        "{1} found the void",
        "{1} left the map and did not come back",
        "{1} forgot the floor was optional",
        "{1} deleted themselves",
    };

    /// <param name="flavour">
    /// The roll the dying player sent. Same number everywhere, so the same sentence everywhere.
    /// </param>
    /// <summary>
    /// Getting your own back on whoever killed you last.
    ///
    /// Beats every other line including the headshot one. Between two people who keep killing
    /// each other, the fact that it is the fourth time running is funnier than where the shot
    /// landed.
    /// </summary>
    static readonly string[] Revenge =
    {
        "{0} finally got {1} back",
        "{0} settled up with {1}",
        "{0} returned the favour to {1}",
        "{0} was not letting that go, {1}",
        "{0} remembered exactly what {1} did",
        "{1} should have run when they had the chance ({0})",
    };

    public static string For(string killer, string victim, string weaponKey, bool headshot,
                             byte flavour, bool revenge = false)
    {
        if (string.IsNullOrEmpty(killer))
            return Format(SelfInflicted, flavour, killer, victim);

        if (revenge)
            return Format(Revenge, flavour, killer, victim);

        // A headshot is the more interesting fact, so it wins over the weapon - except for the
        // peel, where being beaten to death with rubbish is funnier than where it landed.
        if (weaponKey == "Peel")
            return Format(Peel, flavour, killer, victim);

        if (headshot)
            return Format(Headshot, flavour, killer, victim);

        switch (weaponKey)
        {
            case "Sniper": return Format(Sniper, flavour, killer, victim);
            case "Shotgun": return Format(Shotgun, flavour, killer, victim);
            default: return Format(Generic, flavour, killer, victim);
        }
    }

    static string Format(string[] lines, byte flavour, string killer, string victim)
    {
        if (lines.Length == 0)
            return $"{killer} killed {victim}";

        return string.Format(lines[flavour % lines.Length], killer, victim);
    }
}

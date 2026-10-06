using System;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// One thing the gods can do to you when a Death Link lands in chaos mode.
/// </summary>
/// <remarks>
/// Apply returns false when there was nothing to act on, such as a famine with no followers or
/// locusts with nothing planted. The roller then picks another rather than spending the death on
/// an effect the player would never notice.
/// </remarks>
internal class Plague
{
    internal Plague(string name, bool crusadeOnly, Func<string> apply)
    {
        Name = name;
        CrusadeOnly = crusadeOnly;
        Apply = apply;
    }

    /// <summary>For the log, not the player.</summary>
    internal string Name { get; }

    /// <summary>Acts on run state that does not exist at base.</summary>
    internal bool CrusadeOnly { get; }

    /// <summary>Returns the line to show the player, or null if it could not act.</summary>
    internal Func<string> Apply { get; }
}

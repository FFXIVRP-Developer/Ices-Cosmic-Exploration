using System.Collections.Generic;

namespace ICE.ConfigFiles;

// Fork: settings of the fork's additions (README-FORK.md). Kept in their own file so the upstream config stays untouched.
public partial class Config
{
    /// <summary>Fork item 3: between missions, collect the retainers at a summoning bell near the character (AutoRetainer).</summary>
    public bool Fork_RetainersBetweenMissions { get; set; } = true;

    /// <summary>
    ///     Fork item 3: AutoRetainer's own "Artisan integration" while ICE holds it off (it stops Artisan mid-mission when a bell
    ///     is in reach). Null when nothing is held; given back to AutoRetainer once ICE is idle, also after a crash or reload.
    /// </summary>
    public bool? Fork_ArtisanIntegrationHeld { get; set; } = null;

    /// <summary>Fork item 6: hand in every class's relic tool whose research is complete, not only the class ICE works on.</summary>
    public bool Fork_RelicAllClasses { get; set; } = true;

    /// <summary>
    ///     Fork item 1: a relic tool is only handed in when the class can wear what the hand-in gives (and its current tool is
    ///     on hand). Off: any class whose research is complete hands in, whatever its level.
    /// </summary>
    public bool Fork_RelicLevelGate { get; set; } = true;

    /// <summary>
    ///     Fork item 6: per class, the research reading the NPC refused a hand-in at. That class is not tried again while its
    ///     reading (stage, data, level, unlocked stage) stays the same; kept here so a reload does not send ICE back for it.
    /// </summary>
    public Dictionary<uint, string> Fork_RelicRefusals { get; set; } = [];
}

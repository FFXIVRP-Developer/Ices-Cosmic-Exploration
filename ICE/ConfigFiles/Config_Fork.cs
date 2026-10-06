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
}

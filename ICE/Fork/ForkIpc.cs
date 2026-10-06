using ICE.Utilities.Cosmic_Helper;
using ECommons.EzIpcManager;

namespace ICE.Fork;

/// <summary>
///     Fork item 4: IPC for the fork's additions, next to upstream's (same "ICE." prefix). Lets another plugin (BoatRunner)
///     ask ICE directly instead of reaching into its insides by reflection.
/// </summary>
public class ForkIpc
{
    public ForkIpc() => EzIPC.Init(this, "ICE");

    /// <summary>True: this ICE carries the fork's guards (relic level gate, mount guard, AutoRetainer handling).</summary>
    [EzIPC] public bool IsFork() => true;

    /// <summary>Why a relic hand-in for this job (8-18) would be held now; "" when it may go ahead.</summary>
    [EzIPC] public string RelicHandInBlocker(uint job) => ForkRelicGuard.Blocker(job) ?? "";

    /// <summary>ICE is at a summoning bell for the retainers, between missions.</summary>
    [EzIPC] public bool IsInRetainerBreak() => ForkAutoRetainer.InBreak;

    /// <summary>Reads a setting: TurninRelic, StopAfterCurrent, RetainersBetweenMissions. False for an unknown name.</summary>
    [EzIPC] public bool GetSetting(string name) => name switch
    {
        "TurninRelic" => C.TurninRelic,
        "StopAfterCurrent" => Mission_Settings.StopAfterCurrent,
        "RetainersBetweenMissions" => C.Fork_RetainersBetweenMissions,
        _ => false,
    };

    /// <summary>Sets TurninRelic or RetainersBetweenMissions (upstream's ChangeSetting covers neither); saved.</summary>
    [EzIPC] public void SetSetting(string name, bool on)
    {
        switch (name)
        {
            case "TurninRelic": C.TurninRelic = on; break;
            case "RetainersBetweenMissions": C.Fork_RetainersBetweenMissions = on; break;
            default: return;
        }
        IceLogging.Info($"Setting {name} = {on} (IPC)", "[Fork: IPC]");
        C.Save();
    }
}

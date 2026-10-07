using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using ICE.Utilities.Cosmic_Helper;

namespace ICE.Fork;

/// <summary>
///     Fork item 5: a job swap to a job with no gear set is skipped, never retried forever. Upstream's relic hand-in swaps to the
///     "relic battle job" (or the mission job) and waits until the swap is done, asking every second; with no gear set for that job
///     the swap can never happen (GearsetHandler only logs "the gearset doesn't exist"), and ICE stood at the hub with no mission
///     (2026-10-07: the battle job was Warrior, the character had no Warrior gear set; ICE was restarted three times, each time back
///     at the hub). The swap is skipped and the hand-in goes on as the class worn, which the relic guard (item 1) still judges.
/// </summary>
internal static class ForkJobSwapGuard
{
    private const string Tag = "[Fork: Job swap guard]";

    /// <summary>True when a gear set for the job exists (the swap can happen); false, logged once a minute, when none does.</summary>
    public static unsafe bool CanSwapTo(uint job)
    {
        var module = RaptureGearsetModule.Instance();
        if (module is not null)
            for (var id = 0; id < 100; id++)
                if (module->IsValidGearset(id) && module->GetGearset(id)->ClassJob == job)
                    return true;
        if (EzThrottler.Throttle($"Fork job swap guard {job}", 60_000))
            IceLogging.Info($"No gear set for job {job}: the swap to it is skipped (it could never happen).", Tag);
        return false;
    }
}

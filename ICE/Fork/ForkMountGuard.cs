using ECommons.GameHelpers;
using ICE.Utilities.Cosmic_Helper;

namespace ICE.Fork;

/// <summary>
///     Fork item 2: no crafting while mounted (the game refuses it, and the mission timer runs on). A crafting mission is
///     not taken while mounted, and Artisan is not told to craft until the character is on foot.
/// </summary>
internal static class ForkMountGuard
{
    private const string Tag = "[Fork: Mount guard]";

    /// <summary>Task step before a mission is taken: true when it needs no crafting or the character is on foot; else dismounts.</summary>
    public static bool? ReadyForMission(uint missionId)
    {
        if (!CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var mission) || !mission.Attributes.HasFlag(MissionAttributes.Craft))
            return true;
        return ReadyToCraft();
    }

    /// <summary>True when on foot; else dismounts (throttled) and answers false, to be asked again.</summary>
    public static bool ReadyToCraft()
    {
        if (!Player.Mounted && !Player.IsJumping) return true;
        if (EzThrottler.Throttle("Fork mount guard: dismount", 1000))
        {
            IceLogging.Info("Mounted: dismounting before crafting.", Tag);
            Utils.Dismount();
        }
        return false;
    }
}

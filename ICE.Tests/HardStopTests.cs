using ICE.Fork;

namespace ICE.Tests;

/// <summary>
///     Fork item 7. User, 2026-10-08: "hard stop is when the character is walking to a new node or the character ends a craft, it
///     ends it all, abandons the mission by getting out of the crafter table and stops" (BoatRunner asks it; the soft stop is
///     upstream's "stop after current", which lets the mission end however long it takes).
/// </summary>
public sealed class HardStopTests
{
    [Theory]
    [InlineData(true, false, false, false)]  // a craft action under way: never cut mid-synthesis
    [InlineData(false, true, false, false)]  // the synthesis window still up (between steps of a craft)
    [InlineData(false, false, true, false)]  // at a gathering node: until it leaves for the next one
    [InlineData(false, false, false, true)]  // a craft just ended / walking: now
    public void GivenWhatTheCharacterIsDoing_ExpectTheHardStopOnlyAtASafeMoment(bool executingCraft, bool synthesisOpen, bool atNode, bool now) =>
        Assert.Equal(now, ForkHardStop.SafeMoment(executingCraft, synthesisOpen, atNode));
}

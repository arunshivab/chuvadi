using Chuvadi.Print.ManualTests;

namespace Chuvadi.Print.Tests;

public class VerificationGroupTests
{
    [Fact] public void DefaultSettingsAreSensible() => VerificationGroups.DefaultSettingsAreSensible();
    [Fact] public void SettingsCloneIsIndependent() => VerificationGroups.SettingsCloneIsIndependent();
    [Fact] public void PaperSizeConversionsAreCorrect() => VerificationGroups.PaperSizeConversionsAreCorrect();
    [Fact] public void MarginPresetsAreCorrect() => VerificationGroups.MarginPresetsAreCorrect();
    [Fact] public void AllAlignmentsDistinct() => VerificationGroups.AllAlignmentsDistinct();
    [Fact] public void PageSelectionResolvesEveryMode() => VerificationGroups.PageSelectionResolvesEveryMode();
    [Fact] public void PageSelectionRejectsBadRange() => VerificationGroups.PageSelectionRejectsBadRange();
    [Fact] public void PageSelectionRoundTripsThroughCanonical() => VerificationGroups.PageSelectionRoundTripsThroughCanonical();
    [Fact] public void SpoolEnvelopeRoundTrips() => VerificationGroups.SpoolEnvelopeRoundTrips();
    [Fact] public void SpoolEnvelopeDetectsCorruption() => VerificationGroups.SpoolEnvelopeDetectsCorruption();
    [Fact] public void SpoolEnvelopeRejectsForeignData() => VerificationGroups.SpoolEnvelopeRejectsForeignData();
}

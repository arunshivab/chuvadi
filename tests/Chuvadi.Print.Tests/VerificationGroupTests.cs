using Chuvadi.Print.ManualTests;

namespace Chuvadi.Print.Tests;

public class VerificationGroupTests
{
    // ---- A1
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

    // ---- A2: layout
    [Fact] public void LayoutActualSizeHonoursAllNineAlignments() => VerificationGroups.LayoutActualSizeHonoursAllNineAlignments();
    [Fact] public void LayoutScaleModesComputeExpectedFactors() => VerificationGroups.LayoutScaleModesComputeExpectedFactors();
    [Fact] public void LayoutOrientationShapesSheetAndRotation() => VerificationGroups.LayoutOrientationShapesSheetAndRotation();
    [Fact] public void LayoutAutoRotateTurnsMismatchedPages() => VerificationGroups.LayoutAutoRotateTurnsMismatchedPages();
    [Fact] public void LayoutMarginReferenceModesDiffer() => VerificationGroups.LayoutMarginReferenceModesDiffer();
    [Fact] public void LayoutFlagsClippingAndRejectsImpossibleMargins() => VerificationGroups.LayoutFlagsClippingAndRejectsImpossibleMargins();

    // ---- A2: plan
    [Fact] public void PlanExpandsCollatedAndUncollatedCopies() => VerificationGroups.PlanExpandsCollatedAndUncollatedCopies();
    [Fact] public void PlanDuplexPairsSidesAndInsertsBlankBacks() => VerificationGroups.PlanDuplexPairsSidesAndInsertsBlankBacks();
    [Fact] public void PlanDefersCopiesToCapableTarget() => VerificationGroups.PlanDefersCopiesToCapableTarget();

    // ---- A2: PrintJob
    [Fact] public void PrintToEmitsPlannedImpressionsInOrder() => VerificationGroups.PrintToEmitsPlannedImpressionsInOrder();
    [Fact] public void PrintToRendersAtDeviceResolution() => VerificationGroups.PrintToRendersAtDeviceResolution();
    [Fact] public void PrintToKeepsMemoryBounded() => VerificationGroups.PrintToKeepsMemoryBounded();
    [Fact] public void PrintToDeliversRequestedColourMode() => VerificationGroups.PrintToDeliversRequestedColourMode();
    [Fact] public void PrintToRequiresRasterizer() => VerificationGroups.PrintToRequiresRasterizer();
    [Fact] public void PrintToAuditsExactlyOnceOnSuccess() => VerificationGroups.PrintToAuditsExactlyOnceOnSuccess();
    [Fact] public void PrintToReportsFailureAndAuditsOnce() => VerificationGroups.PrintToReportsFailureAndAuditsOnce();
    [Fact] public void PrintToReportsCancellationAndAuditsOnce() => VerificationGroups.PrintToReportsCancellationAndAuditsOnce();
    [Fact] public void PrintToRethrowsWhenAsked() => VerificationGroups.PrintToRethrowsWhenAsked();
    [Fact] public void PrintToReportsProgress() => VerificationGroups.PrintToReportsProgress();
    [Fact] public void FluentSettersCoverEverySetting() => VerificationGroups.FluentSettersCoverEverySetting();

    // ---- A2: spool, conversion, PNG
    [Fact] public void SpoolRoundTripsThroughPrintJob() => VerificationGroups.SpoolRoundTripsThroughPrintJob();
    [Fact] public void SpoolEnvelopeStillReadsVersion1() => VerificationGroups.SpoolEnvelopeStillReadsVersion1();
    [Fact] public void MonochromeConversionsProduceDistinctOutput() => VerificationGroups.MonochromeConversionsProduceDistinctOutput();
    [Fact] public void ImageFileTargetWritesValidPngs() => VerificationGroups.ImageFileTargetWritesValidPngs();

    // ---- A3: Windows (pass trivially where they do not apply)
    [Fact] public void WindowsPrintersAreDiscoverable() => RunUnlessSkipped(VerificationGroups.WindowsPrintersAreDiscoverable);
    [Fact] public void WindowsPrintToPdfWritesFile() => RunUnlessSkipped(VerificationGroups.WindowsPrintToPdfWritesFile);

    private static void RunUnlessSkipped(Action group)
    {
        try { group(); }
        catch (VerificationSkippedException) { }
    }
}

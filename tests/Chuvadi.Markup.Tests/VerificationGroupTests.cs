using Chuvadi.Markup.ManualTests;

namespace Chuvadi.Markup.Tests;

public class VerificationGroupTests
{
    [Fact] public void MarkdownReadmeRendersAsGfm() => VerificationGroups.MarkdownReadmeRendersAsGfm();
    [Fact] public void MarkdownCommonMarkProfileHasNoExtensions() => VerificationGroups.MarkdownCommonMarkProfileHasNoExtensions();
    [Fact] public void MarkdownRawHtmlModes() => VerificationGroups.MarkdownRawHtmlModes();
    [Fact] public void MarkdownUnsafeUrlsAreRemoved() => VerificationGroups.MarkdownUnsafeUrlsAreRemoved();
    [Fact] public void MarkdownAstIsNavigable() => VerificationGroups.MarkdownAstIsNavigable();
    [Fact] public void MarkdownWriterRoundTrips() => VerificationGroups.MarkdownWriterRoundTrips();
    [Fact] public void MarkdownThroughReaderSanitizerIsSafe() => VerificationGroups.MarkdownThroughReaderSanitizerIsSafe();
    [Fact] public void NewsletterSanitizesForAnjal() => VerificationGroups.NewsletterSanitizesForAnjal();
    [Fact] public void NewsletterSanitizesForReader() => VerificationGroups.NewsletterSanitizesForReader();
    [Fact] public void CidImagesResolveThroughHost() => VerificationGroups.CidImagesResolveThroughHost();
    [Fact] public void Windows1252MailDecodes() => VerificationGroups.Windows1252MailDecodes();
    [Fact] public void SanitizedOutputIsStable() => VerificationGroups.SanitizedOutputIsStable();
    [Fact] public void PlainTextReplyBecomesSafeHtml() => VerificationGroups.PlainTextReplyBecomesSafeHtml();
    [Fact] public void FormatFlowedJoinsLines() => VerificationGroups.FormatFlowedJoinsLines();
    [Fact] public void HtmlDomEditAndSerialize() => VerificationGroups.HtmlDomEditAndSerialize();
    [Fact] public void HostileInputStaysBounded() => VerificationGroups.HostileInputStaysBounded();
    [Fact] public void CancellationStopsWork() => VerificationGroups.CancellationStopsWork();
}

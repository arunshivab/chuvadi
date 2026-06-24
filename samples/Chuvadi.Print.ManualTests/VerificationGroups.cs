using System;
using System.Collections.Generic;
using Chuvadi.Print;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>
    /// Each method performs a self-contained verification and throws on failure.
    /// The console Program runs them all; the xUnit Tests project drives each as a [Fact].
    /// </summary>
    public static class VerificationGroups
    {
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Verification failed: " + message);
        }

        public static void DefaultSettingsAreSensible()
        {
            var s = new PrintSettings();
            Require(s.Copies == 1, "default copies should be 1");
            Require(s.Collate, "default collate should be true");
            Require(s.Duplex == Duplex.Simplex, "default duplex");
            Require(s.Color == ColorMode.Color, "default colour");
            Require(s.Orientation == PageOrientation.Portrait, "default orientation");
            Require(s.Scale == ScaleMode.FitToPage, "default scale");
            Require(s.Paper == PaperSize.A4, "default paper A4");
            Require(s.Alignment == ContentAlignment.Center, "default alignment centre");
            Require(!s.Silent, "default silent false");
        }

        public static void SettingsCloneIsIndependent()
        {
            var a = new PrintSettings { Copies = 3, Duplex = Duplex.Horizontal };
            var b = a.Clone();
            b.Copies = 9;
            Require(a.Copies == 3, "clone must not affect original");
            Require(b.Copies == 9 && b.Duplex == Duplex.Horizontal, "clone copies values");
        }

        public static void PaperSizeConversionsAreCorrect()
        {
            var a4 = PaperSize.A4;
            Require(Math.Abs(a4.WidthMillimetres - 210.0) < 0.01, "A4 width 210mm");
            Require(Math.Abs(a4.HeightMillimetres - 297.0) < 0.01, "A4 height 297mm");
            var letter = PaperSize.Letter;
            Require(Math.Abs(letter.WidthPoints - 612.0) < 0.01, "Letter width 612pt");
            Require(Math.Abs(letter.HeightPoints - 792.0) < 0.01, "Letter height 792pt");
            Require(PaperSize.A4.Rotate() == PaperSize.A4.Rotate(), "rotate stable");
            Require(PaperSize.A4 == PaperSize.FromMillimetres(210, 297), "A4 equality by dimensions");
        }

        public static void MarginPresetsAreCorrect()
        {
            Require(Margins.None == new Margins(0, 0, 0, 0), "none margins");
            Require(Math.Abs(Margins.Default.Left - 36.0) < 0.01, "default 0.5in = 36pt");
            Require(Math.Abs(Margins.Wide.Top - 72.0) < 0.01, "wide 1in = 72pt");
        }

        public static void AllAlignmentsDistinct()
        {
            var all = new HashSet<ContentAlignment>
            {
                ContentAlignment.TopLeft, ContentAlignment.TopCenter, ContentAlignment.TopRight,
                ContentAlignment.CenterLeft, ContentAlignment.Center, ContentAlignment.CenterRight,
                ContentAlignment.BottomLeft, ContentAlignment.BottomCenter, ContentAlignment.BottomRight
            };
            Require(all.Count == 9, "nine distinct alignments");
        }

        public static void PageSelectionResolvesEveryMode()
        {
            Require(SeqEqual(PageSelection.All.Resolve(3), 1, 2, 3), "all");
            Require(PageSelection.None.Resolve(3).Count == 0, "none");
            Require(SeqEqual(PageSelection.Odd.Resolve(5), 1, 3, 5), "odd");
            Require(SeqEqual(PageSelection.Even.Resolve(5), 2, 4), "even");
            Require(SeqEqual(PageSelection.Current(2).Resolve(5), 2), "current");
            Require(SeqEqual(PageSelection.First(2).Resolve(5), 1, 2), "first");
            Require(SeqEqual(PageSelection.Last(2).Resolve(5), 4, 5), "last");
            Require(SeqEqual(PageSelection.Explicit(3, 1, 1, 9).Resolve(5), 1, 3), "explicit dedupe+clamp");
            Require(SeqEqual(PageSelection.Range("1-3, 5").Resolve(10), 1, 2, 3, 5), "range");
        }

        public static void PageSelectionRejectsBadRange()
        {
            bool threw = false;
            try { PageSelection.Range("5-2"); } catch (FormatException) { threw = true; }
            Require(threw, "range end before start must throw");
        }

        public static void PageSelectionRoundTripsThroughCanonical()
        {
            foreach (var sel in new[]
            {
                PageSelection.All, PageSelection.None, PageSelection.Odd, PageSelection.Even,
                PageSelection.Current(4), PageSelection.First(3), PageSelection.Last(2),
                PageSelection.Range("1-4, 7"), PageSelection.Explicit(2, 5, 8)
            })
            {
                var round = PageSelection.Parse(sel.ToCanonicalString());
                Require(SeqEqual(round.Resolve(20), AsArray(sel.Resolve(20))), "round-trip " + sel.ToCanonicalString());
            }
        }

        public static void SpoolEnvelopeRoundTrips()
        {
            byte[] pdf = new byte[256];
            for (int i = 0; i < pdf.Length; i++) pdf[i] = (byte)(i * 7);
            var settings = new PrintSettings
            {
                Pages = PageSelection.Range("2-4"),
                Copies = 3,
                Collate = false,
                Duplex = Duplex.Horizontal,
                Color = ColorMode.Grayscale,
                Paper = PaperSize.Legal,
                Orientation = PageOrientation.ReverseLandscape,
                Scale = ScaleMode.Custom,
                CustomScalePercent = 85.5,
                Alignment = ContentAlignment.BottomRight,
                Margins = Margins.FromMillimetres(10, 12, 10, 12),
                Silent = true
            };
            var envelope = new SpoolEnvelope(pdf, settings);
            var back = SpoolEnvelope.FromArray(envelope.ToArray());

            Require(SeqEqualBytes(back.PdfBytes, pdf), "pdf bytes preserved");
            var r = back.Settings;
            Require(r.Copies == 3 && !r.Collate, "copies/collate");
            Require(r.Duplex == Duplex.Horizontal && r.Color == ColorMode.Grayscale, "duplex/colour");
            Require(r.Paper == PaperSize.Legal, "paper");
            Require(r.Orientation == PageOrientation.ReverseLandscape, "orientation");
            Require(r.Scale == ScaleMode.Custom && Math.Abs(r.CustomScalePercent - 85.5) < 1e-9, "scale");
            Require(r.Alignment == ContentAlignment.BottomRight, "alignment");
            Require(r.Margins == settings.Margins, "margins");
            Require(r.Silent, "silent");
            Require(SeqEqual(r.Pages.Resolve(10), 2, 3, 4), "pages");
        }

        public static void SpoolEnvelopeDetectsCorruption()
        {
            var envelope = new SpoolEnvelope(new byte[] { 1, 2, 3 }, new PrintSettings());
            byte[] data = envelope.ToArray();
            data[data.Length - 1] ^= 0xFF; // flip a payload byte
            bool threw = false;
            try { SpoolEnvelope.FromArray(data); } catch (System.IO.InvalidDataException) { threw = true; }
            Require(threw, "corrupted envelope must be rejected");
        }

        public static void SpoolEnvelopeRejectsForeignData()
        {
            bool threw = false;
            try { SpoolEnvelope.FromArray(new byte[] { 9, 9, 9, 9, 9 }); }
            catch (System.IO.InvalidDataException) { threw = true; }
            Require(threw, "non-Chuvadi data must be rejected");
        }

        // --- helpers ---
        public static IReadOnlyList<Action> All() => new Action[]
        {
            DefaultSettingsAreSensible,
            SettingsCloneIsIndependent,
            PaperSizeConversionsAreCorrect,
            MarginPresetsAreCorrect,
            AllAlignmentsDistinct,
            PageSelectionResolvesEveryMode,
            PageSelectionRejectsBadRange,
            PageSelectionRoundTripsThroughCanonical,
            SpoolEnvelopeRoundTrips,
            SpoolEnvelopeDetectsCorruption,
            SpoolEnvelopeRejectsForeignData
        };

        private static int[] AsArray(IReadOnlyList<int> list)
        {
            var arr = new int[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = list[i];
            return arr;
        }

        private static bool SeqEqual(IReadOnlyList<int> actual, params int[] expected)
        {
            if (actual.Count != expected.Length) return false;
            for (int i = 0; i < expected.Length; i++) if (actual[i] != expected[i]) return false;
            return true;
        }

        private static bool SeqEqualBytes(byte[] actual, byte[] expected)
        {
            if (actual.Length != expected.Length) return false;
            for (int i = 0; i < expected.Length; i++) if (actual[i] != expected[i]) return false;
            return true;
        }
    }
}

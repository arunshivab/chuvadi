using System;
using System.Collections.Generic;
using System.Linq;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class VerificationGroups
    {
        private static IEnumerable<Action> L5Groups()
        {
            yield return L5_TableShowcaseRepeatsHeadersAndKeepsEveryRow;
            yield return L5_SplitRowContinuesOnTheNextPage;
        }

        private static void L5_TableShowcaseRepeatsHeadersAndKeepsEveryRow()
        {
            using LaidOutDocument doc = DocumentLayout.Open(PreviewScenarios.TableShowcase(), PreviewScenarios.Options());
            int n = doc.ResolvePageCount();
            Check(n >= 3, "table showcase spans pages");
            string all = string.Join("\n", Enumerable.Range(0, n).Select(i => doc.GetPage(i).GetText()));
            for (int i = 5; i <= 70; i++)
            {
                Check(all.Contains("A-" + i + "\t", StringComparison.Ordinal), "row A-" + i + " missing");
            }
            for (int i = 1; i < n; i++)
            {
                Check(doc.GetPage(i).GetText().StartsWith("Code\tDescription\tUnit\tAmount (INR)", StringComparison.Ordinal) || i == n - 1 && !doc.GetPage(i).GetText().Contains("\t", StringComparison.Ordinal),
                    "header row must repeat on page " + (i + 1));
            }
        }

        private static void L5_SplitRowContinuesOnTheNextPage()
        {
            using LaidOutDocument doc = DocumentLayout.Open(PreviewScenarios.TableShowcase(), PreviewScenarios.Options());
            string p2 = doc.GetPage(1).GetText();
            string second = p2.Split('\n')[1];
            // The continued row has an empty first cell and the rest of the long cell's text; where the text splits
            // depends on the row heights (rows make room for their borders), so only the continuation is checked.
            Check(second.StartsWith("\t", StringComparison.Ordinal) && second.Contains("More text.", StringComparison.Ordinal), "page 2 should continue the split row, got: " + second);
        }
    }
}

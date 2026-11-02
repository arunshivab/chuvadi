# Excel-produced test workbooks

These workbooks come from the Apache POI test corpus
(<https://github.com/apache/poi/tree/trunk/test-data/spreadsheet>). They are under the Apache License 2.0;
see `POI-LICENSE.txt` and `POI-NOTICE.txt`. They are used unchanged. Each one was saved by Microsoft Excel
(docProps/app.xml), except `49609.xlsx`, which comes from a third-party writer that uses `\` in part names.

| File | Excel | Used for |
|---|---|---|
| SampleSS.xlsx | 12.0 (2007) | text, fonts, a red font colour |
| ConditionalFormattingSamples.xlsx | 16.0 | conditional formats across many sheets, shapes |
| NewStyleConditionalFormattings.xlsx | 14.0 (2010) | highlight rules, data bars, colour scales |
| WithDrawing.xlsx | 12.0 | pictures |
| 50846-border_colours.xlsx | 12.0 | theme and indexed border colours |
| WithChart.xlsx | 12.0 | a chart (placeholder) |
| 49609.xlsx | other | part names with backslashes (format sniffing) |

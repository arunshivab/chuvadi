# Word-produced test documents

These documents come from the Apache POI test corpus
(<https://github.com/apache/poi/tree/trunk/test-data/document>). They are under the Apache License 2.0;
see `POI-LICENSE.txt` and `POI-NOTICE.txt`. They are used unchanged. Each one was saved by Microsoft Word
(docProps/app.xml), which also recorded its page count; the tests compare Layout's page count with it.

| File | Word | Pages | Used for |
|---|---|---|---|
| SampleDoc.docx | 12.0 (2007) | 2 | styles, headings, lists |
| TestDocument.docx | 12.0 | 1 | plain paragraphs and runs |
| Headers.docx | 12.0 | 3 | headers and footers across pages |
| DiffFirstPageHeadFoot.docx | 12.0 | 2 | a different first-page header and footer |
| ComplexNumberedLists.docx | 14.0 (2010) | 1 | multi-level numbering |
| ThreeColHeadFoot.docx | 12.0 | 2 | three-column header and footer tables/tabs |
| headerPic.docx | 12.0 | 1 | a picture in the header |
| issue_51265_3.docx | 12.0 | 1 | several inline pictures in one paragraph; pictures in several headers |

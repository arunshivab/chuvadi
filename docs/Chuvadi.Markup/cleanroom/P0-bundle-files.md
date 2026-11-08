# P0 clean-room bundle: file list (D-106, D-195)

The bundle the clean-room author worked from: `chuvadi-markdown-cleanroom-p0.zip`, final state after patch 8
(2026-11-07), 6,334,215 bytes, SHA-256 `BC3FD490931F7C4DAD2B196952E14C4587BE7035E63281ADBC53EC655716B9B2`, 67 files.

It was built from monorepo main `349ee73`. It holds the given library (every Chuvadi.Markup file except the
Markdown parser and renderer), stubs for the parts the author wrote, the specifications, the frozen gate and
the trace templates. It holds no cmark or cmark-gfm code and none of the derived parser or renderer code that
1.1.0 removes: the files `Markdown/Parsing/AstBuilder.cs`, `BlockNode.cs`, `BlockParser.cs`,
`ExtendedAutolinks.cs`, `InlineNode.cs`, `InlineParser.cs` and `Scanners.cs` are not in it, and the renderer
is a stub. The only material from the cmark-gfm repository is the GFM spec and its extension tests, which are
specification text and test data.

"Library from main 349ee73, identical" means byte-identical to the file at `349ee73` (checked again for this
list).

| # | Path | Size (bytes) | SHA-256 | Origin |
|---|---|---|---|---|
| 1 | BRIEF.md | 19,982 | 3cd53c7685c7edffe5eac10cae7a85d1977688e1028922352b49d6142df5530e | written by the Markup chat: the author brief (firewall, byte identity, rule 5, trace, phases); the copy in this folder is BRIEF-author.md |
| 2 | Directory.Build.props | 571 | 32ba6e8ed41f7d35c7810b00eef5844cadae6f0258b168a75a888217366432c7 | written by the Markup chat: build settings mirroring the monorepo |
| 3 | README.md | 6,494 | 6c6487278b344250e44ca5b8477734e4cd501bbba107837ad50d285728a9f76e | written by the Markup chat: bundle layout, cases, checks, configurations |
| 4 | gate/Chuvadi.Markdown.Gate/Cases.cs | 6,225 | 6c2ea6ab9fbe784bba83be1f75fc0e9ad88609e09a84f17c378784d8ffaf133b | gate tool written by the Markup chat: spec-file reader, case file I/O |
| 5 | gate/Chuvadi.Markdown.Gate/Chuvadi.Markdown.Gate.csproj | 448 | 4205a20352d4df3f63750b12b317fcb6a501227669f1f0bff289b3d23bd965b6 | gate tool written by the Markup chat: project file; its only reference is the bundle's own src/Chuvadi.Markup |
| 6 | gate/Chuvadi.Markdown.Gate/GateCore.cs | 27,021 | 4ae74c1b82c119f952972b46d5701c49edb4b960886c68b6ad8e259ae9c3f4b1 | gate tool written by the Markup chat: configurations, AST dump, contract probes, API listing, parse hook |
| 7 | gate/Chuvadi.Markdown.Gate/Program.cs | 18,061 | cef33f50b27d18459acdf5e22096e05fb1ab7dea183a6853c6293afe4437b96d | gate tool written by the Markup chat: runner, phases, diff, API check, timing, rule-5 rules |
| 8 | gate/Chuvadi.Markdown.Gate/TraceCheck.cs | 13,907 | 0d955b6728c17ff70d8d174e0f3a7752854341f9d32f9227aa333ce5ca154e19 | gate tool written by the Markup chat: trace, sources and forbidden-word checks (D-198 superseded-by form) |
| 9 | gate/data/NOTICE.txt | 2,904 | c14761d53dffc60925ae73fa65468d12bcf343badb57435801290acc8b08dbab | written by the Markup chat: data provenance, and the BSD notice for the pathological inputs |
| 10 | gate/data/baseline-scores.txt | 529 | e07b229e02944db6c1ca4fb053b49211785e614d4681786131cb18915b899677 | frozen output: spec scores (CommonMark 652/652, GFM 651/672, extensions 26/26) |
| 11 | gate/data/expected.jsonl.gz | 6,408,126 | 584e07b4e612637b0f5d0330d4d709fe061975053e50d0d0f12a42c55e591f8b | frozen output: 10,388 cases, 352,479 expected outputs (see the list of sets below) |
| 12 | gate/data/public-api.txt | 74,804 | 7d385ec04aa19a0f784a21ff0465f1a257174958c610580be375fd77875d1214 | frozen output: the public API listing (reflection, signatures only) |
| 13 | gate/data/rule5-rules.txt | 3,545 | 1d73bfa265329c31ba6c3e95944723bb71fbd60608a7c732535fe3c2a55c4d07 | written by the Markup chat: the decided rule-5 gate rules |
| 14 | specs/LICENSES.md | 659 | 65492cfcca07782b6fb87e77c8a7d746ae9f594eef51b8edfbb79eb132cbadf6 | written by the Markup chat: the three spec files are CC BY-SA 4.0 |
| 15 | specs/commonmark/spec.txt | 205,025 | 257c41ad946f7a1414a499aca402a1aa8fdac3678532266611348c1cf54f4b80 | download: CommonMark Spec 0.31.2, identical to upstream |
| 16 | specs/gfm/extensions.txt | 21,274 | a2a45e98be9fca95f564f927265a0f63beea6cae5369d1cf4bde44caa51b2a3a | download: GFM extensions test file, identical to upstream; test data, not code |
| 17 | specs/gfm/gfm-spec.txt | 216,680 | 7d8e5814befec287ac116786d81ff14e0adc9b13295b4494649e995408fd871c | download: GFM Spec 0.29-gfm, identical to upstream; spec text, not code |
| 18 | src/Chuvadi.Markup/Chuvadi.Markup.csproj | 1,916 | cfc8fe2fd79505d17fd7beafeec8d77e81dbff5cfa45c1589c305cf68d0a8df8 | library from main 349ee73, changed: pack items, PackageReadmeFile and RepositoryUrl removed; InternalsVisibleTo for the author's tests |
| 19 | src/Chuvadi.Markup/Css/CssModel.cs | 3,066 | 987f5ddfdf21e893d58a26c5d63bf101f2253f7deaee4e32a56db7f37c06f8fe | library from main 349ee73, identical |
| 20 | src/Chuvadi.Markup/Css/CssParser.cs | 13,060 | 6a48c351e3bf7058537f6aa69000b6f28d8e28f552e491ab806479dab905ab93 | library from main 349ee73, identical |
| 21 | src/Chuvadi.Markup/Css/CssSerializer.cs | 12,000 | 871e126b4cafc3509cdcd1fa03e56fa6737ee97d736d5b4c176668e33caf7e37 | library from main 349ee73, identical |
| 22 | src/Chuvadi.Markup/Css/CssToken.cs | 5,139 | 04ca35087aee4dff1ac3df5660d57d594dfd7d398d4326cd57ae3f87a991df1b | library from main 349ee73, identical |
| 23 | src/Chuvadi.Markup/Css/CssTokenizer.cs | 13,733 | cb310721275249adc9a071b8c3f21a50b9567d33f583541a00eb1d5288628b4b | library from main 349ee73, identical |
| 24 | src/Chuvadi.Markup/Html/HtmlElement.cs | 8,392 | fc99c24d02dbbaa1cf1a70331a42a9909fd87585ca413729f7e0d07b318badda | library from main 349ee73, identical |
| 25 | src/Chuvadi.Markup/Html/HtmlEncoding.cs | 18,365 | 24df37a4bda2abf6456a21ab3dc5834c1e4baa74019ca40dbbfe2d5cbddcc3bd | library from main 349ee73, identical |
| 26 | src/Chuvadi.Markup/Html/HtmlNode.cs | 18,000 | 78b4f13d883e6ad3c754d79891c9c8ff93bbc75a0f73173c01220501e538a935 | library from main 349ee73, identical |
| 27 | src/Chuvadi.Markup/Html/HtmlParser.cs | 6,764 | dd8e325f0bc619b7c7c46d89081468bd734239b8e753ee98004a4b3192ced904 | library from main 349ee73, identical |
| 28 | src/Chuvadi.Markup/Html/HtmlParserOptions.cs | 1,770 | 1fd9c339426859451b09989d43ee1babbde97e3156dd460443164d3a63205c39 | library from main 349ee73, identical |
| 29 | src/Chuvadi.Markup/Html/HtmlSerializer.cs | 10,319 | a6c4d759af787159b9e8de4ae1a6d5cecf5725b493c97301d78e6298ff3f3aa3 | library from main 349ee73, identical |
| 30 | src/Chuvadi.Markup/Html/HtmlToken.cs | 3,974 | 75a3a24705eec182636888b40ac20ae9307b362ba3053a4b5f3720a04a58baac | library from main 349ee73, identical |
| 31 | src/Chuvadi.Markup/Html/HtmlTokenizer.cs | 59,439 | f79c8acb8d5dc10190fce2b5946192b42442e991c9bcd1dbc253228c09da9181 | library from main 349ee73, identical |
| 32 | src/Chuvadi.Markup/Html/TreeBuilder.Modes.cs | 73,653 | 6904a63351ba6f514f2bc6472820aec175a54dbc3e10038463ff052d6f65be9a | library from main 349ee73, identical |
| 33 | src/Chuvadi.Markup/Html/TreeBuilder.cs | 32,588 | 15201d990ad60c98e2c630939d5fe9600109a43d0a1b377687e386e10b7d8906 | library from main 349ee73, identical |
| 34 | src/Chuvadi.Markup/Internal/CaseFolding.cs | 1,730 | 1a6bc7bee0ba97895753449b642edcb6ba7ae6abb651795764e7421f5a4959c8 | library from main 349ee73, identical |
| 35 | src/Chuvadi.Markup/Internal/CaseFolding.g.cs | 37,988 | 0d10e7374b1987c4a97bd223feebca53a5edcbead1dc10b43e97890f994377c5 | library from main 349ee73, identical |
| 36 | src/Chuvadi.Markup/Internal/CharUtil.cs | 5,108 | 5abceabef40b6e67cc8ebd277eec07e4a0fb92b2f9f104ed585441df53d117a1 | library from main 349ee73, identical |
| 37 | src/Chuvadi.Markup/Internal/Guard.cs | 1,909 | 08a05c377fd26d868459164affeb83d35067fd9638a0f0884433baca518fbcfc | library from main 349ee73, identical |
| 38 | src/Chuvadi.Markup/Internal/HtmlEntities.cs | 2,089 | 656aac73ae062ed33bfa8d397e91ea46087d8bbc4de8cd9addac32bbd96ce792 | library from main 349ee73, identical |
| 39 | src/Chuvadi.Markup/Internal/HtmlEntities.g.cs | 69,003 | bd39030e812f4b5b6f9a4210c9317205a1241cdc53f7d27149e11dfc83d5abb1 | library from main 349ee73, identical |
| 40 | src/Chuvadi.Markup/Internal/LimitedStringWriter.cs | 2,802 | df3e8f3697debcf38e4476cff08d9d3d28361e6e9fb77e5e42ebd08aaf8e295a | library from main 349ee73, identical |
| 41 | src/Chuvadi.Markup/Internal/TextInput.cs | 874 | 292f76a07f1f6145c82373f7eb9381920dab3e04f286ac68f4b3ed5815d0a332 | library from main 349ee73, identical |
| 42 | src/Chuvadi.Markup/Markdown/Blocks.cs | 12,732 | cb9cf6e660c9dd73e6743869928bd7529f778a2e44f11715f380913477076b37 | library from main 349ee73, changed: NormalizeLabel follows the spec (D-174) |
| 43 | src/Chuvadi.Markup/Markdown/Inlines.cs | 6,910 | 223b9d226f5823286d206abef66233143ff392332c38b7cdb3982fb12bc9a1e4 | library from main 349ee73, identical |
| 44 | src/Chuvadi.Markup/Markdown/MarkdownConverter.cs | 682 | fa58202680e5fb48ad91869f488d91e3f286b2d7c66e45aa157b2dac355ce8bf | library from main 349ee73, identical |
| 45 | src/Chuvadi.Markup/Markdown/MarkdownHtmlOptions.cs | 4,336 | 1a2dd4a377dd7adf67d959bccdaa24702fd810b45ed62c2b6c76e39688cb910b | library from main 349ee73, changed: three doc comments reworded in spec terms |
| 46 | src/Chuvadi.Markup/Markdown/MarkdownHtmlRenderer.cs | 1,580 | 939ad641b2bf988ee070bb1baac6b652531546a36b69360f6048bdc738662afd | stub written by the Markup chat: public signatures and XML docs, bodies throw |
| 47 | src/Chuvadi.Markup/Markdown/MarkdownNode.cs | 7,468 | d61f2a902fc4751881dd3dff37204d6cd74460d72a27289c26dd6dfe710b0f55 | library from main 349ee73, identical |
| 48 | src/Chuvadi.Markup/Markdown/MarkdownOptions.cs | 2,249 | 009a0d654a3a45e2a59a1020194e86ab98b2577366f017d3737e6c8445bbd8e4 | library from main 349ee73, identical |
| 49 | src/Chuvadi.Markup/Markdown/MarkdownParser.cs | 2,520 | 62d10080e4efc8782452bec712479000854edba0818bfa963adfbf53aff34484 | library from main 349ee73, changed: calls MarkdownEngine.Parse |
| 50 | src/Chuvadi.Markup/Markdown/MarkdownVisitor.cs | 2,342 | bcf7d45773791a8cf9ed4262e31dc6103089790bf642eca3461741f3d9d8b3aa | library from main 349ee73, identical |
| 51 | src/Chuvadi.Markup/Markdown/MarkdownWriter.cs | 34,672 | 5251efd9800dab2a49a6d9a3737dde90f6a2a1e9828416ab19498dd3a382fa74 | library from main 349ee73, changed: calls MarkdownSyntax.IsUriAutolinkBody; a private helper renamed |
| 52 | src/Chuvadi.Markup/Markdown/Parsing/MarkdownEngine.cs | 1,610 | 5b56aea6e1b54179e7fd0addae30bc66b289ba68c59c3f5e6d1785307b04809a | stub written by the Markup chat: contract comment, body throws |
| 53 | src/Chuvadi.Markup/Markdown/Parsing/MarkdownSyntax.cs | 1,535 | fd3ef8074b286d363ed9342cf927d9df8b8bceb79211c126bcab043cc2cd616a | stub written by the Markup chat: contract comments, bodies throw |
| 54 | src/Chuvadi.Markup/MarkupLimitExceededException.cs | 1,088 | b6df4f003abbd4d75075f4efd8e804d0983d325d913840406f68d25d8d87cd83 | library from main 349ee73, identical |
| 55 | src/Chuvadi.Markup/MarkupLimits.cs | 7,140 | 846ff093f03d41ec3673fb5b98b46f4815aff9178431855058d2d88a2ef792da | library from main 349ee73, changed: doc comments of MaxNodes (R5-01), MaxParserWorkPerChar (R5-02) and MaxNestingDepth (D-193, D-199) |
| 56 | src/Chuvadi.Markup/Sanitization/CssSanitizer.cs | 30,767 | e48c7ac97a81d06d86c9f29f758478f9261eb4525814f5a1ab4bf64e9faf61f8 | library from main 349ee73, identical |
| 57 | src/Chuvadi.Markup/Sanitization/HtmlSanitizer.cs | 6,052 | 98cdd5d3a8b19c7e90b7c0416580fb77a0a92e7fa941e901de9e4100c6c4cecd | library from main 349ee73, identical |
| 58 | src/Chuvadi.Markup/Sanitization/SanitizerEngine.cs | 33,108 | e0a60fb66656b342713cdd9da7ebf6581b9e03b885332cf8185315e8c8b2644c | library from main 349ee73, identical |
| 59 | src/Chuvadi.Markup/Sanitization/SanitizerPolicy.cs | 27,477 | df1efcede926d299c6617d1e174fa0215af99a83bf3f78913a5c9f6c2bd8a089 | library from main 349ee73, identical |
| 60 | src/Chuvadi.Markup/Sanitization/SanitizerTypes.cs | 7,486 | a17fe395b0fcef1d643d265cdb8314950e1423bcef0a233a46105ee8c35f5aa3 | library from main 349ee73, identical |
| 61 | src/Chuvadi.Markup/Sanitization/UrlUtil.cs | 3,863 | 952ad0ff86f7a312fa6c823c3334e6366c2c582df825898485c0cb71e0798648 | library from main 349ee73, identical |
| 62 | src/Chuvadi.Markup/SourceSpan.cs | 1,840 | 6f1d8027fcae0c434fc8a84b6b1831c99c31cd43486ea6b696a209874f4ecfc1 | library from main 349ee73, identical |
| 63 | src/Chuvadi.Markup/Text/PlainText.cs | 17,320 | d953d85d8209dbe08f54abe4b6a456ae971b9913f77be1e2eebcb4d95e3f43e5 | library from main 349ee73, changed: calls MarkdownSyntax.TrimExtendedAutolink |
| 64 | trace/experiments/README.md | 367 | ceabd79d55f6a6d50d2e0601ff10b97d8015366b8d732523111becc62090ff5d | written by the Markup chat: experiment-record template |
| 65 | trace/rule5-cases.md | 709 | c324db83a9020764dacca46940f887145743042b0e407f154396a409192a969c | written by the Markup chat: rule-5 case template |
| 66 | trace/rules.md | 1,563 | e18780e8e2775ef87e52cdf21d2af72b46eca0ac478fbe0641da260fe77c29f6 | written by the Markup chat: trace-log template |
| 67 | trace/sources.md | 818 | 9708b28a6d635caf05d01c3951ba0b4e74c6d9fb1f3178bec1e8fabc1adc572c | written by the Markup chat: sources-log template |

## Frozen sets in `gate/data/expected.jsonl.gz`

| Set | Cases | Source |
|---|---|---|
| commonmark | 652 | CommonMark 0.31.2 spec examples |
| gfm-spec | 672 | GFM 0.29 spec examples |
| gfm-extensions | 30 | GFM extension tests |
| documents | 28 | the three spec files split into documents |
| fuzz | 4,000 | the Markup chat's generator, seed 20261103 |
| limits | 106 | limit inputs written by the Markup chat |
| labels | 41 | link-label inputs (D-174) |
| urls | 3 | data: URL inputs (D-199) |
| pathological | 28 | pathological inputs (provenance in NOTICE.txt) |
| contract | 1 | contract probes |
| diff-core | 2,086 | comparison inputs from the Markup chat's generators (D-232, D-237) |
| diff-full | 1,379 | comparison inputs from the Markup chat's generators (D-232, D-237) |
| slugs | 26 | heading-id inputs (D-237) |
| diff-domain | 1,336 | domain-start inputs (D-242) |

Expected outputs come from the given library with the approved rule-5 rulings applied, each derived from the
spec text or from Arun's ruling. cmark was not consulted. Freezing twice gives a byte-identical file.

## Patches sent to the author

| Patch | Decisions | Content |
|---|---|---|
| 2 | D-141 | gate fix (Cases.cs) and refreeze: 15 cases changed |
| 3 | D-143 | refreeze: 46 cases changed |
| 4 | D-157 | refreeze: 10 cases changed |
| 5 | D-174 | Blocks.cs NormalizeLabel; 41 label cases and one probe added |
| 6 | D-193, D-198, D-199 | MarkupLimits.cs doc; TraceCheck.cs superseded-by form; 3 url cases added |
| 7 | D-232, D-237 | 3,491 cases added (diff-core, diff-full, slugs); no earlier case changed |
| 8 | D-242 | 1,336 cases added (diff-domain); no earlier case changed |

## Confirmations

1. No file contains cmark or cmark-gfm source code, comments or identifiers. The word "cmark" appears only in
   BRIEF.md (the firewall's list of forbidden implementations) and in TraceCheck.cs (its list of forbidden
   words).
2. No file contains the derived Chuvadi.Markup parser or renderer source.
3. The frozen outputs contain only HTML, AST dumps (public property names of the public AST), Markdown writer
   output, plain-text HTML and exception type, limit and message text. They contain no stack traces and no
   internal type or member names.
4. The gate never calls the old Chuvadi.Markup DLL. Its only reference is the bundle's own src/Chuvadi.Markup,
   and it compares against the frozen files only.

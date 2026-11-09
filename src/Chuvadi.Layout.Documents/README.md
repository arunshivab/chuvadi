# Chuvadi.Layout.Documents

One call for a file of unknown kind, for [Chuvadi.Layout](https://github.com/arunshivab/chuvadi): the format is
detected from the content and the file goes to Chuvadi.Layout.Docs (.docx), Chuvadi.Layout.Sheets (.xlsx) or the
engine (plain text). With `LayoutOptions.Password`, an encrypted file is decrypted and laid out as a document or a
workbook, whichever it is; a missing or wrong password is `LayoutPasswordRequiredException` (`PasswordGiven` tells
which). Pure BCL; works in memory.

```csharp
using Chuvadi.Layout;
using Chuvadi.Layout.Documents;

using LaidOutDocument doc = AutoDetectLayout.Open(stream, LayoutSourceFormat.Auto, options, ct);
```

| Member | What it does |
|---|---|
| `AutoDetectLayout.Open(Stream / byte[], LayoutSourceFormat format = Auto, LayoutOptions?, ct)` | sniffs and lays out docx, xlsx (encrypted too) and text; a stated `format` skips the sniffing |

Markdown and HTML are text to a sniffer, so they are laid out by Chuvadi.Layout.Markup (`MarkdownLayout`,
`HtmlLayout`), chosen by the caller. The class is named `AutoDetectLayout` so that it is not confused with the engine's
`DocumentLayout`, whose `Open(Stream)` lays out text only and names the package for the other formats.

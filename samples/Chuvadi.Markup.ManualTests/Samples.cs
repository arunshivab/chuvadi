using System.Text;

namespace Chuvadi.Markup.ManualTests
{
    /// <summary>Realistic inputs shared by the verification groups and the preview pages.</summary>
    public static class Samples
    {
        /// <summary>A 1x1 transparent PNG.</summary>
        public const string PngDataUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        public const string ReadmeMarkdown =
"# Chuvadi Reader\n" +
"\n" +
"A **fast**, _safe_ reader for ~~unsafe~~ untrusted documents. See <https://example.com> or www.example.org.\n" +
"\n" +
"## Features\n" +
"\n" +
"- [x] Markdown (CommonMark 0.31.2 + GFM)\n" +
"- [x] HTML sanitizing\n" +
"- [ ] PDF annotations\n" +
"\n" +
"1. First\n" +
"2. Second\n" +
"   > nested quote with `code`\n" +
"\n" +
"| Format | Status | Notes |\n" +
"|:-------|:------:|------:|\n" +
"| MD     | done   | 652/652 |\n" +
"| HTML   | done   | WHATWG |\n" +
"\n" +
"```csharp\n" +
"var html = MarkdownConverter.ToHtml(\"# Hi\");\n" +
"```\n" +
"\n" +
"Raw: <span onclick=\"alert(1)\">inline html</span> and [bad](javascript:alert(1)).\n" +
"\n" +
"Line one  \n" +
"line two\\\n" +
"line three\n" +
"\n" +
"***\n" +
"\n" +
"[ref]: https://example.com/ref \"Ref title\"\n" +
"See the [reference][ref] and ![logo](" + PngDataUri + " \"Logo\").\n";

        public const string NewsletterHtml =
"<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">\n" +
"<html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\"><title>October deals</title>\n" +
"<style type=\"text/css\">\n" +
"body { margin:0; background:#f4f4f4; font-family: Arial, sans-serif }\n" +
"#wrapper { width: 600px; margin: 0 auto; background: #fff }\n" +
".hero td { padding: 24px; background: url(https://tracker.example/bg.png) #036; color: #fff }\n" +
"a.button { display:inline-block; padding: 10px 18px; background:#e60; color:#fff; text-decoration:none; border-radius:4px }\n" +
"@media only screen and (max-width: 620px) { #wrapper { width: 100% !important } }\n" +
"@import url(https://fonts.example/css?family=Roboto);\n" +
".x { behavior: url(evil.htc); position: fixed; top: 0 }\n" +
"</style>\n" +
"<script>document.write('tracking')</script>\n" +
"</head>\n" +
"<body bgcolor=\"#f4f4f4\" style=\"color:#222\" onload=\"track()\">\n" +
"<center><table id=\"wrapper\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"600\">\n" +
"<tr class=\"hero\"><td align=\"center\"><h1>October deals</h1><p>Up to <b>50%</b> off</p></td></tr>\n" +
"<tr><td style=\"padding:16px\"><img src=\"https://cdn.example/product.jpg\" width=\"560\" alt=\"Product photo\">\n" +
"<img src=\"cid:logo@shop.example\" alt=\"Shop logo\" width=\"120\">\n" +
"<p>Hello <i>Arun</i>, click <a class=\"button\" href=\"https://shop.example/deals?utm_source=mail&amp;id=42\">Shop now</a>.</p>\n" +
"<form action=\"https://phish.example/login\" method=\"post\"><p>Confirm password: <input type=\"password\" name=\"pw\"><button>Confirm</button></p></form>\n" +
"<iframe src=\"https://ads.example/frame\"></iframe>\n" +
"<p style=\"font-size:11px;color:#888\">Unsubscribe <a href=\"javascript:unsub()\">here</a>. <a href=\"mailto:help@shop.example\">help@shop.example</a></p>\n" +
"</td></tr></table></center>\n" +
"<img src=\"https://tracker.example/open.gif?u=42\" width=\"1\" height=\"1\">\n" +
"</body></html>\n";

        public const string PlainTextReply =
"Hi Arun,\n" +
"\n" +
"Thanks for the update. The build is at https://ci.example/build/123 and the\n" +
"notes are on www.example.org/notes. Ping ops@example.com if it breaks.\n" +
"\n" +
"\tIndented with a tab <not a tag> & ampersand.\n" +
"\n" +
"On Tue, Arun wrote:\n" +
"> Can you check the release?\n" +
">> Earlier: it failed on Windows.\n" +
"> Thanks.\n" +
"\n" +
"-- \n" +
"Ravi\n";

        public const string FormatFlowedText =
"This paragraph was wrapped by the sender's mail client and \n" +
"should be shown as one flowing line in the reader. \n" +
"It ends here.\n" +
"> Quoted flowed text also \n" +
"> joins up.\n";

        /// <summary>The newsletter encoded as windows-1252 bytes with a curly-quote footer.</summary>
        public static byte[] Windows1252Mail()
        {
            var bytes = new System.Collections.Generic.List<byte>(Encoding.ASCII.GetBytes("<html><head><meta charset=\"windows-1252\"></head><body><p>"));
            bytes.AddRange(new byte[] { 0x93, (byte)'C', (byte)'a', (byte)'f', 0xE9, 0x94, (byte)' ', 0x80, (byte)'5' });
            bytes.AddRange(Encoding.ASCII.GetBytes("</p></body></html>"));
            return bytes.ToArray();
        }
    }
}

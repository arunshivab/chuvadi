namespace Chuvadi.Docs.Model;

/// <summary>
/// The defaults a new <see cref="Document"/> starts with (Docs 2.3.0, S1/D-371): pick with
/// <see cref="Document(DocumentPreset)"/>. <c>new Document()</c> is <see cref="Word"/>. A loaded
/// document has no preset: it keeps the defaults its file has.
/// </summary>
public enum DocumentPreset
{
    /// <summary>
    /// Word's new-document defaults (the default): paragraphs get 8 pt after and 1.08 lines
    /// (<see cref="DocumentDefaults.StandardParagraphFormat"/>, written as
    /// <c>w:spacing w:after="160" w:line="259" w:lineRule="auto"</c>), and header and footer paragraphs in
    /// Normal use Word's Header and Footer styles (no space after, single lines, a centre and a right tab;
    /// <see cref="DocumentDefaults.HeaderParagraphStyleId"/>). A document made for people who open it in Word
    /// should use this: with no spacing, Word lets the next line paint over marks that reach below their own
    /// line (Latha's Tamil vowel signs lose their lower part).
    /// </summary>
    Word = 0,

    /// <summary>
    /// No default paragraph spacing: 0 pt after and single lines, an empty <c>w:pPrDefault</c>, and no Header or
    /// Footer style (header and footer paragraphs stay in Normal): the new-document output before 2.3.0, byte for
    /// byte. Everything else is as <see cref="Word"/>: the other built-in styles, Calibri 11 pt, compatibility mode 15. Suits documents only shown through Chuvadi (for example
    /// Chuvadi.Layout's previews and PDFs, which never clip glyphs), or callers who set every
    /// paragraph's spacing themselves.
    /// </summary>
    Minimal = 1,
}

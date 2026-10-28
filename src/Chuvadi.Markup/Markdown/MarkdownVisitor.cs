namespace Chuvadi.Markup.Markdown
{
    /// <summary>
    /// Visitor over the Markdown AST. Every method's default visits the node's children, so a
    /// subclass overrides only the kinds it cares about. Recursion depth equals tree depth, which the
    /// parser bounds by <see cref="MarkupLimits.MaxNestingDepth"/>.
    /// </summary>
    public abstract class MarkdownVisitor
    {
        /// <summary>Visits each child in order.</summary>
        protected void VisitChildren(MarkdownNode node)
        {
            var kids = node.Children;
            for (int i = 0; i < kids.Count; i++) kids[i].Accept(this);
        }

#pragma warning disable CS1591
        public virtual void VisitDocument(MarkdownDocument node) => VisitChildren(node);
        public virtual void VisitBlockQuote(BlockQuote node) => VisitChildren(node);
        public virtual void VisitList(ListBlock node) => VisitChildren(node);
        public virtual void VisitListItem(ListItem node) => VisitChildren(node);
        public virtual void VisitCodeBlock(CodeBlock node) { }
        public virtual void VisitHtmlBlock(HtmlBlock node) { }
        public virtual void VisitParagraph(Paragraph node) => VisitChildren(node);
        public virtual void VisitHeading(Heading node) => VisitChildren(node);
        public virtual void VisitThematicBreak(ThematicBreak node) { }
        public virtual void VisitTable(Table node) => VisitChildren(node);
        public virtual void VisitTableRow(TableRow node) => VisitChildren(node);
        public virtual void VisitTableCell(TableCell node) => VisitChildren(node);
        public virtual void VisitText(TextInline node) { }
        public virtual void VisitCodeSpan(CodeSpan node) { }
        public virtual void VisitEmphasis(Emphasis node) => VisitChildren(node);
        public virtual void VisitStrong(Strong node) => VisitChildren(node);
        public virtual void VisitStrikethrough(Strikethrough node) => VisitChildren(node);
        public virtual void VisitLink(Link node) => VisitChildren(node);
        public virtual void VisitImage(Image node) => VisitChildren(node);
        public virtual void VisitHtmlInline(HtmlInline node) { }
        public virtual void VisitHardBreak(HardBreak node) { }
        public virtual void VisitSoftBreak(SoftBreak node) { }
#pragma warning restore CS1591
    }
}

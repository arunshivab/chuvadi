using Chuvadi.Docs.Model;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// The XML element count of one operation (<see cref="DocxReaderOptions.MaxElements"/>, D-281): every
/// <see cref="GuardedXmlReader"/> of the operation adds to it, so the limit covers all the parts the
/// operation reads together. One operation runs on one thread at a time, so the count needs no locking.
/// </summary>
internal sealed class ElementBudget
{
    private readonly int _max;
    private long _count;

    public ElementBudget(int max) => _max = max;

    /// <summary>Counts one element; throws once the operation has read more than the limit.</summary>
    public void Add(string partName)
    {
        if (++_count > _max)
            throw new DocxSecurityLimitException(DocxLimit.Elements, _count, _max, partName);
    }
}

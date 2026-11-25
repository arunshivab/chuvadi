using System.Runtime.CompilerServices;
using Chuvadi.Docs.Model;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// The content nesting of one load operation (<see cref="DocxReaderOptions.MaxNestingDepth"/>, 2.4.1): every level the
/// loader reads by calling itself — a table (a nested table, rows or cells inside nested content controls) and a
/// hyperlink, tracked change (ins, del, moveTo, moveFrom) or simple field inside a paragraph — counts one, and a text box
/// counts <see cref="TextBoxWeight"/> (it takes about four times the stack of a table), over every story the operation
/// reads. It is checked before the loader goes a level deeper, so a hostile file is refused
/// with <see cref="DocxSecurityLimitException"/> (<see cref="DocxLimit.NestingDepth"/>) long before the stack runs out,
/// and every document that loads is shallow enough for the writer and the converters. Each level beyond the default
/// depth also checks that enough stack is left (<see cref="RuntimeHelpers.EnsureSufficientExecutionStack"/>), so with
/// a raised limit or none a deep file ends in a catchable <see cref="System.InsufficientExecutionStackException"/>
/// rather than a process crash.
/// One operation runs on one thread at a time, so the count needs no locking.
/// </summary>
internal sealed class NestingGuard
{
    /// <summary>The levels one text box counts: measured, a nested text box takes about 9 KB of stack, a table about 2 KB.</summary>
    public const int TextBoxWeight = 4;

    private readonly int? _max;
    private int _depth;

    public NestingGuard(int? max) => _max = max;

    /// <summary>Goes one level deeper; pair every call with <see cref="Exit"/> in a finally block.</summary>
    public void Enter(string? partName, int weight = 1)
    {
        int depth = _depth + weight;
        if (_max is int max && depth > max)
            throw new DocxSecurityLimitException(DocxLimit.NestingDepth, depth, max, partName);
        // Up to the default depth the loader, the writer and the converters are measured to fit a 256 KB stack.
        // Deeper (only with a raised limit or none), every level checks the stack that is left. The runtime keeps a
        // large reserve for that check (about half of a 256 KB stack), so it is not made within the default depth.
        if (depth > DocxReaderOptions.DefaultMaxNestingDepth) RuntimeHelpers.EnsureSufficientExecutionStack();
        _depth = depth;
    }

    /// <summary>Comes back one level.</summary>
    public void Exit(int weight = 1) => _depth -= weight;
}

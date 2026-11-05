using System;
using System.Collections;
using System.Collections.Generic;
using Chuvadi.Docs.Model;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// The single exception contract for malformed input (Docs 2.0). Every reading entry point
/// (Document.Load, DocxReader.Open and its lazy readers, DocxTemplate.Fill / GetPlaceholders /
/// CheckTemplate) lets the contract exceptions through unchanged and reports every other failure
/// while reading the package as a <see cref="DocxFormatException"/> carrying the original error
/// as its InnerException. Argument checks run before the guarded region, so caller mistakes keep
/// their ArgumentException types; opening a file by path also happens outside it.
/// </summary>
internal static class DocxErrors
{
    /// <summary>The exceptions that pass through unchanged.</summary>
    public static bool IsContract(Exception ex) =>
        ex is DocxFormatException             // includes DocxSecurityLimitException
           or DocxPasswordRequiredException
           or DocxTemplateException
           or DocxLimitExceededException      // an IOException, but a limit: never wrapped
           or OperationCanceledException
           or ObjectDisposedException;        // using a disposed reader is a caller mistake

    /// <summary>Wraps a non-contract failure in a <see cref="DocxFormatException"/>.</summary>
    public static DocxFormatException Wrap(Exception ex, string message) => new(message + " " + ex.Message, ex);

    public const string ReadMessage = "Not a valid docx package:";

    public const string TemplateReadMessage = "The template is not a valid docx package:";

    /// <summary>Enumerates <paramref name="source"/>, wrapping failures raised while moving to the next item.</summary>
    public static IEnumerable<T> Guard<T>(IEnumerable<T> source) => new GuardedEnumerable<T>(source);

    private sealed class GuardedEnumerable<T> : IEnumerable<T>
    {
        private readonly IEnumerable<T> _source;
        public GuardedEnumerable(IEnumerable<T> source) => _source = source;
        public IEnumerator<T> GetEnumerator() => new GuardedEnumerator<T>(_source.GetEnumerator());
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class GuardedEnumerator<T> : IEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;
        public GuardedEnumerator(IEnumerator<T> inner) => _inner = inner;
        public T Current => _inner.Current;
        object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            try { return _inner.MoveNext(); }
            catch (Exception ex) when (!IsContract(ex)) { throw Wrap(ex, ReadMessage); }
        }

        public void Reset() => _inner.Reset();
        public void Dispose() => _inner.Dispose();
    }
}

using System;
using System.Threading;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// The token one <see cref="Model.DocxReader"/> read observes (D-284, R3): the token given to
/// <c>Open</c> and the call's own token. When only one of them can be cancelled, that one is used as is;
/// when both can, a linked source is made for the read and disposed when it ends (an iterator disposes it
/// when its enumeration is disposed).
/// </summary>
internal readonly struct ReadToken : IDisposable
{
    private readonly CancellationTokenSource? _linked;

    private ReadToken(CancellationToken token, CancellationTokenSource? linked)
    {
        Token = token;
        _linked = linked;
    }

    public CancellationToken Token { get; }

    public static ReadToken Combine(CancellationToken openToken, CancellationToken callToken)
    {
        if (!callToken.CanBeCanceled || callToken == openToken) return new ReadToken(openToken, null);
        if (!openToken.CanBeCanceled) return new ReadToken(callToken, null);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(openToken, callToken);
        return new ReadToken(linked.Token, linked);
    }

    public void Dispose() => _linked?.Dispose();
}

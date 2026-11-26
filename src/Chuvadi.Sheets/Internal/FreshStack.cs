using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// 2.2.1 (P1): lets the recursive readers keep their nesting limits (256 formula levels, 256 shape groups) on a thread
/// with a small stack. Thread-pool, Windows and Android threads can have 256 KB or less, of which .NET keeps 128 KB in
/// reserve, so a formula nested 256 levels does not fit there. When the stack runs low, the rest of the work continues
/// on a fresh 1 MB stack, and the calling thread waits for it: the same technique .NET's own Regex parser uses. The
/// nesting limits bound the total, so this happens at most a few times per formula or drawing, and only for input
/// nested far deeper than real files. Where no thread can be started (WebAssembly), or after
/// <see cref="MaxHops"/> fresh stacks, the stack check refuses the input, as the second guard.
/// </summary>
internal static class FreshStack
{
    /// <summary>The most fresh stacks one piece of work may use; far more than 256 levels need.</summary>
    internal const int MaxHops = 8;

    private const int StackSize = 1024 * 1024;

    [ThreadStatic] private static int t_hops;

    /// <summary>True when the current stack has room for another level of recursion.</summary>
    public static bool HasRoom => RuntimeHelpers.TryEnsureSufficientExecutionStack();

    /// <summary>Runs <paramref name="body"/> on a fresh stack and returns its result, or rethrows its exception.</summary>
    public static T Run<T>(Func<T> body)
    {
        int hops = t_hops + 1;
        if (hops > MaxHops) throw new InsufficientExecutionStackException();
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        T result = default!;
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            t_hops = hops;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
            try { result = body(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        }, StackSize)
        { IsBackground = true, Name = "Chuvadi.Sheets deep nesting" };
        try { thread.Start(); }
        catch (Exception ex) when (ex is PlatformNotSupportedException or OutOfMemoryException or ThreadStartException)
        {
            throw new InsufficientExecutionStackException("No thread could be started to continue on a fresh stack.", ex);
        }
        thread.Join();
        error?.Throw();
        return result;
    }
}

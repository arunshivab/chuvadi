using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Chuvadi.Markup.Internal
{
    /// <summary>Cheap periodic cancellation checks for hot loops.</summary>
    internal struct CancellationPoll
    {
        private const int Interval = 4096;
        private readonly CancellationToken _token;
        private int _counter;

        public CancellationPoll(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _token = token;
            _counter = 0;
        }

        public CancellationToken Token => _token;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Tick()
        {
            if (++_counter >= Interval)
            {
                _counter = 0;
                _token.ThrowIfCancellationRequested();
            }
        }

        public void Check() => _token.ThrowIfCancellationRequested();
    }

    /// <summary>Counts created nodes against <see cref="MarkupLimits.MaxNodes"/>.</summary>
    internal sealed class NodeBudget
    {
        private readonly int _max;
        private int _count;

        public NodeBudget(int max) => _max = max;

        public int Count => _count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Take()
        {
            if (++_count > _max)
                throw new MarkupLimitExceededException(nameof(MarkupLimits.MaxNodes), _max, _count);
        }
    }

    internal static class Guard
    {
        public static void InputLength(int length, MarkupLimits limits)
        {
            if (length > limits.MaxInputChars)
                throw new MarkupLimitExceededException(nameof(MarkupLimits.MaxInputChars), limits.MaxInputChars, length);
        }

        public static T NotNull<T>(T? value, string name) where T : class
            => value ?? throw new ArgumentNullException(name);
    }
}

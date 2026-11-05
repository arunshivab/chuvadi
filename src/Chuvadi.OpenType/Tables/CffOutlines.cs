using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType.Tables;

/// <summary>
/// Reads the parts of a 'CFF ' table needed to compute glyph bounds: the INDEX
/// structures, the Top DICT, Private DICTs (or the FDArray and FDSelect of a
/// CID-keyed font) and the Type 2 charstrings with their subroutines.
/// </summary>
internal sealed class CffOutlines
{
    private const int MaxSubrDepth = 10;
    private const int MaxOperations = 200_000;

    private readonly ReadOnlyMemory<byte> _cff;
    private readonly int _glyphCount;
    private readonly bool _usable;
    private readonly CffIndex _charStrings;
    private readonly CffIndex _globalSubrs;
    private readonly CffIndex _localSubrs;
    private readonly CffIndex[] _fdLocalSubrs = Array.Empty<CffIndex>();
    private readonly int _fdSelect = -1;

    // TRACE R-OT-0026
    internal CffOutlines(ReadOnlyMemory<byte> cff, int glyphCount)
    {
        _cff = cff;
        _glyphCount = glyphCount;
        ReadOnlySpan<byte> s = cff.Span;
        if (s.Length < 4 || BigEndian.U8(s, 0) != 1)
        {
            return;
        }

        int hdrSize = BigEndian.U8(s, 2);
        CffIndex names = CffIndex.Read(s, hdrSize);
        CffIndex topDicts = CffIndex.Read(s, names.End);
        CffIndex strings = CffIndex.Read(s, topDicts.End);
        _globalSubrs = CffIndex.Read(s, strings.End);
        if (!names.IsValid || !topDicts.IsValid || topDicts.Count < 1 || !strings.IsValid || !_globalSubrs.IsValid)
        {
            return;
        }

        Dictionary<int, double[]> top = ParseDict(topDicts.Element(s, 0));
        if (top.TryGetValue(1206, out double[]? csType) && csType.Length == 1 && csType[0] != 2)
        {
            return; // Only Type 2 charstrings are read.
        }

        if (!top.TryGetValue(17, out double[]? cs) || cs.Length != 1)
        {
            return;
        }

        _charStrings = CffIndex.Read(s, (int)cs[0]);
        if (!_charStrings.IsValid)
        {
            return;
        }

        if (top.ContainsKey(1230) && top.TryGetValue(1236, out double[]? fdArray) && fdArray.Length == 1 &&
            top.TryGetValue(1237, out double[]? fdSelect) && fdSelect.Length == 1)
        {
            // TRACE R-OT-0027
            CffIndex fds = CffIndex.Read(s, (int)fdArray[0]);
            if (!fds.IsValid)
            {
                return;
            }

            _fdLocalSubrs = new CffIndex[fds.Count];
            for (int i = 0; i < fds.Count; i++)
            {
                _fdLocalSubrs[i] = ReadLocalSubrs(s, ParseDict(fds.Element(s, i)));
            }

            _fdSelect = (int)fdSelect[0];
        }
        else
        {
            _localSubrs = ReadLocalSubrs(s, top);
        }

        _usable = true;
    }

    internal bool TryGetBounds(int glyphId, out GlyphBounds bounds)
    {
        bounds = default;
        if (!_usable || (uint)glyphId >= (uint)_glyphCount || glyphId >= _charStrings.Count)
        {
            return false;
        }

        ReadOnlySpan<byte> s = _cff.Span;
        CffIndex local = _localSubrs;
        if (_fdSelect >= 0)
        {
            int fd = FdForGlyph(s, glyphId);
            local = (uint)fd < (uint)_fdLocalSubrs.Length ? _fdLocalSubrs[fd] : default;
        }

        Type2BoundsInterpreter interpreter = new Type2BoundsInterpreter(s, _globalSubrs, local);
        return interpreter.Run(_charStrings.Element(s, glyphId), out bounds);
    }

    // TRACE R-OT-0027
    private int FdForGlyph(ReadOnlySpan<byte> s, int glyphId)
    {
        int format = BigEndian.U8(s, _fdSelect);
        if (format == 0)
        {
            return BigEndian.Has(s, _fdSelect + 1 + glyphId, 1) ? s[_fdSelect + 1 + glyphId] : -1;
        }

        if (format == 3)
        {
            int nRanges = BigEndian.U16(s, _fdSelect + 1);
            for (int i = 0; i < nRanges; i++)
            {
                int p = _fdSelect + 3 + (3 * i);
                int first = BigEndian.U16(s, p);
                int next = BigEndian.U16(s, p + 3);
                if (glyphId >= first && glyphId < next)
                {
                    return BigEndian.U8(s, p + 2);
                }
            }
        }

        return -1;
    }

    private static CffIndex ReadLocalSubrs(ReadOnlySpan<byte> s, Dictionary<int, double[]> dict)
    {
        if (!dict.TryGetValue(18, out double[]? priv) || priv.Length != 2)
        {
            return default;
        }

        int size = (int)priv[0];
        int offset = (int)priv[1];
        Dictionary<int, double[]> p = ParseDict(BigEndian.SliceClamped(s, offset, size));
        if (!p.TryGetValue(19, out double[]? subrs) || subrs.Length != 1)
        {
            return default;
        }

        CffIndex index = CffIndex.Read(s, offset + (int)subrs[0]);
        return index.IsValid ? index : default;
    }

    // TRACE R-OT-0028
    internal static Dictionary<int, double[]> ParseDict(ReadOnlySpan<byte> d)
    {
        Dictionary<int, double[]> result = new Dictionary<int, double[]>();
        List<double> operands = new List<double>();
        int i = 0;
        while (i < d.Length)
        {
            int b0 = d[i];
            if (b0 <= 21)
            {
                int op = b0;
                i++;
                if (b0 == 12)
                {
                    if (i >= d.Length)
                    {
                        break;
                    }

                    op = 1200 + d[i];
                    i++;
                }

                result[op] = operands.ToArray();
                operands.Clear();
            }
            else if (b0 == 28)
            {
                operands.Add(BigEndian.I16(d, i + 1));
                i += 3;
            }
            else if (b0 == 29)
            {
                operands.Add(BigEndian.I32(d, i + 1));
                i += 5;
            }
            else if (b0 == 30)
            {
                i++;
                operands.Add(ParseReal(d, ref i));
            }
            else if (b0 >= 32 && b0 <= 246)
            {
                operands.Add(b0 - 139);
                i++;
            }
            else if (b0 >= 247 && b0 <= 250)
            {
                operands.Add(((b0 - 247) * 256) + BigEndian.U8(d, i + 1) + 108);
                i += 2;
            }
            else if (b0 >= 251 && b0 <= 254)
            {
                operands.Add((-(b0 - 251) * 256) - BigEndian.U8(d, i + 1) - 108);
                i += 2;
            }
            else
            {
                // Reserved byte: skip it.
                i++;
            }

            if (operands.Count > 513)
            {
                break;
            }
        }

        return result;
    }

    private static double ParseReal(ReadOnlySpan<byte> d, ref int i)
    {
        StringBuilder sb = new StringBuilder();
        bool done = false;
        while (i < d.Length && !done)
        {
            int b = d[i++];
            for (int k = 0; k < 2 && !done; k++)
            {
                int n = k == 0 ? b >> 4 : b & 0xF;
                switch (n)
                {
                    case <= 9: sb.Append((char)('0' + n)); break;
                    case 0xA: sb.Append('.'); break;
                    case 0xB: sb.Append('E'); break;
                    case 0xC: sb.Append("E-"); break;
                    case 0xE: sb.Append('-'); break;
                    case 0xF: done = true; break;
                    default: break;
                }
            }
        }

        return double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
    }

    /// <summary>A CFF INDEX: count, offSize, offsets (1-based), then data.</summary>
    internal readonly struct CffIndex
    {
        private readonly int _offsetsStart;
        private readonly int _offSize;

        private CffIndex(int count, int offSize, int offsetsStart, int end, bool valid)
        {
            Count = count;
            _offSize = offSize;
            _offsetsStart = offsetsStart;
            End = end;
            IsValid = valid;
        }

        internal int Count { get; }

        internal int End { get; }

        internal bool IsValid { get; }

        // TRACE R-OT-0026
        internal static CffIndex Read(ReadOnlySpan<byte> s, int offset)
        {
            if (!BigEndian.Has(s, offset, 2))
            {
                return new CffIndex(0, 0, 0, offset, false);
            }

            int count = BigEndian.U16(s, offset);
            if (count == 0)
            {
                return new CffIndex(0, 0, 0, offset + 2, true);
            }

            int offSize = BigEndian.U8(s, offset + 2);
            if (offSize < 1 || offSize > 4 || !BigEndian.Has(s, offset + 3, (count + 1) * offSize))
            {
                return new CffIndex(0, 0, 0, offset, false);
            }

            int offsetsStart = offset + 3;
            long last = ReadOffset(s, offsetsStart + (count * offSize), offSize);
            long end = offsetsStart + ((long)(count + 1) * offSize) + last - 1;
            if (last < 1 || end > s.Length)
            {
                return new CffIndex(0, 0, 0, offset, false);
            }

            return new CffIndex(count, offSize, offsetsStart, (int)end, true);
        }

        internal ReadOnlySpan<byte> Element(ReadOnlySpan<byte> s, int i)
        {
            if (!IsValid || (uint)i >= (uint)Count)
            {
                return ReadOnlySpan<byte>.Empty;
            }

            long dataBase = _offsetsStart + ((long)(Count + 1) * _offSize) - 1;
            long a = ReadOffset(s, _offsetsStart + (i * _offSize), _offSize);
            long b = ReadOffset(s, _offsetsStart + ((i + 1) * _offSize), _offSize);
            if (a < 1 || b < a || dataBase + b > End)
            {
                return ReadOnlySpan<byte>.Empty;
            }

            return s.Slice((int)(dataBase + a), (int)(b - a));
        }

        private static long ReadOffset(ReadOnlySpan<byte> s, int p, int size)
        {
            long v = 0;
            for (int k = 0; k < size; k++)
            {
                v = (v << 8) | BigEndian.U8(s, p + k);
            }

            return v;
        }
    }

    /// <summary>Runs a Type 2 charstring and records the extent of the outline it draws.</summary>
    private ref struct Type2BoundsInterpreter
    {
        private readonly ReadOnlySpan<byte> _cff;
        private readonly CffIndex _global;
        private readonly CffIndex _local;
        private readonly double[] _stack;
        private readonly double[] _transient;
        private int _sp;
        private int _stems;
        private bool _widthSeen;
        private bool _ended;
        private int _operations;
        private double _x;
        private double _y;
        private bool _startIncluded;
        private bool _any;
        private double _minX;
        private double _minY;
        private double _maxX;
        private double _maxY;

        internal Type2BoundsInterpreter(ReadOnlySpan<byte> cff, CffIndex global, CffIndex local)
        {
            _cff = cff;
            _global = global;
            _local = local;
            _stack = new double[48];
            _transient = new double[32];
            _sp = 0;
            _stems = 0;
            _widthSeen = false;
            _ended = false;
            _operations = 0;
            _x = 0;
            _y = 0;
            _startIncluded = false;
            _any = false;
            _minX = double.MaxValue;
            _minY = double.MaxValue;
            _maxX = double.MinValue;
            _maxY = double.MinValue;
        }

        // TRACE R-OT-0029
        internal bool Run(ReadOnlySpan<byte> charstring, out GlyphBounds bounds)
        {
            Execute(charstring, 0);
            if (!_any)
            {
                bounds = default;
                return false;
            }

            bounds = new GlyphBounds((int)Math.Floor(_minX), (int)Math.Floor(_minY), (int)Math.Ceiling(_maxX), (int)Math.Ceiling(_maxY));
            return true;
        }

        private void Execute(ReadOnlySpan<byte> cs, int depth)
        {
            int i = 0;
            while (i < cs.Length && !_ended)
            {
                if (++_operations > MaxOperations)
                {
                    _ended = true;
                    return;
                }

                int b0 = cs[i];
                if (b0 >= 32 || b0 == 28)
                {
                    double v;
                    if (b0 == 28)
                    {
                        v = BigEndian.I16(cs, i + 1);
                        i += 3;
                    }
                    else if (b0 <= 246)
                    {
                        v = b0 - 139;
                        i++;
                    }
                    else if (b0 <= 250)
                    {
                        v = ((b0 - 247) * 256) + BigEndian.U8(cs, i + 1) + 108;
                        i += 2;
                    }
                    else if (b0 <= 254)
                    {
                        v = (-(b0 - 251) * 256) - BigEndian.U8(cs, i + 1) - 108;
                        i += 2;
                    }
                    else
                    {
                        v = BigEndian.I32(cs, i + 1) / 65536.0;
                        i += 5;
                    }

                    Push(v);
                    continue;
                }

                i++;
                switch (b0)
                {
                    case 1: // hstem
                    case 3: // vstem
                    case 18: // hstemhm
                    case 23: // vstemhm
                        TakeWidth(_sp % 2 == 1);
                        _stems += _sp / 2;
                        _sp = 0;
                        break;

                    case 19: // hintmask
                    case 20: // cntrmask
                        TakeWidth(_sp % 2 == 1);
                        _stems += _sp / 2;
                        _sp = 0;
                        i += (_stems + 7) / 8;
                        break;

                    case 21: // rmoveto
                        TakeWidth(_sp > 2);
                        MoveTo(_x + Arg(0), _y + Arg(1));
                        _sp = 0;
                        break;

                    case 22: // hmoveto
                        TakeWidth(_sp > 1);
                        MoveTo(_x + Arg(0), _y);
                        _sp = 0;
                        break;

                    case 4: // vmoveto
                        TakeWidth(_sp > 1);
                        MoveTo(_x, _y + Arg(0));
                        _sp = 0;
                        break;

                    case 5: // rlineto
                        for (int k = 0; k + 1 < _sp; k += 2)
                        {
                            LineTo(_x + _stack[k], _y + _stack[k + 1]);
                        }

                        _sp = 0;
                        break;

                    case 6: // hlineto
                    case 7: // vlineto
                        {
                            bool horizontal = b0 == 6;
                            for (int k = 0; k < _sp; k++)
                            {
                                if (horizontal)
                                {
                                    LineTo(_x + _stack[k], _y);
                                }
                                else
                                {
                                    LineTo(_x, _y + _stack[k]);
                                }

                                horizontal = !horizontal;
                            }

                            _sp = 0;
                            break;
                        }

                    case 8: // rrcurveto
                        for (int k = 0; k + 5 < _sp; k += 6)
                        {
                            RCurve(_stack[k], _stack[k + 1], _stack[k + 2], _stack[k + 3], _stack[k + 4], _stack[k + 5]);
                        }

                        _sp = 0;
                        break;

                    case 24: // rcurveline
                        {
                            int k = 0;
                            for (; k + 5 < _sp - 2; k += 6)
                            {
                                RCurve(_stack[k], _stack[k + 1], _stack[k + 2], _stack[k + 3], _stack[k + 4], _stack[k + 5]);
                            }

                            if (k + 1 < _sp)
                            {
                                LineTo(_x + _stack[k], _y + _stack[k + 1]);
                            }

                            _sp = 0;
                            break;
                        }

                    case 25: // rlinecurve
                        {
                            int k = 0;
                            for (; k + 1 < _sp - 6; k += 2)
                            {
                                LineTo(_x + _stack[k], _y + _stack[k + 1]);
                            }

                            if (k + 5 < _sp)
                            {
                                RCurve(_stack[k], _stack[k + 1], _stack[k + 2], _stack[k + 3], _stack[k + 4], _stack[k + 5]);
                            }

                            _sp = 0;
                            break;
                        }

                    case 26: // vvcurveto
                        {
                            int k = 0;
                            double dx1 = 0;
                            if (_sp % 2 == 1)
                            {
                                dx1 = _stack[0];
                                k = 1;
                            }

                            for (; k + 3 < _sp; k += 4)
                            {
                                RCurve(dx1, _stack[k], _stack[k + 1], _stack[k + 2], 0, _stack[k + 3]);
                                dx1 = 0;
                            }

                            _sp = 0;
                            break;
                        }

                    case 27: // hhcurveto
                        {
                            int k = 0;
                            double dy1 = 0;
                            if (_sp % 2 == 1)
                            {
                                dy1 = _stack[0];
                                k = 1;
                            }

                            for (; k + 3 < _sp; k += 4)
                            {
                                RCurve(_stack[k], dy1, _stack[k + 1], _stack[k + 2], _stack[k + 3], 0);
                                dy1 = 0;
                            }

                            _sp = 0;
                            break;
                        }

                    case 30: // vhcurveto
                    case 31: // hvcurveto
                        AlternatingCurves(b0 == 31);
                        _sp = 0;
                        break;

                    case 10: // callsubr
                    case 29: // callgsubr
                        {
                            if (_sp < 1 || depth >= MaxSubrDepth)
                            {
                                _ended = true;
                                return;
                            }

                            CffIndex subrs = b0 == 10 ? _local : _global;
                            int bias = subrs.Count < 1240 ? 107 : subrs.Count < 33900 ? 1131 : 32768;
                            int n = (int)_stack[--_sp] + bias;
                            if (!subrs.IsValid || (uint)n >= (uint)subrs.Count)
                            {
                                _ended = true;
                                return;
                            }

                            Execute(subrs.Element(_cff, n), depth + 1);
                            break;
                        }

                    case 11: // return
                        return;

                    case 14: // endchar
                        TakeWidth(_sp == 1 || _sp == 5);
                        // TRACE R-OT-0030
                        _ended = true;
                        return;

                    case 12:
                        {
                            int b1 = i < cs.Length ? cs[i] : -1;
                            i++;
                            Escape(b1);
                            break;
                        }

                    default:
                        // Reserved operator: clear the stack.
                        _sp = 0;
                        break;
                }
            }
        }

        private void Escape(int op)
        {
            switch (op)
            {
                case 35: // flex
                    if (_sp >= 12)
                    {
                        RCurve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                        RCurve(_stack[6], _stack[7], _stack[8], _stack[9], _stack[10], _stack[11]);
                    }

                    _sp = 0;
                    break;

                case 34: // hflex
                    if (_sp >= 7)
                    {
                        double y0 = _y;
                        RCurve(_stack[0], 0, _stack[1], _stack[2], _stack[3], 0);
                        RCurve(_stack[4], 0, _stack[5], y0 - _y, _stack[6], 0);
                    }

                    _sp = 0;
                    break;

                case 36: // hflex1
                    if (_sp >= 9)
                    {
                        double y0 = _y;
                        RCurve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], 0);
                        double yAfter = _y;
                        double dy6 = y0 - (yAfter + _stack[7]);
                        RCurve(_stack[5], 0, _stack[6], _stack[7], _stack[8], dy6);
                    }

                    _sp = 0;
                    break;

                case 37: // flex1
                    if (_sp >= 11)
                    {
                        double dx = 0;
                        double dy = 0;
                        for (int k = 0; k < 10; k += 2)
                        {
                            dx += _stack[k];
                            dy += _stack[k + 1];
                        }

                        double x0 = _x;
                        double y0 = _y;
                        RCurve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                        double c1x = _x + _stack[6];
                        double c1y = _y + _stack[7];
                        double c2x = c1x + _stack[8];
                        double c2y = c1y + _stack[9];
                        double ex;
                        double ey;
                        if (Math.Abs(dx) > Math.Abs(dy))
                        {
                            ex = c2x + _stack[10];
                            ey = y0;
                        }
                        else
                        {
                            ex = x0;
                            ey = c2y + _stack[10];
                        }

                        CurveTo(c1x, c1y, c2x, c2y, ex, ey);
                    }

                    _sp = 0;
                    break;

                // Arithmetic and storage operators (Type 2 charstring format, section 4.4 and 4.5).
                case 3: Binary((a, b) => a != 0 && b != 0 ? 1 : 0); break; // and
                case 4: Binary((a, b) => a != 0 || b != 0 ? 1 : 0); break; // or
                case 5: Unary(a => a == 0 ? 1 : 0); break; // not
                case 9: Unary(Math.Abs); break; // abs
                case 10: Binary((a, b) => a + b); break; // add
                case 11: Binary((a, b) => a - b); break; // sub
                case 12: Binary((a, b) => b == 0 ? 0 : a / b); break; // div
                case 14: Unary(a => -a); break; // neg
                case 15: Binary((a, b) => a == b ? 1 : 0); break; // eq
                case 18: // drop
                    if (_sp > 0)
                    {
                        _sp--;
                    }

                    break;
                case 20: // put
                    if (_sp >= 2)
                    {
                        int idx = (int)_stack[_sp - 1];
                        if ((uint)idx < (uint)_transient.Length)
                        {
                            _transient[idx] = _stack[_sp - 2];
                        }

                        _sp -= 2;
                    }

                    break;
                case 21: // get
                    if (_sp >= 1)
                    {
                        int idx = (int)_stack[_sp - 1];
                        _stack[_sp - 1] = (uint)idx < (uint)_transient.Length ? _transient[idx] : 0;
                    }

                    break;
                case 22: // ifelse
                    if (_sp >= 4)
                    {
                        double s1 = _stack[_sp - 4];
                        double s2 = _stack[_sp - 3];
                        double v1 = _stack[_sp - 2];
                        double v2 = _stack[_sp - 1];
                        _sp -= 4;
                        Push(v1 <= v2 ? s1 : s2);
                    }

                    break;
                case 23: // random: a fixed value in (0, 1] so results stay deterministic
                    Push(0.5);
                    break;
                case 24: Binary((a, b) => a * b); break; // mul
                case 26: Unary(a => a < 0 ? 0 : Math.Sqrt(a)); break; // sqrt
                case 27: // dup
                    if (_sp >= 1)
                    {
                        Push(_stack[_sp - 1]);
                    }

                    break;
                case 28: // exch
                    if (_sp >= 2)
                    {
                        (_stack[_sp - 1], _stack[_sp - 2]) = (_stack[_sp - 2], _stack[_sp - 1]);
                    }

                    break;
                case 29: // index
                    if (_sp >= 1)
                    {
                        int idx = (int)_stack[_sp - 1];
                        if (idx < 0)
                        {
                            idx = 0;
                        }

                        int src = _sp - 2 - idx;
                        _stack[_sp - 1] = src >= 0 ? _stack[src] : 0;
                    }

                    break;
                case 30: // roll
                    if (_sp >= 2)
                    {
                        int n = (int)_stack[_sp - 2];
                        int j = (int)_stack[_sp - 1];
                        _sp -= 2;
                        if (n > 0 && n <= _sp)
                        {
                            int start = _sp - n;
                            double[] tmp = new double[n];
                            for (int k = 0; k < n; k++)
                            {
                                int target = (((k + j) % n) + n) % n;
                                tmp[target] = _stack[start + k];
                            }

                            Array.Copy(tmp, 0, _stack, start, n);
                        }
                    }

                    break;
                default:
                    _sp = 0;
                    break;
            }
        }

        private void AlternatingCurves(bool startHorizontal)
        {
            bool horizontal = startHorizontal;
            int k = 0;
            while (k + 3 < _sp)
            {
                bool last = _sp - k == 5;
                double extra = last ? _stack[k + 4] : 0;
                if (horizontal)
                {
                    RCurve(_stack[k], 0, _stack[k + 1], _stack[k + 2], last ? extra : 0, _stack[k + 3]);
                }
                else
                {
                    RCurve(0, _stack[k], _stack[k + 1], _stack[k + 2], _stack[k + 3], last ? extra : 0);
                }

                k += 4;
                horizontal = !horizontal;
            }
        }

        private void Unary(Func<double, double> f)
        {
            if (_sp >= 1)
            {
                _stack[_sp - 1] = f(_stack[_sp - 1]);
            }
        }

        private void Binary(Func<double, double, double> f)
        {
            if (_sp >= 2)
            {
                double b = _stack[--_sp];
                _stack[_sp - 1] = f(_stack[_sp - 1], b);
            }
        }

        private void Push(double v)
        {
            if (_sp < _stack.Length)
            {
                _stack[_sp++] = v;
            }
        }

        private void TakeWidth(bool hasWidth)
        {
            if (!_widthSeen)
            {
                _widthSeen = true;
                if (hasWidth && _sp > 0)
                {
                    Array.Copy(_stack, 1, _stack, 0, _sp - 1);
                    _sp--;
                }
            }
        }

        private double Arg(int k) => k < _sp ? _stack[k] : 0;

        private void MoveTo(double x, double y)
        {
            _x = x;
            _y = y;
            _startIncluded = false;
        }

        private void IncludeStart()
        {
            if (!_startIncluded)
            {
                Include(_x, _y);
                _startIncluded = true;
            }
        }

        private void LineTo(double x, double y)
        {
            IncludeStart();
            _x = x;
            _y = y;
            Include(x, y);
        }

        private void RCurve(double dxa, double dya, double dxb, double dyb, double dxc, double dyc)
        {
            double x1 = _x + dxa;
            double y1 = _y + dya;
            double x2 = x1 + dxb;
            double y2 = y1 + dyb;
            CurveTo(x1, y1, x2, y2, x2 + dxc, y2 + dyc);
        }

        private void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            IncludeStart();
            ExtremaX(_x, x1, x2, x3);
            ExtremaY(_y, y1, y2, y3);
            _x = x3;
            _y = y3;
            Include(x3, y3);
        }

        private void ExtremaX(double p0, double p1, double p2, double p3)
        {
            foreach (double t in Roots(p0, p1, p2, p3))
            {
                Include(Cubic(p0, p1, p2, p3, t), double.NaN);
            }
        }

        private void ExtremaY(double p0, double p1, double p2, double p3)
        {
            foreach (double t in Roots(p0, p1, p2, p3))
            {
                Include(double.NaN, Cubic(p0, p1, p2, p3, t));
            }
        }

        private static double Cubic(double p0, double p1, double p2, double p3, double t)
        {
            double u = 1 - t;
            return (u * u * u * p0) + (3 * u * u * t * p1) + (3 * u * t * t * p2) + (t * t * t * p3);
        }

        // Parameters t in (0, 1) where the derivative of the cubic is zero.
        private static double[] Roots(double p0, double p1, double p2, double p3)
        {
            double a = p3 - (3 * p2) + (3 * p1) - p0;
            double b = 2 * (p2 - (2 * p1) + p0);
            double c = p1 - p0;
            List<double> r = new List<double>(2);
            if (Math.Abs(a) < 1e-12)
            {
                if (Math.Abs(b) > 1e-12)
                {
                    r.Add(-c / b);
                }
            }
            else
            {
                double disc = (b * b) - (4 * a * c);
                if (disc >= 0)
                {
                    double sq = Math.Sqrt(disc);
                    r.Add((-b + sq) / (2 * a));
                    r.Add((-b - sq) / (2 * a));
                }
            }

            r.RemoveAll(t => !(t > 0 && t < 1));
            return r.ToArray();
        }

        private void Include(double x, double y)
        {
            if (!double.IsNaN(x))
            {
                _minX = Math.Min(_minX, x);
                _maxX = Math.Max(_maxX, x);
            }

            if (!double.IsNaN(y))
            {
                _minY = Math.Min(_minY, y);
                _maxY = Math.Max(_maxY, y);
            }

            if (!double.IsNaN(x) && !double.IsNaN(y))
            {
                _any = true;
            }
        }
    }
}

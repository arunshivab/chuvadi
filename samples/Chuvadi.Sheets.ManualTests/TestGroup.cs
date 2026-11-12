using System;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// The pass/fail bookkeeping the 2.1.0 groups share (saves, headers, streamreads, widths, cellref, displaytext,
/// limits): each check prints PASS or FAIL, and <see cref="Finish"/> throws with the failed checks listed.
/// </summary>
internal sealed class TestGroup
{
    private readonly string _name;
    private readonly FailedChecks _failed = new();
    private int _pass, _fail;

    public TestGroup(string name, string title)
    {
        _name = name;
        Console.WriteLine($"[{_name}] {title}");
        Console.WriteLine();
    }

    public void Check(string label, Action body)
    {
        try { body(); Console.WriteLine($"    PASS: {label}"); _pass++; }
        catch (Exception ex)
        {
            Console.WriteLine($"    FAIL: {label}");
            Console.WriteLine($"          {ex.GetType().Name}: {ex.Message}");
            _fail++;
            _failed.Add(label, ex);
        }
    }

    public void Finish()
    {
        Console.WriteLine();
        Console.WriteLine($"[{_name}] {_pass} passed, {_fail} failed.");
        Console.WriteLine();
        if (_fail > 0) throw new Exception(_failed.Summary($"{_name}: {_fail} check(s) failed."));
    }

    public static void True(bool condition, string what)
    {
        if (!condition) throw new Exception(what);
    }

    public static void Eq(object? expected, object? actual, string what)
    {
        if (!Equals(expected, actual)) throw new Exception($"{what}: expected <{expected}> got <{actual}>");
    }

    /// <summary>Runs <paramref name="body"/> and checks it throws <typeparamref name="TEx"/> (exactly that type).</summary>
    public static TEx Throws<TEx>(Action body, string what) where TEx : Exception
    {
        try { body(); }
        catch (TEx ex) when (ex.GetType() == typeof(TEx)) { return ex; }
        catch (Exception ex) { throw new Exception($"{what}: expected {typeof(TEx).Name}, got {ex.GetType().Name}: {ex.Message}"); }
        throw new Exception($"{what}: expected {typeof(TEx).Name}, nothing was thrown");
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Describes one column derived from a property of <c>T</c>.
/// </summary>
internal sealed class ColumnPlan
{
    public required string PropertyName { get; init; }
    public required string Header { get; init; }
    public required Type PropertyType { get; init; }
    public int Order { get; init; } = ColumnAttribute.DefaultOrder;
    public double Width { get; init; }                // 0 = unset
    public string? Format { get; init; }              // numFmt code; null = unset
    public CellStyle? Style { get; init; }            // per-data-cell style; null = unset
    public required int DeclaredIndex { get; init; }  // index of property in declaration order; tie-breaker for Order

    /// <summary>Returns a copy with the given fluent-config overrides applied.</summary>
    public ColumnPlan WithOverrides(string? header, double width, string? format, CellStyle? style) => new()
    {
        PropertyName  = PropertyName,
        Header        = header ?? Header,
        PropertyType  = PropertyType,
        Order         = Order,
        Width         = width > 0 ? width : Width,
        Format        = format ?? Format,
        Style         = style ?? Style,
        DeclaredIndex = DeclaredIndex,
    };
}

/// <summary>
/// Caches per-type column metadata and a compiled extraction delegate. The first export of
/// a given <c>T</c> pays the reflection cost once; every subsequent row uses the compiled
/// delegate (a few method calls per row, no PropertyInfo.GetValue in the hot path).
///
/// Thread-safe via static initialization on a generic type.
/// </summary>
internal static class TypeAccessor<T>
{
    /// <summary>The columns derived from <c>T</c>'s public properties (attribute-aware), sorted.</summary>
    public static readonly IReadOnlyList<ColumnPlan> Columns;

    /// <summary>
    /// Compiled extraction delegate. Given an instance of <c>T</c> and a pre-allocated
    /// object?[] buffer of length <c>Columns.Count</c>, populates the buffer with the
    /// current property values. Callers reuse the same buffer across rows.
    /// </summary>
    public static readonly Action<T, object?[]> Extract;

    static TypeAccessor()
    {
        Columns = BuildColumns();
        Extract = CompileExtractor(Columns);
    }

    private static IReadOnlyList<ColumnPlan> BuildColumns()
    {
        var t = typeof(T);
        var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)  // skip indexers
            .ToList();

        var plans = new List<ColumnPlan>(props.Count);
        for (int i = 0; i < props.Count; i++)
        {
            var p = props[i];
            if (p.GetCustomAttribute<ColumnIgnoreAttribute>() is not null)
                continue;

            var colAttr = p.GetCustomAttribute<ColumnAttribute>();
            var styleAttr = p.GetCustomAttribute<ColumnStyleAttribute>();

            plans.Add(new ColumnPlan
            {
                PropertyName  = p.Name,
                Header        = !string.IsNullOrEmpty(colAttr?.Header) ? colAttr!.Header! : p.Name,
                PropertyType  = p.PropertyType,
                Order         = colAttr?.Order ?? ColumnAttribute.DefaultOrder,
                Width         = colAttr?.Width ?? 0,
                Format        = colAttr?.Format ?? styleAttr?.Format,
                Style         = styleAttr is not null ? styleAttr.ToCellStyle() : null,
                DeclaredIndex = i,
            });
        }

        return plans.OrderBy(c => c.Order).ThenBy(c => c.DeclaredIndex).ToList();
    }

    /// <summary>
    /// Compiles a delegate that reads each property into a buffer:
    ///   (T instance, object?[] buffer) => { buffer[0] = instance.Prop0; buffer[1] = instance.Prop1; ... }
    /// </summary>
    private static Action<T, object?[]> CompileExtractor(IReadOnlyList<ColumnPlan> columns)
    {
        if (columns.Count == 0)
            return static (_, _) => { };  // No columns — extractor is a no-op.

        var t = typeof(T);
        var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, p => p);

        var instanceParam = Expression.Parameter(t, "instance");
        var bufferParam = Expression.Parameter(typeof(object?[]), "buffer");
        var body = new List<Expression>(columns.Count);

        for (int i = 0; i < columns.Count; i++)
        {
            var prop = props[columns[i].PropertyName];
            // (object?)instance.<Prop>
            var value = Expression.Property(instanceParam, prop);
            var boxed = prop.PropertyType.IsValueType
                ? Expression.Convert(value, typeof(object))
                : (Expression)value;
            // buffer[i] = <boxed>;
            var assign = Expression.Assign(
                Expression.ArrayAccess(bufferParam, Expression.Constant(i)),
                boxed);
            body.Add(assign);
        }

        var lambda = Expression.Lambda<Action<T, object?[]>>(
            Expression.Block(body),
            instanceParam, bufferParam);
        return lambda.Compile();
    }
}

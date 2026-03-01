namespace Extension.Models;

public enum LayoutKind { Cards, Grid }

public enum CountStyle
{
    Total,       // "(3)" — no filtering, showing everything
    ForProfile,  // "(3 for profile)" — filtered to active profile
    OfTotal,     // "(3 of 10)" — filtered, with total shown for context
    OfSubTotal   // "(3 of 10 in profile)" — filtered, with subtotal context
}

public record FilterSet<T>(string Name, List<Func<T, bool>> Filters);

public record SortExpression<T>(Func<T, object> KeySelector, bool Descending = false);

public record SortSet<T>(string Name, List<SortExpression<T>> Expressions);

public record ViewDef<T>(
    string Id,
    string Name,
    List<FilterSet<T>>? FilterSets = null,
    List<FilterSet<T>>? SubtotalFilterSets = null,
    CountStyle CountStyle = CountStyle.Total,
    LayoutKind LayoutKind = LayoutKind.Cards,
    List<SortSet<T>>? SortSets = null
);

public static class ViewDefHelper
{
    public static IEnumerable<T> ApplyFilters<T>(IEnumerable<T> items, ViewDef<T>? viewDef)
    {
        if (viewDef?.FilterSets is null or [])
        {
            return items;
        }
        return items.Where(item =>
            viewDef.FilterSets.Any(fs => fs.Filters.All(f => f(item))));
    }

    public static int ApplySubtotalCount<T>(IEnumerable<T> items, ViewDef<T>? viewDef)
    {
        if (viewDef?.SubtotalFilterSets is null or [])
        {
            return items.Count();
        }
        return items.Count(item =>
            viewDef.SubtotalFilterSets.Any(fs => fs.Filters.All(f => f(item))));
    }

    public static IEnumerable<T> ApplySort<T>(IEnumerable<T> items, ViewDef<T>? viewDef)
    {
        if (viewDef?.SortSets is null or [])
        {
            return items;
        }
        var sortSet = viewDef.SortSets[0];
        if (sortSet.Expressions.Count == 0)
        {
            return items;
        }
        var first = sortSet.Expressions[0];
        IOrderedEnumerable<T> ordered = first.Descending
            ? items.OrderByDescending(first.KeySelector)
            : items.OrderBy(first.KeySelector);
        for (int i = 1; i < sortSet.Expressions.Count; i++)
        {
            var expr = sortSet.Expressions[i];
            ordered = expr.Descending
                ? ordered.ThenByDescending(expr.KeySelector)
                : ordered.ThenBy(expr.KeySelector);
        }
        return ordered;
    }

    public static List<T> Apply<T>(IEnumerable<T> items, ViewDef<T>? viewDef)
    {
        return [.. ApplySort(ApplyFilters(items, viewDef), viewDef)];
    }
}

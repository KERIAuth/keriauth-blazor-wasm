namespace Extension.Tests.Models;

using Extension.Models;

public class ViewDefTests
{
    private sealed record TestItem(string Name, int Value, string Category);

    private static readonly List<TestItem> SampleItems =
    [
        new("Alice", 10, "A"),
        new("Bob", 20, "B"),
        new("Charlie", 30, "A"),
        new("Diana", 40, "B"),
        new("Eve", 50, "C")
    ];

    // ApplyFilters tests

    [Fact]
    public void ApplyFilters_NullViewDef_ReturnsAllItems()
    {
        var result = ViewDefHelper.ApplyFilters<TestItem>(SampleItems, null).ToList();
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void ApplyFilters_NullFilterSets_ReturnsAllItems()
    {
        var viewDef = new ViewDef<TestItem>("test", "Test");
        var result = ViewDefHelper.ApplyFilters(SampleItems, viewDef).ToList();
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void ApplyFilters_EmptyFilterSets_ReturnsAllItems()
    {
        var viewDef = new ViewDef<TestItem>("test", "Test", FilterSets: []);
        var result = ViewDefHelper.ApplyFilters(SampleItems, viewDef).ToList();
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void ApplyFilters_SingleFilterSet_SingleFilter_FiltersCorrectly()
    {
        var viewDef = new ViewDef<TestItem>("test", "Test",
            FilterSets: [new("Cat", [i => i.Category == "A"])]);
        var result = ViewDefHelper.ApplyFilters(SampleItems, viewDef).ToList();
        Assert.Equal(2, result.Count);
        Assert.All(result, i => Assert.Equal("A", i.Category));
    }

    [Fact]
    public void ApplyFilters_SingleFilterSet_ANDsWithinSet()
    {
        // Category == "A" AND Value > 15 => only Charlie(30, A)
        var viewDef = new ViewDef<TestItem>("test", "Test",
            FilterSets: [new("Combined", [
                i => i.Category == "A",
                i => i.Value > 15
            ])]);
        var result = ViewDefHelper.ApplyFilters(SampleItems, viewDef).ToList();
        Assert.Single(result);
        Assert.Equal("Charlie", result[0].Name);
    }

    [Fact]
    public void ApplyFilters_MultipleFilterSets_ORsAcrossSets()
    {
        // Category == "A" OR Category == "C" => Alice, Charlie, Eve
        var viewDef = new ViewDef<TestItem>("test", "Test",
            FilterSets: [
                new("CatA", [i => i.Category == "A"]),
                new("CatC", [i => i.Category == "C"])
            ]);
        var result = ViewDefHelper.ApplyFilters(SampleItems, viewDef).ToList();
        Assert.Equal(3, result.Count);
        Assert.Contains(result, i => i.Name == "Alice");
        Assert.Contains(result, i => i.Name == "Charlie");
        Assert.Contains(result, i => i.Name == "Eve");
    }

    [Fact]
    public void ApplyFilters_MultipleFilterSets_ANDWithinORBetween()
    {
        // (Category == "A" AND Value > 15) OR (Category == "B" AND Value < 30)
        // => Charlie(30,A) OR Bob(20,B) => 2 items
        var viewDef = new ViewDef<TestItem>("test", "Test",
            FilterSets: [
                new("HighA", [i => i.Category == "A", i => i.Value > 15]),
                new("LowB", [i => i.Category == "B", i => i.Value < 30])
            ]);
        var result = ViewDefHelper.ApplyFilters(SampleItems, viewDef).ToList();
        Assert.Equal(2, result.Count);
        Assert.Contains(result, i => i.Name == "Charlie");
        Assert.Contains(result, i => i.Name == "Bob");
    }

    // ApplySubtotalCount tests

    [Fact]
    public void ApplySubtotalCount_NullSubtotalFilterSets_ReturnsTotalCount()
    {
        var viewDef = new ViewDef<TestItem>("test", "Test");
        var count = ViewDefHelper.ApplySubtotalCount(SampleItems, viewDef);
        Assert.Equal(5, count);
    }

    [Fact]
    public void ApplySubtotalCount_WithSubtotalFilterSets_ReturnsFilteredCount()
    {
        // Subtotal: Category == "A" OR Category == "B" => 4 items
        var viewDef = new ViewDef<TestItem>("test", "Test",
            SubtotalFilterSets: [
                new("CatA", [i => i.Category == "A"]),
                new("CatB", [i => i.Category == "B"])
            ]);
        var count = ViewDefHelper.ApplySubtotalCount(SampleItems, viewDef);
        Assert.Equal(4, count);
    }

    // ApplySort tests

    [Fact]
    public void ApplySort_NullSortSets_PreservesOrder()
    {
        var viewDef = new ViewDef<TestItem>("test", "Test");
        var result = ViewDefHelper.ApplySort(SampleItems, viewDef).ToList();
        Assert.Equal("Alice", result[0].Name);
        Assert.Equal("Eve", result[4].Name);
    }

    [Fact]
    public void ApplySort_SingleExpression_Ascending()
    {
        var items = new List<TestItem>
        {
            new("Charlie", 30, "A"),
            new("Alice", 10, "A"),
            new("Bob", 20, "B")
        };
        var viewDef = new ViewDef<TestItem>("test", "Test",
            SortSets: [new("ByName", [new SortExpression<TestItem>(i => i.Name)])]);
        var result = ViewDefHelper.ApplySort(items, viewDef).ToList();
        Assert.Equal("Alice", result[0].Name);
        Assert.Equal("Bob", result[1].Name);
        Assert.Equal("Charlie", result[2].Name);
    }

    [Fact]
    public void ApplySort_SingleExpression_Descending()
    {
        var viewDef = new ViewDef<TestItem>("test", "Test",
            SortSets: [new("ByValue", [new SortExpression<TestItem>(i => i.Value, Descending: true)])]);
        var result = ViewDefHelper.ApplySort(SampleItems, viewDef).ToList();
        Assert.Equal(50, result[0].Value);
        Assert.Equal(10, result[4].Value);
    }

    [Fact]
    public void ApplySort_MultipleExpressions_ThenBy()
    {
        // Sort by Category ascending, then Value descending
        var viewDef = new ViewDef<TestItem>("test", "Test",
            SortSets: [new("CatThenValue", [
                new SortExpression<TestItem>(i => i.Category),
                new SortExpression<TestItem>(i => i.Value, Descending: true)
            ])]);
        var result = ViewDefHelper.ApplySort(SampleItems, viewDef).ToList();
        // A: Charlie(30), Alice(10) then B: Diana(40), Bob(20) then C: Eve(50)
        Assert.Equal("Charlie", result[0].Name);
        Assert.Equal("Alice", result[1].Name);
        Assert.Equal("Diana", result[2].Name);
        Assert.Equal("Bob", result[3].Name);
        Assert.Equal("Eve", result[4].Name);
    }

    // Apply (combined) tests

    [Fact]
    public void Apply_CombinesFiltersAndSort()
    {
        // Filter to Category A or B, sort by Value descending
        var viewDef = new ViewDef<TestItem>("test", "Test",
            FilterSets: [
                new("CatA", [i => i.Category == "A"]),
                new("CatB", [i => i.Category == "B"])
            ],
            SortSets: [new("ByValue", [new SortExpression<TestItem>(i => i.Value, Descending: true)])]);
        var result = ViewDefHelper.Apply(SampleItems, viewDef);
        Assert.Equal(4, result.Count);
        Assert.Equal("Diana", result[0].Name);  // 40
        Assert.Equal("Charlie", result[1].Name); // 30
        Assert.Equal("Bob", result[2].Name);     // 20
        Assert.Equal("Alice", result[3].Name);   // 10
    }

    [Fact]
    public void Apply_NoFilterNoSort_ReturnsAllInOriginalOrder()
    {
        var viewDef = new ViewDef<TestItem>("test", "Test");
        var result = ViewDefHelper.Apply(SampleItems, viewDef);
        Assert.Equal(5, result.Count);
        Assert.Equal("Alice", result[0].Name);
    }

    [Fact]
    public void Apply_NullViewDef_ReturnsAllInOriginalOrder()
    {
        var result = ViewDefHelper.Apply<TestItem>(SampleItems, null);
        Assert.Equal(5, result.Count);
    }

    // Edge cases

    [Fact]
    public void ApplyFilters_EmptySourceCollection_ReturnsEmpty()
    {
        var viewDef = new ViewDef<TestItem>("test", "Test",
            FilterSets: [new("Any", [i => i.Category == "A"])]);
        var result = ViewDefHelper.ApplyFilters(new List<TestItem>(), viewDef).ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void ApplyFilters_NoMatchingItems_ReturnsEmpty()
    {
        var viewDef = new ViewDef<TestItem>("test", "Test",
            FilterSets: [new("None", [i => i.Category == "Z"])]);
        var result = ViewDefHelper.ApplyFilters(SampleItems, viewDef).ToList();
        Assert.Empty(result);
    }
}

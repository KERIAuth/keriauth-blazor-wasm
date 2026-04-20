using Extension.Helper;

namespace Extension.Tests.Helper;

public class DefaultSediNameTests
{
    [Fact]
    public void AllPools_Have_TenEntries()
    {
        Assert.Equal(10, DefaultSediName.GetMaleFirsts().Count);
        Assert.Equal(10, DefaultSediName.GetMaleMiddles().Count);
        Assert.Equal(10, DefaultSediName.GetFemaleFirsts().Count);
        Assert.Equal(10, DefaultSediName.GetFemaleMiddles().Count);
        Assert.Equal(10, DefaultSediName.GetLastNames().Count);
    }

    [Fact]
    public void MaleFirsts_And_MaleMiddles_Are_Disjoint()
    {
        var firsts = DefaultSediName.GetMaleFirsts().ToHashSet();
        var middles = DefaultSediName.GetMaleMiddles();
        Assert.DoesNotContain(middles, m => firsts.Contains(m));
    }

    [Fact]
    public void FemaleFirsts_And_FemaleMiddles_Are_Disjoint()
    {
        var firsts = DefaultSediName.GetFemaleFirsts().ToHashSet();
        var middles = DefaultSediName.GetFemaleMiddles();
        Assert.DoesNotContain(middles, m => firsts.Contains(m));
    }

    [Fact]
    public void Generate_WithSeededRandom_IsDeterministic()
    {
        var rng1 = new Random(42);
        var rng2 = new Random(42);
        var name1 = DefaultSediName.Generate(rng1);
        var name2 = DefaultSediName.Generate(rng2);
        Assert.Equal(name1, name2);
    }

    [Fact]
    public void Generate_ReturnsThreeSpaceSeparatedNonEmptyTokens()
    {
        var name = DefaultSediName.Generate(new Random(7));
        var parts = name.Split(' ');
        Assert.Equal(3, parts.Length);
        Assert.All(parts, p => Assert.False(string.IsNullOrWhiteSpace(p)));
    }

    [Fact]
    public void Generate_ProducesBothGenders_AcrossManyCalls()
    {
        // With a uniform coin flip, many calls should produce names from both pools.
        var rng = new Random(0);
        var seenMale = false;
        var seenFemale = false;
        var maleFirsts = DefaultSediName.GetMaleFirsts().ToHashSet();
        var femaleFirsts = DefaultSediName.GetFemaleFirsts().ToHashSet();
        for (var i = 0; i < 100; i++) {
            var first = DefaultSediName.Generate(rng).Split(' ')[0];
            if (maleFirsts.Contains(first)) seenMale = true;
            if (femaleFirsts.Contains(first)) seenFemale = true;
        }
        Assert.True(seenMale, "Expected at least one male first name across 100 samples");
        Assert.True(seenFemale, "Expected at least one female first name across 100 samples");
    }
}

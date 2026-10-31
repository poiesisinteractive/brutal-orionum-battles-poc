using OrionumFleet.Sim;

namespace OrionumFleet.Sim.Tests;

/// <summary>
/// Le RNG doit reproduire bit à bit celui du proto TS (rng.ts au commit 3eae78c).
/// Valeurs de référence obtenues avec Node sur le fichier TS d'origine.
/// </summary>
public class RngTests
{
    [Theory]
    [InlineData(0u, new[] { 0.3588899802416563, 0.10590326134115458, 0.675290479324758, 0.9179345588199794, 0.10157715040259063, 0.30100292386487126 })]
    [InlineData(1u, new[] { 0.6270739405881613, 0.002735721180215478, 0.5274470399599522, 0.9810509674716741, 0.9683778982143849, 0.281103502959013 })]
    [InlineData(42u, new[] { 0.6011037519201636, 0.44829055899754167, 0.8524657934904099, 0.6697340414393693, 0.17481389874592423, 0.5265925421845168 })]
    [InlineData(123456789u, new[] { 0.2577907438389957, 0.9707721115555614, 0.7853280142880976, 0.20616457983851433, 0.30307188746519387, 0.7470660470426083 })]
    [InlineData(4294967295u, new[] { 0.8964226141106337, 0.189478256739676, 0.7156526781618595, 0.9440599093213677, 0.8452364315744489, 0.5391399988438934 })]
    public void Next_matches_the_ts_proto_bit_for_bit(uint seed, double[] expected)
    {
        var rng = new Rng(seed);
        foreach (var value in expected)
            Assert.Equal(value, rng.NextDouble());
    }

    [Theory]
    [InlineData(0u, new[] { 24, 466, 433, 547 })]
    [InlineData(1u, new[] { 612, 720, 425, 994 })]
    [InlineData(42u, new[] { 273, 624, 865, 472 })]
    [InlineData(123456789u, new[] { 778, 284, 16, 161 })]
    public void Int_continues_the_same_stream(uint seed, int[] expected)
    {
        var rng = new Rng(seed);
        for (var i = 0; i < 6; i++)
            rng.NextDouble();

        foreach (var value in expected)
            Assert.Equal(value, rng.Int(1000));
    }

    [Fact]
    public void A_copy_continues_the_same_stream_independently()
    {
        var rng = new Rng(42);
        rng.NextDouble();
        var copy = rng;

        Assert.Equal(rng.NextDouble(), copy.NextDouble());
        Assert.Equal(rng.NextDouble(), copy.NextDouble());
    }
}

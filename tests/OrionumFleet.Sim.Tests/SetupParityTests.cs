using System.Text.Json;

namespace OrionumFleet.Sim.Tests;

/// <summary>
/// Le monde construit en C# doit être identique à celui du proto TS (commit 3eae78c) : mêmes coques,
/// mêmes ids, noms, positions, cooldowns tirés du RNG, mêmes puits, et le RNG au même point ensuite.
/// Références générées par tools/ts-reference/dump-setup.ts.
/// </summary>
public class SetupParityTests
{
    [Theory]
    [InlineData("setup-raid-42.json")]
    [InlineData("setup-raid-7.json")]
    [InlineData("setup-wellfield-3.json")]
    [InlineData("setup-raid-5-noterrain.json")]
    public void World_setup_matches_the_ts_proto(string file)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Reference", file)));
        var root = json.RootElement;

        var scenario = root.GetProperty("scenario").GetString() == "well-field" ? ScenarioId.WellField : ScenarioId.Wolf359Raid;
        var terrain = root.GetProperty("terrain").GetBoolean();
        var w = new World(root.GetProperty("seed").GetUInt32(), new WorldOptions
        {
            Scenario = scenario,
            Pillars = new Pillars(Horloge: true, Terrain: terrain),
        });

        var ships = root.GetProperty("ships");
        Assert.Equal(ships.GetArrayLength(), w.Ships.Count);
        var i = 0;
        foreach (var expected in ships.EnumerateArray())
        {
            ref var s = ref w.Ships[i++];
            Assert.Equal(expected.GetProperty("id").GetInt32(), s.Id);
            Assert.Equal(expected.GetProperty("classId").GetString(), s.Class.Key);
            Assert.Equal(expected.GetProperty("name").GetString(), s.Name);
            Assert.Equal(expected.GetProperty("side").GetString() == "coalition" ? Side.Coalition : Side.Clans, s.Side);
            AssertVec(expected.GetProperty("pos"), s.Pos);
            AssertVec(expected.GetProperty("fwd"), s.Fwd);
            Assert.Equal(expected.GetProperty("hull").GetSingle(), s.Hull);
            Assert.Equal(expected.GetProperty("face").GetSingle(), s.Faces[0]);
            Assert.Equal(expected.GetProperty("subsystemHp").GetSingle(), s.Subsystems[0].Hp);
            Assert.Equal(expected.GetProperty("empAmmo").GetInt32(), s.EmpAmmo);
            Assert.Equal(expected.GetProperty("hangarStock").GetInt32(), s.HasHangar ? s.Hangar.Stock : -1);
            Assert.Equal(expected.GetProperty("orionum").GetBoolean(), s.HasOrionum);

            var cooldowns = expected.GetProperty("cooldowns");
            Assert.Equal(cooldowns.GetArrayLength(), s.WeaponCount);
            var w2 = 0;
            foreach (var cooldown in cooldowns.EnumerateArray())
                // Recharges en double, tirées du RNG comme le TS : égales au bit près.
                Assert.Equal(cooldown.GetDouble(), s.Weapons[w2++].Cooldown);
        }

        var wells = root.GetProperty("wells");
        Assert.Equal(wells.GetArrayLength(), w.Wells.Count);
        var g = 0;
        foreach (var expected in wells.EnumerateArray())
        {
            ref var well = ref w.Wells[g++];
            Assert.Equal(expected.GetProperty("id").GetInt32(), well.Id);
            Assert.Equal(expected.GetProperty("name").GetString(), w.WellName(well));
            AssertVec(expected.GetProperty("pos"), well.Pos);
            Assert.Equal(expected.GetProperty("core").GetSingle(), well.Core);
            Assert.Equal(expected.GetProperty("pull").GetSingle(), well.Pull);
            Assert.Equal(expected.GetProperty("reach").GetSingle(), well.Reach);
        }

        Assert.Equal(root.GetProperty("depotId").GetInt32(), w.DepotId);
        // Le RNG a été tiré exactement autant de fois, dans le même ordre : la valeur suivante est identique.
        Assert.Equal(root.GetProperty("rngNext").GetDouble(), w.Rng.NextDouble());
    }

    static void AssertVec(JsonElement expected, Brutal.Numerics.float3 actual)
    {
        Assert.Equal((float)expected[0].GetDouble(), actual.X);
        Assert.Equal((float)expected[1].GetDouble(), actual.Y);
        Assert.Equal((float)expected[2].GetDouble(), actual.Z);
    }
}

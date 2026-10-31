using Brutal.Numerics;

namespace OrionumFleet.Sim.Tests;

public class StorageTests
{
    [Fact]
    public void Spatial_grid_finds_exactly_what_brute_force_finds_in_index_order()
    {
        var rng = new Rng(9);
        var points = new float3[3000];
        for (var i = 0; i < points.Length; i++)
            points[i] = new float3(rng.Range(-3000, 3000), rng.Range(-400, 400), rng.Range(-3000, 3000));

        var grid = new SpatialGrid(cellSize: 250);
        grid.Build(points);
        var results = Array.Empty<int>();

        foreach (var radius in (ReadOnlySpan<float>)[10, 120, 400, 1500, 9000])
        {
            for (var q = 0; q < 50; q++)
            {
                var center = new float3(rng.Range(-3200, 3200), rng.Range(-500, 500), rng.Range(-3200, 3200));
                var found = grid.QuerySphere(center, radius, ref results);

                var expected = new List<int>();
                for (var i = 0; i < points.Length; i++)
                    if (Vec.DistSq(points[i], center) <= radius * radius)
                        expected.Add(i);

                Assert.Equal(expected, results.AsSpan(0, found).ToArray());
            }
        }
    }

    [Fact]
    public void Id_map_resolves_many_ids_and_survives_growth_and_cloning()
    {
        var map = new IdMap(4);
        for (var id = 1; id <= 20_000; id++)
            map.Set(id, new EntityRef(EntityKind.Projectile, id * 3));

        var copy = map.Clone();
        map.Set(5, new EntityRef(EntityKind.Ship, 0));

        for (var id = 1; id <= 20_000; id++)
            Assert.Equal(new EntityRef(EntityKind.Projectile, id * 3), copy.Get(id));
        Assert.Equal(new EntityRef(EntityKind.Ship, 0), map.Get(5));
        Assert.Equal(EntityRef.None, map.Get(20_001));
        Assert.Equal(EntityRef.None, map.Get(0));
    }

    [Fact]
    public void Order_queue_keeps_order_and_caps_its_size()
    {
        var queue = new OrderQueue();
        for (var i = 0; i < OrderQueue.Capacity + 3; i++)
            queue.Push(new Order(OrderType.Move) { TargetId = i });

        Assert.Equal(OrderQueue.Capacity, queue.Count);
        queue.RemoveAt(0);
        Assert.Equal(1, queue[0].TargetId);
        Assert.Equal(OrderQueue.Capacity - 1, queue.Count);

        queue.Set(Order.Hold);
        Assert.Equal(1, queue.Count);
        Assert.Equal(OrderType.Hold, queue[0].Type);
        Assert.Null(queue[0].Subsystem);
    }
}

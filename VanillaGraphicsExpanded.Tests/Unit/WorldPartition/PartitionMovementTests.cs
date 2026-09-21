using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.WorldPartition;

/// <summary>Spatial changes exercise ownership, pressure, and three-dimensional incremental coverage.</summary>
public sealed class PartitionMovementTests
{
    #region Spatial membership
    /// <summary>Incremental three-dimensional slab updates match an independent brute-force overlap oracle.</summary>
    [Fact]
    public void ThreeDimensionalMovementMatchesIntersectionOracle()
    {
        var c = new PartitionCoordinator(new(1000, 1000, 1000, 1000, 10000));
        var p = new PartitionProviderFixture();
        long id = c.Register("p", "w", new(new(2, 3, 4)), new(0, 0, 0), new(1000, 1000, 1000, 1000, 10000), p);
        PartitionBounds[] movements = [new(new(-3,-4,-5),new(3,4,5)), new(new(-1,-2,-3),new(5,6,7)),
            new(new(6,6,8),new(7,7,9)), new(new(0,0,0),new(0,1,1)), new(new(-2,-3,-4),new(2,3,4))];
        for (int i = 0; i < movements.Length; i++)
        {
            PartitionBounds b = movements[i];
            c.SetSource(new(1,id,"w",b.Min,b)); c.Pump(i);
            var expected = new HashSet<PartitionCoordinate>();
            if (b.Min.X != b.Max.X)
                for (int z=-4;z<=4;z++) for(int y=-4;y<=4;y++) for(int x=-4;x<=4;x++)
                    if (x*2 < b.Max.X && (x+1)*2 > b.Min.X && y*3 < b.Max.Y && (y+1)*3 > b.Min.Y && z*4 < b.Max.Z && (z+1)*4 > b.Min.Z)
                        expected.Add(new(x,y,z));
            Assert.True(expected.SetEquals(c.Cells(id).Select(cell => cell.Key.Coordinate)));
            p.CompleteAll();
        }
    }

    /// <summary>A teleport cancels departed work while disjoint sources still preserve their own cells.</summary>
    [Fact]
    public void TeleportCancelsOnlyDepartedSourceCoverage()
    {
        var c = new PartitionCoordinator(new(100,100,100,100,1000));
        var p = new PartitionProviderFixture();
        long id = c.Register("p","w",new(new(16,16,16)),new(0,0,0),new(100,100,100,100,1000),p);
        c.SetSource(new(1,id,"w",new(),new(new(),new(1,1,1))));
        c.SetSource(new(2,id,"w",new(160,0,0),new(new(160,0,0),new(161,1,1)))); c.Pump(0);
        var old = p.Pending.Single(w=>w.Request.Key.Coordinate.X==0).Request;
        c.SetSource(new(1,id,"w",new(-160,0,0),new(new(-160,0,0),new(-159,1,1)))); c.Pump(1);
        Assert.True(old.Cancellation.IsCancellationRequested);
        Assert.Equal(new long[]{-10,10},c.Cells(id).Select(x=>x.Key.Coordinate.X).Order());
        p.CompleteAll();c.Pump(2);
        Assert.Equal(2,p.Publications.Count);
        Assert.DoesNotContain(p.Publications,x=>x.Request==old);
    }
    #endregion

    #region Residency pressure and participation
    /// <summary>Required work evicts speculative storage under both shared and local limits.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RequiredCellsEvictPrefetchedStorage(bool sharedPressure)
    {
        var c = new PartitionCoordinator(new(sharedPressure?2:100,100,100,100,1000));
        var p = new PartitionProviderFixture { AutoComplete=true };
        long id = c.Register("p","w",new(new(16,16,16)),new(1,1,0),new(2,100,100,100,1000),p);
        c.SetSource(new(1,id,"w",new(),new(new(1,1,1),new(16,2,2))));c.Pump(0);
        Assert.Equal(2,p.Visible.Count);
        c.SetSource(new(2,id,"w",new(160,1,1),new(new(160,1,1),new(161,2,2))));c.Pump(1);
        Assert.All(c.Cells(id).Where(x=>x.Desired==PartitionResidency.Active),x=>Assert.True(x.Ready));
        Assert.Equal(2,p.Visible.Count);
        Assert.DoesNotContain(p.Visible,x=>x.Coordinate.X==1);
    }

    /// <summary>Dirty re-publication preserves participation until an explicit deactivation acknowledgement.</summary>
    [Fact]
    public void DirtyActiveCellDemotionIsAcknowledged()
    {
        var c = new PartitionCoordinator(new(100,100,100,100,1000));
        var p = new PartitionProviderFixture { AutoComplete=true };
        long id = c.Register("p","w",new(new(16,16,16)),new(1,1,0),new(100,100,100,100,1000),p);
        c.SetSource(new(1,id,"w",new(),new(new(15,1,1),new(17,2,2))));c.Pump(0);
        var key = c.Cells(id).Single(x=>x.Key.Coordinate.X==1).Key;
        c.Dirty(key);p.Activations.Clear();
        c.SetSource(new(1,id,"w",new(),new(new(1,1,1),new(16,2,2))));c.Pump(1);
        Assert.Contains((key,false),p.Activations);
        Assert.Equal(PartitionResidency.Loaded,c.Cells(id).Single(x=>x.Key==key).Actual);
    }
    #endregion
}

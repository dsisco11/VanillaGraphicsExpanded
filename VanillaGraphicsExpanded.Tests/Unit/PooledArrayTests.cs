using System.Buffers;
using VanillaGraphicsExpanded.Collections;

namespace VanillaGraphicsExpanded.Tests.Unit;

/// <summary>Verifies pooled ownership independently of shared-pool bucket sizing.</summary>
public sealed class PooledArrayTests
{
    #region Ownership
    /// <summary>The owner exposes only requested elements and returns its original storage once.</summary>
    [Theory]
    [InlineData(0)] [InlineData(3)]
    public void LogicalSliceAndSingleReturn(int length)
    {
        var pool=new RecordingPool<int>();
        var owner=PooledArray<int>.Rent(length,pool);
        Assert.Equal(length,owner.Length);
        Assert.Equal(length,owner.Memory.Length);
        Assert.Equal(length,owner.Span.Length);
        owner.Span.Fill(42);
        for(int i=0;i<length;i++) Assert.Equal(42,pool.Storage[i]);
        Assert.Equal(0,pool.Storage[length]);
        owner.Dispose();owner.Dispose();
        Assert.Equal(1,pool.Returns);
        Assert.Same(pool.Storage,pool.Returned);
        Assert.False(pool.Clear);
        Assert.Throws<ObjectDisposedException>(()=>{_ = owner.Memory;});
        Assert.Throws<ObjectDisposedException>(()=>{_ = owner.Span.Length;});
    }

    /// <summary>Reference-containing elements request clearing and negative lengths never rent.</summary>
    [Fact]
    public void ReferenceClearingAndNegativeLength()
    {
        var pool=new RecordingPool<string>();
        Assert.Throws<ArgumentOutOfRangeException>(()=>PooledArray<string>.Rent(-1,pool));
        Assert.Equal(0,pool.Rents);
        using(var owner=PooledArray<string>.Rent(1,pool)) owner.Span[0]="owned";
        Assert.True(pool.Clear);
        Assert.Null(pool.Storage[0]);
    }
    #endregion

    #region Pool observation
    /// <summary>Provides excess capacity and records exact return semantics.</summary>
    private sealed class RecordingPool<T> : ArrayPool<T>
    {
        public T[] Storage { get; }=new T[8];
        public T[]? Returned { get; private set; }
        public int Rents { get; private set; }
        public int Returns { get; private set; }
        public bool Clear { get; private set; }
        /// <summary>Returns a deliberately oversized array.</summary>
        public override T[] Rent(int minimumLength) { Rents++;return Storage; }
        /// <summary>Records and implements the requested clearing behavior.</summary>
        public override void Return(T[] array,bool clearArray=false)
        {
            Returned=array;Returns++;Clear=clearArray;
            if(clearArray) Array.Clear(array);
        }
    }
    #endregion
}

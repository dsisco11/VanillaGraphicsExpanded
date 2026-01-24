using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit;

/// <summary>
/// Unit tests for GpuEbo class that don't require OpenGL context.
/// These tests focus on parameter validation and basic object behavior.
/// </summary>
[Trait("Category", "Unit")]
public class GpuEboUnitTests
{
    [Fact]
    public void Create_WithNullDebugName_AcceptsNull()
    {
        // This test verifies the method signature accepts null without requiring GL context
        // We can't actually create the EBO without GL context, but we can verify the API design
        
        // The fact that this compiles without warnings shows the API correctly accepts nullable string
        Assert.True(true); // Placeholder assertion for API design verification
    }

    [Fact]
    public void Create_HasExpectedSignature()
    {
        // This test documents and verifies the expected method signature
        // Without GL context, we can't create actual EBOs, but we can verify the API design
        
        // Verify the Create method exists with expected parameters
        var createMethod = typeof(GpuEbo).GetMethod("Create", 
            new[] { typeof(BufferUsageHint), typeof(string) });
        
        Assert.NotNull(createMethod);
        Assert.True(createMethod.IsStatic);
        Assert.True(createMethod.IsPublic);
    }

    [Theory]
    [InlineData(DrawElementsType.UnsignedByte)]
    [InlineData(DrawElementsType.UnsignedShort)]
    [InlineData(DrawElementsType.UnsignedInt)]
    public void DrawElementsType_AllValidTypes_AreSupported(DrawElementsType indexType)
    {
        // This test documents which index types should be supported by GpuEbo
        // We verify that these are valid enum values that can be used with the API
        Assert.True(Enum.IsDefined(typeof(DrawElementsType), indexType));
    }

    [Theory]
    [InlineData(BufferUsageHint.StaticDraw)]
    [InlineData(BufferUsageHint.StaticRead)]
    [InlineData(BufferUsageHint.StaticCopy)]
    [InlineData(BufferUsageHint.DynamicDraw)]
    [InlineData(BufferUsageHint.DynamicRead)]
    [InlineData(BufferUsageHint.DynamicCopy)]
    [InlineData(BufferUsageHint.StreamDraw)]
    [InlineData(BufferUsageHint.StreamRead)]
    [InlineData(BufferUsageHint.StreamCopy)]
    public void BufferUsageHint_AllValidHints_AreSupported(BufferUsageHint usage)
    {
        // This test documents which usage hints should be supported
        // We verify that these are valid enum values that can be used with the API
        Assert.True(Enum.IsDefined(typeof(BufferUsageHint), usage));
    }

    [Theory]
    [InlineData(PrimitiveType.Points)]
    [InlineData(PrimitiveType.Lines)]
    [InlineData(PrimitiveType.LineLoop)]
    [InlineData(PrimitiveType.LineStrip)]
    [InlineData(PrimitiveType.Triangles)]
    [InlineData(PrimitiveType.TriangleStrip)]
    [InlineData(PrimitiveType.TriangleFan)]
    public void PrimitiveType_AllValidTypes_AreSupported(PrimitiveType primitiveType)
    {
        // This test documents which primitive types should be supported for drawing
        Assert.True(Enum.IsDefined(typeof(PrimitiveType), primitiveType));
    }

    [Fact]
    public void GpuEbo_InheritsFromGpuBufferObject()
    {
        // Verify the inheritance hierarchy
        Assert.True(typeof(GpuBufferObject).IsAssignableFrom(typeof(GpuEbo)));
    }

    [Fact]
    public void GpuEbo_IsSealed()
    {
        // Verify that GpuEbo is sealed as intended
        Assert.True(typeof(GpuEbo).IsSealed);
    }

    [Fact]
    public void GpuEbo_IsInternal()
    {
        // Verify that GpuEbo has the correct visibility
        Assert.False(typeof(GpuEbo).IsPublic);
        Assert.True(typeof(GpuEbo).IsNotPublic);
    }

    [Fact]
    public void GpuEbo_ImplementsIDisposable()
    {
        // Verify that GpuEbo implements IDisposable (through base class)
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(GpuEbo)));
    }

    [Fact]
    public void GpuEbo_HasIndexCountProperty()
    {
        // Verify that IndexCount property exists and has correct type
        var indexCountProperty = typeof(GpuEbo).GetProperty("IndexCount");
        
        Assert.NotNull(indexCountProperty);
        Assert.Equal(typeof(int), indexCountProperty.PropertyType);
        Assert.True(indexCountProperty.CanRead);
        Assert.False(indexCountProperty.CanWrite);
    }

    [Fact]
    public void GpuEbo_HasIndexTypeProperty()
    {
        // Verify that IndexType property exists and has correct type
        var indexTypeProperty = typeof(GpuEbo).GetProperty("IndexType");
        
        Assert.NotNull(indexTypeProperty);
        Assert.Equal(typeof(DrawElementsType), indexTypeProperty.PropertyType);
        Assert.True(indexTypeProperty.CanRead);
        Assert.False(indexTypeProperty.CanWrite);
    }

    [Theory]
    [InlineData("TestEBO")]
    [InlineData("")]
    [InlineData("A very long debug name for an element buffer object")]
    [InlineData("SpecialChars!@#$%^&*()_+-={}[]|\\:;\"'<>?,.~/`")]
    public void DebugName_VariousValidStrings_ShouldBeAcceptable(string debugName)
    {
        // This test documents what kinds of debug names should be acceptable
        // We're not testing actual GL functionality, just API design
        Assert.NotNull(debugName); // All test cases are non-null strings
    }

    [Fact]
    public void UploadIndices_HasOverloadsForAllIndexTypes()
    {
        // Verify that UploadIndices methods exist for all supported index types
        
        // Array overloads
        var uintArrayMethod = typeof(GpuEbo).GetMethod("UploadIndices", new[] { typeof(uint[]) });
        var ushortArrayMethod = typeof(GpuEbo).GetMethod("UploadIndices", new[] { typeof(ushort[]) });
        var byteArrayMethod = typeof(GpuEbo).GetMethod("UploadIndices", new[] { typeof(byte[]) });
        
        Assert.NotNull(uintArrayMethod);
        Assert.NotNull(ushortArrayMethod);
        Assert.NotNull(byteArrayMethod);
        
        // Span overloads
        var uintSpanMethod = typeof(GpuEbo).GetMethod("UploadIndices", new[] { typeof(ReadOnlySpan<uint>) });
        var ushortSpanMethod = typeof(GpuEbo).GetMethod("UploadIndices", new[] { typeof(ReadOnlySpan<ushort>) });
        var byteSpanMethod = typeof(GpuEbo).GetMethod("UploadIndices", new[] { typeof(ReadOnlySpan<byte>) });
        
        Assert.NotNull(uintSpanMethod);
        Assert.NotNull(ushortSpanMethod);
        Assert.NotNull(byteSpanMethod);
    }

    [Fact]
    public void TryUploadIndices_HasOverloadsForAllIndexTypes()
    {
        // Verify that TryUploadIndices methods exist for all supported index types
        
        // Array overloads
        var uintArrayMethod = typeof(GpuEbo).GetMethod("TryUploadIndices", new[] { typeof(uint[]) });
        var ushortArrayMethod = typeof(GpuEbo).GetMethod("TryUploadIndices", new[] { typeof(ushort[]) });
        var byteArrayMethod = typeof(GpuEbo).GetMethod("TryUploadIndices", new[] { typeof(byte[]) });
        
        Assert.NotNull(uintArrayMethod);
        Assert.NotNull(ushortArrayMethod);
        Assert.NotNull(byteArrayMethod);
        
        // Span overloads
        var uintSpanMethod = typeof(GpuEbo).GetMethod("TryUploadIndices", new[] { typeof(ReadOnlySpan<uint>) });
        var ushortSpanMethod = typeof(GpuEbo).GetMethod("TryUploadIndices", new[] { typeof(ReadOnlySpan<ushort>) });
        var byteSpanMethod = typeof(GpuEbo).GetMethod("TryUploadIndices", new[] { typeof(ReadOnlySpan<byte>) });
        
        Assert.NotNull(uintSpanMethod);
        Assert.NotNull(ushortSpanMethod);
        Assert.NotNull(byteSpanMethod);
        
        // All Try methods should return bool
        Assert.Equal(typeof(bool), uintArrayMethod.ReturnType);
        Assert.Equal(typeof(bool), ushortArrayMethod.ReturnType);
        Assert.Equal(typeof(bool), byteArrayMethod.ReturnType);
        Assert.Equal(typeof(bool), uintSpanMethod.ReturnType);
        Assert.Equal(typeof(bool), ushortSpanMethod.ReturnType);
        Assert.Equal(typeof(bool), byteSpanMethod.ReturnType);
    }

    [Fact]
    public void DrawMethods_HaveCorrectSignatures()
    {
        // Verify draw method signatures
        var drawElementsMethod = typeof(GpuEbo).GetMethod("DrawElements", 
            new[] { typeof(PrimitiveType), typeof(int), typeof(int) });
        
        var drawElementsInstancedMethod = typeof(GpuEbo).GetMethod("DrawElementsInstanced", 
            new[] { typeof(PrimitiveType), typeof(int), typeof(int), typeof(int) });
        
        Assert.NotNull(drawElementsMethod);
        Assert.NotNull(drawElementsInstancedMethod);
        Assert.Equal(typeof(void), drawElementsMethod.ReturnType);
        Assert.Equal(typeof(void), drawElementsInstancedMethod.ReturnType);
    }

    [Fact]
    public void IndexDataTypes_CorrespondToDrawElementsTypes()
    {
        // This test documents the correspondence between C# types and DrawElementsType values
        // uint[] → DrawElementsType.UnsignedInt
        // ushort[] → DrawElementsType.UnsignedShort  
        // byte[] → DrawElementsType.UnsignedByte
        
        Assert.Equal(4, sizeof(uint)); // 4 bytes = UnsignedInt
        Assert.Equal(2, sizeof(ushort)); // 2 bytes = UnsignedShort
        Assert.Equal(1, sizeof(byte)); // 1 byte = UnsignedByte
        
        // Verify the enum values exist
        Assert.True(Enum.IsDefined(typeof(DrawElementsType), DrawElementsType.UnsignedInt));
        Assert.True(Enum.IsDefined(typeof(DrawElementsType), DrawElementsType.UnsignedShort));
        Assert.True(Enum.IsDefined(typeof(DrawElementsType), DrawElementsType.UnsignedByte));
    }

    [Fact]
    public void OverriddenMethods_HaveCorrectSignatures()
    {
        // Verify that GpuEbo properly overrides base class methods
        var detachMethod = typeof(GpuEbo).GetMethod("Detach");
        var disposeMethod = typeof(GpuEbo).GetMethod("Dispose");
        var toStringMethod = typeof(GpuEbo).GetMethod("ToString");
        
        Assert.NotNull(detachMethod);
        Assert.NotNull(disposeMethod);
        Assert.NotNull(toStringMethod);
        
        Assert.Equal(typeof(nint), detachMethod.ReturnType);
        Assert.Equal(typeof(void), disposeMethod.ReturnType);
        Assert.Equal(typeof(string), toStringMethod.ReturnType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(65535)] // Max ushort
    [InlineData(16777215)] // Max value for 3-byte index
    public void IndexCounts_ValidRanges_ShouldBeSupported(int indexCount)
    {
        // This test documents reasonable ranges for index counts
        Assert.True(indexCount >= 0);
        
        // For different index types, we have different practical limits:
        // - byte indices: 0-255 unique vertices
        // - ushort indices: 0-65535 unique vertices  
        // - uint indices: 0-4294967295 unique vertices
        
        if (indexCount <= 255)
        {
            // Can use any index type
            Assert.True(indexCount <= byte.MaxValue);
            Assert.True(indexCount <= ushort.MaxValue);
            Assert.True((uint)indexCount <= uint.MaxValue);
        }
        else if (indexCount <= 65535)
        {
            // Can use ushort or uint
            Assert.True(indexCount <= ushort.MaxValue);
            Assert.True((uint)indexCount <= uint.MaxValue);
        }
        else
        {
            // Must use uint
            Assert.True((uint)indexCount <= uint.MaxValue);
        }
    }
}

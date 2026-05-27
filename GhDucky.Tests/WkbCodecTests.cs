using System;
using System.Buffers.Binary;
using GhDucky.Utils;
using Rhino.Geometry;
using Xunit;

namespace GhDucky.Tests;

/// <summary>
/// Format-level checks for <see cref="WkbCodec"/>. We exercise only the encode
/// path with geometry primitives that are pure-managed (Point3d, Line,
/// Polyline) so the suite runs outside a Rhino host. The Decode path is not
/// covered here because it constructs Rhino types whose constructors p/invoke
/// into native Rhino code and require a running Rhino runtime.
/// </summary>
public class WkbCodecTests
{
    private const byte LittleEndian = 0x01;
    private const uint TypePointZ      = 1 + 1000;
    private const uint TypeLineStringZ = 2 + 1000;

    [Fact]
    public void EncodePoint_HasCorrectHeader()
    {
        var bytes = WkbCodec.Encode(new Point3d(1.5, -2.5, 3.5), tolerance: 0.0);

        // Header: 1 byte endianness + 4 bytes type + 3 doubles = 29 bytes.
        Assert.Equal(1 + 4 + (3 * 8), bytes.Length);

        Assert.Equal(LittleEndian, bytes[0]);
        Assert.Equal(TypePointZ, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(1, 4)));

        Assert.Equal(1.5, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(5, 8)));
        Assert.Equal(-2.5, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(13, 8)));
        Assert.Equal(3.5, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(21, 8)));
    }

    [Fact]
    public void EncodeLine_ProducesTwoPointLineString()
    {
        var line = new Line(new Point3d(0, 0, 0), new Point3d(10, 20, 30));
        var bytes = WkbCodec.Encode(line, tolerance: 0.0);

        ReadLineStringHeader(bytes, out var numPoints, out var coordStart);
        Assert.Equal(2u, numPoints);

        var pts = ReadCoords(bytes, coordStart, (int)numPoints);
        Assert.Equal((0, 0, 0), pts[0]);
        Assert.Equal((10, 20, 30), pts[1]);
    }

    [Fact]
    public void EncodePolyline_PreservesAllVertices()
    {
        var pl = new Polyline
        {
            new Point3d(0, 0, 0),
            new Point3d(1, 0, 0),
            new Point3d(1, 1, 0),
            new Point3d(0, 1, 0),
        };

        var bytes = WkbCodec.Encode(pl, tolerance: 0.0);

        ReadLineStringHeader(bytes, out var numPoints, out var coordStart);
        Assert.Equal(4u, numPoints);

        var pts = ReadCoords(bytes, coordStart, (int)numPoints);
        Assert.Equal((0, 0, 0), pts[0]);
        Assert.Equal((1, 0, 0), pts[1]);
        Assert.Equal((1, 1, 0), pts[2]);
        Assert.Equal((0, 1, 0), pts[3]);
    }

    [Fact]
    public void EncodePoint_NullGeometry_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => WkbCodec.Encode(null, tolerance: 0.0));
    }

    [Fact]
    public void EncodePoint_UnsupportedType_Throws()
    {
        // string is not a geometry that WkbCodec knows how to encode.
        Assert.Throws<NotSupportedException>(
            () => WkbCodec.Encode("not a geometry", tolerance: 0.0));
    }

    // ---------- helpers ----------

    private static void ReadLineStringHeader(byte[] bytes, out uint numPoints, out int coordStart)
    {
        Assert.Equal(LittleEndian, bytes[0]);
        Assert.Equal(TypeLineStringZ, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(1, 4)));
        numPoints = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(5, 4));
        coordStart = 9;

        // 1 + 4 + 4 header + 24 bytes per point
        Assert.Equal(coordStart + (int)numPoints * 24, bytes.Length);
    }

    private static (double, double, double)[] ReadCoords(byte[] bytes, int start, int count)
    {
        var result = new (double, double, double)[count];
        for (var i = 0; i < count; i++)
        {
            var offset = start + i * 24;
            var x = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(offset, 8));
            var y = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(offset + 8, 8));
            var z = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(offset + 16, 8));
            result[i] = (x, y, z);
        }
        return result;
    }
}


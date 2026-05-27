using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Rhino.Geometry;

namespace GhDucky.Utils
{
    /// Self-contained ISO-WKB encoder/decoder for the geometry types DuckDB
    /// spatial accepts. We write little-endian Z geometries; on read we honour
    /// whatever endianness/dimensionality the buffer declares.
    ///
    /// Supported encode -> WKB:
    ///   Point3d           -> POINT Z
    ///   LineCurve         -> LINESTRING Z
    ///   PolylineCurve     -> LINESTRING Z
    ///   Polyline          -> LINESTRING Z
    ///   Curve (other)     -> LINESTRING Z (tessellated)
    ///   Mesh              -> MULTIPOLYGON Z (one polygon per face)
    ///   Brep              -> MULTIPOLYGON Z (auto-meshed)
    ///
    /// Supported WKB -> Rhino geometry:
    ///   POINT             -> Point
    ///   LINESTRING        -> PolylineCurve
    ///   POLYGON           -> Mesh (outer ring; holes ignored with a warning)
    ///   MULTIPOLYGON      -> Mesh (combined; holes ignored)
    ///   MULTILINESTRING   -> first linestring (warning issued)
    ///   MULTIPOINT        -> first point (warning issued)
    internal static class WkbCodec
    {
        private const uint TypePoint = 1;
        private const uint TypeLineString = 2;
        private const uint TypePolygon = 3;
        private const uint TypeMultiPoint = 4;
        private const uint TypeMultiLineString = 5;
        private const uint TypeMultiPolygon = 6;
        private const uint Z = 1000;

        private const int DefaultCurveSamples = 64;

        public static byte[] Encode(object geometry, double tolerance)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            WriteAny(w, geometry, tolerance);
            return ms.ToArray();
        }

        public static GeometryBase Decode(byte[] wkb, out string warning)
        {
            warning = null;
            if (wkb == null || wkb.Length < 5)
                throw new ArgumentException("WKB buffer too small.");
            using var ms = new MemoryStream(wkb);
            using var r = new BinaryReader(ms);
            return ReadGeometry(r, ref warning);
        }

        // -------- WRITE --------

        private static void WriteAny(BinaryWriter w, object geometry, double tolerance)
        {
            switch (geometry)
            {
                case null:
                    throw new ArgumentNullException(nameof(geometry));
                case Point3d p3:
                    WritePoint(w, p3);
                    return;
                case Point pt:
                    WritePoint(w, pt.Location);
                    return;
                case Line line:
                    WriteLineString(w, new[] { line.From, line.To });
                    return;
                case LineCurve lc:
                    WriteLineString(w, new[] { lc.PointAtStart, lc.PointAtEnd });
                    return;
                case Polyline pl:
                    WriteLineString(w, pl);
                    return;
                case PolylineCurve plc:
                    {
                        if (plc.TryGetPolyline(out var pl2))
                            WriteLineString(w, pl2);
                        else
                            WriteLineString(w, TessellateCurve(plc, tolerance));
                        return;
                    }
                case Curve crv:
                    WriteLineString(w, TessellateCurve(crv, tolerance));
                    return;
                case Mesh mesh:
                    WriteMultiPolygon(w, MeshToFaces(mesh));
                    return;
                case Brep brep:
                    WriteMultiPolygon(w, BrepToFaces(brep, tolerance));
                    return;
                case GeometryBase gb:
                    throw new NotSupportedException(
                        "Unsupported geometry type for WKB encoding: " + gb.ObjectType);
                default:
                    throw new NotSupportedException(
                        "Unsupported value for WKB encoding: " + geometry.GetType().Name);
            }
        }

        private static void WritePoint(BinaryWriter w, Point3d p)
        {
            w.Write((byte)1);                // little-endian
            w.Write(TypePoint + Z);
            w.Write(p.X);
            w.Write(p.Y);
            w.Write(p.Z);
        }

        private static void WriteLineString(BinaryWriter w, IList<Point3d> pts)
        {
            w.Write((byte)1);
            w.Write(TypeLineString + Z);
            w.Write((uint)pts.Count);
            foreach (var p in pts)
            {
                w.Write(p.X);
                w.Write(p.Y);
                w.Write(p.Z);
            }
        }

        private static void WritePolygonZ(BinaryWriter w, IList<Point3d> outerRing)
        {
            w.Write((byte)1);
            w.Write(TypePolygon + Z);
            w.Write((uint)1);             // one ring (no holes)
            WriteRing(w, outerRing);
        }

        private const double ClosureTolerance = 1e-12;

        private static void WriteRing(BinaryWriter w, IList<Point3d> ring)
        {
            // WKB rings must be explicitly closed: first == last.
            var needsClose = ring.Count == 0 ||
                             ring[0].DistanceTo(ring[ring.Count - 1]) > ClosureTolerance;
            var count = (uint)(ring.Count + (needsClose ? 1 : 0));
            w.Write(count);
            foreach (var p in ring)
            {
                w.Write(p.X); w.Write(p.Y); w.Write(p.Z);
            }
            if (needsClose && ring.Count > 0)
            {
                var p = ring[0];
                w.Write(p.X); w.Write(p.Y); w.Write(p.Z);
            }
        }

        private static void WriteMultiPolygon(BinaryWriter w, IReadOnlyList<Point3d[]> rings)
        {
            w.Write((byte)1);
            w.Write(TypeMultiPolygon + Z);
            w.Write((uint)rings.Count);
            foreach (var t in rings)
                WritePolygonZ(w, t);
        }

        // -------- READ --------

        private static GeometryBase ReadGeometry(BinaryReader r, ref string warning)
        {
            var littleEndian = r.ReadByte() == 1;
            var type = ReadUInt32(r, littleEndian);
            var baseType = type % 1000;
            var hasZ = (type / 1000) % 2 == 1;
            var hasM = (type / 2000) % 2 == 1;
            var coordDim = 2 + (hasZ ? 1 : 0) + (hasM ? 1 : 0);

            switch (baseType)
            {
                case TypePoint:
                    return new Point(ReadPoint(r, littleEndian, coordDim, hasZ));

                case TypeLineString:
                    {
                        var pts = ReadLineStringPoints(r, littleEndian, coordDim, hasZ);
                        var pl = new Polyline(pts);
                        return new PolylineCurve(pl);
                    }

                case TypePolygon:
                    {
                        var rings = ReadPolygonRings(r, littleEndian, coordDim, hasZ);
                        if (rings.Count > 1)
                            warning = AppendWarning(warning, "Polygon holes were ignored.");
                        var mesh = new Mesh();
                        if (rings.Count > 0)
                            AddRingAsFace(mesh, rings[0]);
                        return mesh;
                    }

                case TypeMultiPolygon:
                    {
                        var n = ReadUInt32(r, littleEndian);
                        var mesh = new Mesh();
                        var anyHoles = false;
                        for (var i = 0; i < n; i++)
                        {
                            var subLe = r.ReadByte() == 1;
                            var subType = ReadUInt32(r, subLe);
                            var subBase = subType % 1000;
                            if (subBase != TypePolygon)
                                throw new InvalidDataException(
                                    "MULTIPOLYGON contained non-polygon sub-geometry (type " + subType + ").");
                            var subZ = (subType / 1000) % 2 == 1;
                            var subM = (subType / 2000) % 2 == 1;
                            var subDim = 2 + (subZ ? 1 : 0) + (subM ? 1 : 0);
                            var rings = ReadPolygonRings(r, subLe, subDim, subZ);
                            if (rings.Count > 1) anyHoles = true;
                            if (rings.Count > 0)
                                AddRingAsFace(mesh, rings[0]);
                        }
                        if (anyHoles)
                            warning = AppendWarning(warning, "Polygon holes were ignored on one or more faces.");
                        return mesh;
                    }

                case TypeMultiPoint:
                    {
                        var n = ReadUInt32(r, littleEndian);
                        var points = new List<Point3d>((int)n);
                        for (var i = 0; i < n; i++)
                        {
                            var subLe = r.ReadByte() == 1;
                            var subType = ReadUInt32(r, subLe);
                            var subZ = (subType / 1000) % 2 == 1;
                            var subM = (subType / 2000) % 2 == 1;
                            var subDim = 2 + (subZ ? 1 : 0) + (subM ? 1 : 0);
                            points.Add(ReadPoint(r, subLe, subDim, subZ));
                        }

                        if (points.Count == 0)
                            return new Point(Point3d.Origin);
                        if (points.Count == 1)
                            return new Point(points[0]);

                        // Return a PointCloud so all points are preserved.
                        var cloud = new PointCloud();
                        foreach (var pt in points)
                            cloud.Add(pt);
                        return cloud;
                    }

                case TypeMultiLineString:
                    {
                        var n = ReadUInt32(r, littleEndian);
                        var polylines = new List<Polyline>((int)n);
                        for (var i = 0; i < n; i++)
                        {
                            bool subLe = r.ReadByte() == 1;
                            uint subType = ReadUInt32(r, subLe);
                            bool subZ = (subType / 1000) % 2 == 1;
                            bool subM = (subType / 2000) % 2 == 1;
                            int subDim = 2 + (subZ ? 1 : 0) + (subM ? 1 : 0);
                            var pts = ReadLineStringPoints(r, subLe, subDim, subZ);
                            if (pts.Count > 0)
                                polylines.Add(new Polyline(pts));
                        }

                        if (polylines.Count == 0)
                            return new PolylineCurve(new Polyline());
                        if (polylines.Count == 1)
                            return new PolylineCurve(polylines[0]);

                        // Combine all linestrings into a single polyline, preserving all vertices.
                        // A warning is issued because the gaps between disjoint segments are bridged.
                        warning = AppendWarning(warning,
                            $"MULTILINESTRING with {polylines.Count} parts was combined into a single polyline; gaps between segments are bridged.");
                        var combined = new Polyline();
                        foreach (var pl in polylines)
                            combined.AddRange(pl);
                        return new PolylineCurve(combined);
                    }

                default:
                    throw new NotSupportedException("Unsupported WKB type code: " + type);
            }
        }

        private static Point3d ReadPoint(BinaryReader r, bool littleEndian, int coordDim, bool hasZ)
        {
            var x = ReadDouble(r, littleEndian);
            var y = ReadDouble(r, littleEndian);
            double z = 0;
            if (coordDim >= 3)
            {
                var v = ReadDouble(r, littleEndian);
                if (hasZ) z = v;
            }
            if (coordDim == 4) ReadDouble(r, littleEndian); // skip M
            return new Point3d(x, y, z);
        }

        private static List<Point3d> ReadLineStringPoints(BinaryReader r, bool le, int dim, bool hasZ)
        {
            var n = ReadUInt32(r, le);
            var list = new List<Point3d>((int)n);
            for (var i = 0; i < n; i++)
                list.Add(ReadPoint(r, le, dim, hasZ));
            return list;
        }

        private static List<List<Point3d>> ReadPolygonRings(BinaryReader r, bool le, int dim, bool hasZ)
        {
            var ringCount = ReadUInt32(r, le);
            var rings = new List<List<Point3d>>((int)ringCount);
            for (var i = 0; i < ringCount; i++)
                rings.Add(ReadLineStringPoints(r, le, dim, hasZ));
            return rings;
        }

        private static uint ReadUInt32(BinaryReader r, bool littleEndian)
        {
            Span<byte> b = stackalloc byte[4];
            var read = r.Read(b);
            if (read != 4) throw new EndOfStreamException();
            return littleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(b)
                : BinaryPrimitives.ReadUInt32BigEndian(b);
        }

        private static double ReadDouble(BinaryReader r, bool littleEndian)
        {
            Span<byte> b = stackalloc byte[8];
            var read = r.Read(b);
            if (read != 8) throw new EndOfStreamException();
            return littleEndian
                ? BinaryPrimitives.ReadDoubleLittleEndian(b)
                : BinaryPrimitives.ReadDoubleBigEndian(b);
        }

        private static string AppendWarning(string acc, string msg) =>
            string.IsNullOrEmpty(acc) ? msg : acc + " " + msg;

        // -------- Helpers --------

        private static void AddRingAsFace(Mesh mesh, List<Point3d> ring)
        {
            // Drop the closing duplicate point if present.
            var n = ring.Count;
            if (n >= 2 && ring[0].DistanceTo(ring[n - 1]) <= ClosureTolerance) n--;
            if (n < 3) return;

            var baseIndex = mesh.Vertices.Count;
            for (var i = 0; i < n; i++)
                mesh.Vertices.Add(ring[i]);

            switch (n)
            {
                case 3:
                    mesh.Faces.AddFace(baseIndex, baseIndex + 1, baseIndex + 2);
                    break;
                case 4:
                    mesh.Faces.AddFace(baseIndex, baseIndex + 1, baseIndex + 2, baseIndex + 3);
                    break;
                default:
                {
                    // Fan-triangulate ngon from the first vertex.
                    for (var i = 1; i < n - 1; i++)
                        mesh.Faces.AddFace(baseIndex, baseIndex + i, baseIndex + i + 1);
                    break;
                }
            }
        }

        private static IReadOnlyList<Point3d[]> MeshToFaces(Mesh mesh)
        {
            var faces = new List<Point3d[]>(mesh.Faces.Count);
            var vertices = mesh.Vertices;
            foreach (var f in mesh.Faces)
            {
                if (f.IsTriangle)
                {
                    faces.Add(new[]
                    {
                        (Point3d)vertices[f.A],
                        (Point3d)vertices[f.B],
                        (Point3d)vertices[f.C],
                    });
                }
                else
                {
                    faces.Add(new[]
                    {
                        (Point3d)vertices[f.A],
                        (Point3d)vertices[f.B],
                        (Point3d)vertices[f.C],
                        (Point3d)vertices[f.D],
                    });
                }
            }
            return faces;
        }

        private static IReadOnlyList<Point3d[]> BrepToFaces(Brep brep, double tolerance)
        {
            var mp = MeshingParameters.Default;
            if (tolerance > 0) mp.Tolerance = tolerance;
            var meshes = Mesh.CreateFromBrep(brep, mp) ?? Array.Empty<Mesh>();
            var all = new Mesh();
            foreach (var m in meshes)
                if (m != null) all.Append(m);
            return MeshToFaces(all);
        }

        private static Polyline TessellateCurve(Curve curve, double tolerance)
        {
            if (curve == null) return new Polyline();
            if (curve.TryGetPolyline(out var asPoly)) return asPoly;

            // Try Rhino's adaptive tessellation first; fall back to uniform sampling.
            if (tolerance > 0)
            {
                var pc = curve.ToPolyline(0, 0, 0.1, 0.0, 0.0, tolerance, 0.0, 0.0, true);
                if (pc != null && pc.TryGetPolyline(out var poly) && poly.Count >= 2)
                    return poly;
            }

            int samples = DefaultCurveSamples;
            var ts = curve.DivideByCount(samples, true);
            var pts = new Polyline();
            if (ts != null && ts.Length > 0)
            {
                foreach (var t in ts)
                    pts.Add(curve.PointAt(t));
            }
            else
            {
                pts.Add(curve.PointAtStart);
                pts.Add(curve.PointAtEnd);
            }
            return pts;
        }
    }
}

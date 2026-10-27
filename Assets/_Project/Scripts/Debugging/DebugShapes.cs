using System.Collections.Generic;
using Metroidvania.Player;
using UnityEngine;

namespace Metroidvania.Debugging
{
    /// <summary>
    /// Builds outline points for any Collider2D (plus the player's ground probe), shared by
    /// editor gizmos and the in-game collider visualizer. Outlines are in world space.
    /// </summary>
    public static class DebugShapes
    {
        private const int ArcSegments = 12;
        private const int CircleSegments = 24;

        private static readonly List<Vector2> PathBuffer = new List<Vector2>();
        private static readonly List<Vector2> ShapeVertices = new List<Vector2>();
        private static readonly PhysicsShapeGroup2D ShapeGroup = new PhysicsShapeGroup2D();
        private static readonly List<Vector3> AlignSource = new List<Vector3>();

        /// <summary>
        /// Appends the collider's outlines to the buffer. With pixelsPerUnit > 0 they are
        /// snapped to the pixel grid and inset half a pixel into the solid, so 1 px lines
        /// land on pixel centers instead of straddling two pixels and breaking up.
        /// </summary>
        public static void Collect(Collider2D collider, OutlineBuffer buffer, float pixelsPerUnit = 0f)
        {
            var first = buffer.Count;
            var t = collider.transform;

            switch (collider)
            {
                case CompositeCollider2D composite:
                    for (var p = 0; p < composite.pathCount; p++)
                    {
                        composite.GetPath(p, PathBuffer);
                        AddPath(buffer, t, Vector2.zero, composite.geometryType == CompositeCollider2D.GeometryType.Polygons);
                    }
                    break;

                case PolygonCollider2D polygon:
                    for (var p = 0; p < polygon.pathCount; p++)
                    {
                        polygon.GetPath(p, PathBuffer);
                        AddPath(buffer, t, polygon.offset, true);
                    }
                    break;

                case BoxCollider2D box:
                {
                    var loop = buffer.Add(true);
                    var half = box.size * 0.5f;
                    loop.Add(t.TransformPoint(box.offset + new Vector2(-half.x, -half.y)));
                    loop.Add(t.TransformPoint(box.offset + new Vector2(-half.x, half.y)));
                    loop.Add(t.TransformPoint(box.offset + new Vector2(half.x, half.y)));
                    loop.Add(t.TransformPoint(box.offset + new Vector2(half.x, -half.y)));
                    break;
                }

                case CapsuleCollider2D capsule:
                {
                    var vertical = capsule.direction == CapsuleDirection2D.Vertical;
                    var radius = (vertical ? capsule.size.x : capsule.size.y) * 0.5f;
                    var half = Mathf.Max(0f, (vertical ? capsule.size.y : capsule.size.x) * 0.5f - radius);
                    var axis = vertical ? Vector2.up : Vector2.right;
                    var loop = buffer.Add(true);
                    AddCapsule(loop, capsule.offset - axis * half, capsule.offset + axis * half, radius);
                    for (var i = 0; i < loop.Count; i++)
                        loop[i] = t.TransformPoint(loop[i]);
                    break;
                }

                case CircleCollider2D circle:
                {
                    var scale = t.lossyScale;
                    var radius = circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                    AddCircle(buffer.Add(true), t.TransformPoint(circle.offset), radius);
                    break;
                }

                case EdgeCollider2D edge:
                    edge.GetPoints(PathBuffer);
                    AddPath(buffer, t, edge.offset, false);
                    break;

                default:
                    CollectShapes(collider, buffer);
                    break;
            }

            if (pixelsPerUnit > 0f)
                PixelAlign(buffer, first, pixelsPerUnit);
        }

        /// <summary>Area swept by the ground probe: the box plus its cast distance.</summary>
        public static void GroundProbe(PlayerController pc, OutlineBuffer buffer, float pixelsPerUnit = 0f)
        {
            var first = buffer.Count;
            pc.GetGroundProbe(out var origin, out var size, out var skin);
            var reach = pc.GroundProbeDistance + skin;
            var center = origin + Vector2.down * reach * 0.5f;
            var half = new Vector2(size.x, size.y + reach) * 0.5f;
            var loop = buffer.Add(true);
            loop.Add(center + new Vector2(-half.x, -half.y));
            loop.Add(center + new Vector2(-half.x, half.y));
            loop.Add(center + new Vector2(half.x, half.y));
            loop.Add(center + new Vector2(half.x, -half.y));
            if (pixelsPerUnit > 0f)
                PixelAlign(buffer, first, pixelsPerUnit);
        }

        public static void DrawGizmos(OutlineBuffer buffer)
        {
            for (var l = 0; l < buffer.Count; l++)
            {
                var loop = buffer[l];
                var segments = buffer.IsClosed(l) ? loop.Count : loop.Count - 1;
                for (var i = 0; i < segments; i++)
                    Gizmos.DrawLine(loop[i], loop[(i + 1) % loop.Count]);
            }
        }

        private static void AddPath(OutlineBuffer buffer, Transform t, Vector2 offset, bool closed)
        {
            var loop = buffer.Add(closed);
            foreach (var point in PathBuffer)
                loop.Add(t.TransformPoint(point + offset));
        }

        /// <summary>Fallback for colliders without a dedicated case (e.g. an unmerged TilemapCollider2D).</summary>
        private static void CollectShapes(Collider2D collider, OutlineBuffer buffer)
        {
            collider.GetShapes(ShapeGroup);
            // Shapes are expressed in the space of the attached body, if any.
            var body = collider.attachedRigidbody != null ? collider.attachedRigidbody.transform : null;
            for (var s = 0; s < ShapeGroup.shapeCount; s++)
            {
                var shape = ShapeGroup.GetShape(s);
                ShapeGroup.GetShapeVertices(s, ShapeVertices);
                for (var i = 0; i < ShapeVertices.Count; i++)
                    ShapeVertices[i] = body != null ? (Vector2)body.TransformPoint(ShapeVertices[i]) : ShapeVertices[i];

                switch (shape.shapeType)
                {
                    case PhysicsShapeType2D.Circle:
                        AddCircle(buffer.Add(true), ShapeVertices[0], shape.radius);
                        break;
                    case PhysicsShapeType2D.Capsule:
                        AddCapsule(buffer.Add(true), ShapeVertices[0], ShapeVertices[1], shape.radius);
                        break;
                    default:
                    {
                        var loop = buffer.Add(shape.shapeType == PhysicsShapeType2D.Polygon);
                        foreach (var v in ShapeVertices)
                            loop.Add(v);
                        break;
                    }
                }
            }
        }

        /// <summary>Capsule around segment a-b: half circle around b, then half circle around a.</summary>
        private static void AddCapsule(List<Vector3> loop, Vector2 a, Vector2 b, float radius)
        {
            var axis = b - a;
            if (axis.sqrMagnitude < 1e-8f)
            {
                AddCircle(loop, a, radius);
                return;
            }
            var start = Mathf.Atan2(axis.y, axis.x) - Mathf.PI * 0.5f;
            for (var end = 0; end < 2; end++)
            {
                var center = end == 0 ? b : a;
                for (var i = 0; i <= ArcSegments; i++)
                {
                    var angle = start + Mathf.PI * (end + (float)i / ArcSegments);
                    loop.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                }
            }
        }

        private static void AddCircle(List<Vector3> loop, Vector2 center, float radius)
        {
            for (var i = 0; i < CircleSegments; i++)
            {
                var angle = 2f * Mathf.PI * i / CircleSegments;
                loop.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        }

        /// <summary>
        /// Snaps the loops added since 'first' to pixel corners and insets closed loops half a
        /// pixel into the solid. The solid side is taken from the winding of the largest loop,
        /// so holes (opposite winding) are inset into the surrounding solid as well.
        /// </summary>
        private static void PixelAlign(OutlineBuffer buffer, int first, float ppu)
        {
            var largestArea = 0f;
            for (var l = first; l < buffer.Count; l++)
            {
                var loop = buffer[l];
                for (var i = 0; i < loop.Count; i++)
                    loop[i] = new Vector3(Mathf.Round(loop[i].x * ppu), Mathf.Round(loop[i].y * ppu)) / ppu;
                RemoveDuplicates(loop, buffer.IsClosed(l));

                var area = SignedArea(loop);
                if (Mathf.Abs(area) > Mathf.Abs(largestArea))
                    largestArea = area;
            }

            var halfPixel = 0.5f / ppu;
            var side = largestArea >= 0f ? 1f : -1f;
            for (var l = first; l < buffer.Count; l++)
            {
                var loop = buffer[l];
                if (!buffer.IsClosed(l) || loop.Count < 3)
                {
                    for (var i = 0; i < loop.Count; i++)
                        loop[i] += new Vector3(halfPixel, halfPixel);
                    continue;
                }

                AlignSource.Clear();
                AlignSource.AddRange(loop);
                var n = AlignSource.Count;
                for (var i = 0; i < n; i++)
                {
                    var prev = AlignSource[(i + n - 1) % n];
                    var cur = AlignSource[i];
                    var next = AlignSource[(i + 1) % n];
                    var n1 = Inward((Vector2)(cur - prev), side);
                    var n2 = Inward((Vector2)(next - cur), side);
                    // Miter offset keeps both adjacent edges exactly half a pixel inside.
                    var miter = Mathf.Max(0.2f, 1f + Vector2.Dot(n1, n2));
                    loop[i] = cur + (Vector3)((n1 + n2) / miter * halfPixel);
                }
            }
        }

        /// <summary>Left normal for counter-clockwise loops (side = 1), right normal otherwise.</summary>
        private static Vector2 Inward(Vector2 edge, float side)
        {
            var dir = edge.normalized;
            return new Vector2(-dir.y, dir.x) * side;
        }

        private static void RemoveDuplicates(List<Vector3> loop, bool closed)
        {
            for (var i = loop.Count - 1; i > 0; i--)
            {
                if (loop[i] == loop[i - 1])
                    loop.RemoveAt(i);
            }
            if (closed && loop.Count > 1 && loop[0] == loop[loop.Count - 1])
                loop.RemoveAt(loop.Count - 1);
        }

        private static float SignedArea(List<Vector3> loop)
        {
            var area = 0f;
            for (var i = 0; i < loop.Count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }
    }

    /// <summary>Reusable list of outlines so drawing every frame does not allocate.</summary>
    public sealed class OutlineBuffer
    {
        private readonly List<List<Vector3>> loops = new List<List<Vector3>>();
        private readonly List<bool> closed = new List<bool>();

        public int Count { get; private set; }

        public List<Vector3> this[int index] => loops[index];

        public bool IsClosed(int index) => closed[index];

        public void Clear() => Count = 0;

        public List<Vector3> Add(bool isClosed)
        {
            if (Count == loops.Count)
            {
                loops.Add(new List<Vector3>());
                closed.Add(isClosed);
            }
            var loop = loops[Count];
            loop.Clear();
            closed[Count] = isClosed;
            Count++;
            return loop;
        }
    }
}

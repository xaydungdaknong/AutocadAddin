using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace DwgToDxf.Printing
{
    /// <summary>Một khung in: đối tượng gốc và hình chữ nhật bao (WCS, song song trục).</summary>
    public class Frame
    {
        public ObjectId Id { get; set; }
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }

        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public double CenterX => (MinX + MaxX) / 2;
        public double CenterY => (MinY + MaxY) / 2;
        public double Area => Width * Height;

        public bool Contains(double x, double y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

        public bool Contains(Frame other, double tol) =>
            other.MinX >= MinX - tol && other.MaxX <= MaxX + tol &&
            other.MinY >= MinY - tol && other.MaxY <= MaxY + tol;
    }

    /// <summary>
    /// Tìm các khung in giống khung mẫu: block reference cùng tên block (kể cả dynamic block),
    /// hoặc polyline kín hình chữ nhật nằm trên cùng layer với khung mẫu.
    /// </summary>
    public static class FrameFinder
    {
        /// <summary>Kiểm tra khung mẫu hợp lệ. Trả về null nếu được, ngược lại là lý do.</summary>
        public static string CheckSample(Transaction tr, ObjectId sampleId)
        {
            var ent = (Entity)tr.GetObject(sampleId, OpenMode.ForRead);
            if (ent is BlockReference) return null;
            if (ent is Polyline pl) return IsRectangle(pl) ? null : "Polyline mẫu không phải hình chữ nhật kín song song trục.";
            return "Khung mẫu phải là block hoặc polyline kín hình chữ nhật.";
        }

        /// <param name="candidates">Đối tượng để xét. Null: mọi đối tượng trong không gian hiện hành.</param>
        public static List<Frame> Find(Transaction tr, Database db, ObjectId sampleId, IEnumerable<ObjectId> candidates)
        {
            var sample = (Entity)tr.GetObject(sampleId, OpenMode.ForRead);
            var blockName = sample is BlockReference sbr ? EffectiveName(tr, sbr) : null;
            var layer = sample.Layer;

            if (candidates == null)
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                candidates = space.Cast<ObjectId>();
            }

            var frames = new List<Frame>();
            foreach (var id in candidates)
            {
                if (id.IsErased) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || !ent.Visible) continue;

                Frame f = null;
                if (blockName != null)
                {
                    if (ent is BlockReference br && string.Equals(EffectiveName(tr, br), blockName, StringComparison.OrdinalIgnoreCase))
                        f = FromBlock(tr, br);
                }
                else if (ent is Polyline pl && string.Equals(pl.Layer, layer, StringComparison.OrdinalIgnoreCase) && IsRectangle(pl))
                {
                    f = FromExtents(pl.Id, pl.GeometricExtents);
                }

                if (f != null && f.Width > 0 && f.Height > 0) frames.Add(f);
            }

            return RemoveNested(frames);
        }

        /// <summary>Bỏ các khung nằm trọn trong khung khác (ví dụ ô chữ nhật trong khung tên).</summary>
        private static List<Frame> RemoveNested(List<Frame> frames)
        {
            var byArea = frames.OrderByDescending(f => f.Area).ToList();
            var kept = new List<Frame>();
            foreach (var f in byArea)
            {
                var tol = Math.Max(f.Width, f.Height) * 1e-6;
                if (!kept.Any(k => k.Contains(f, tol))) kept.Add(f);
            }
            return kept;
        }

        public static string EffectiveName(Transaction tr, BlockReference br)
        {
            var btrId = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
            return ((BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead)).Name;
        }

        private static Frame FromBlock(Transaction tr, BlockReference br)
        {
            // Lấy bao của hình học trong block (không tính attribute) rồi biến đổi theo block reference.
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            var ext = new Extents3d();
            var any = false;
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || ent is AttributeDefinition || !ent.Visible) continue;
                try
                {
                    var e = ent.GeometricExtents;
                    ext.AddExtents(e);
                    any = true;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // Đối tượng không có extents (ví dụ text rỗng): bỏ qua.
                }
            }

            if (!any)
            {
                try { return FromExtents(br.Id, br.GeometricExtents); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
            }

            var x = br.BlockTransform;
            var corners = new[]
            {
                new Point3d(ext.MinPoint.X, ext.MinPoint.Y, 0),
                new Point3d(ext.MaxPoint.X, ext.MinPoint.Y, 0),
                new Point3d(ext.MaxPoint.X, ext.MaxPoint.Y, 0),
                new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, 0),
            }.Select(p => p.TransformBy(x)).ToList();

            return new Frame
            {
                Id = br.Id,
                MinX = corners.Min(p => p.X),
                MinY = corners.Min(p => p.Y),
                MaxX = corners.Max(p => p.X),
                MaxY = corners.Max(p => p.Y),
            };
        }

        private static Frame FromExtents(ObjectId id, Extents3d e) => new Frame
        {
            Id = id,
            MinX = e.MinPoint.X,
            MinY = e.MinPoint.Y,
            MaxX = e.MaxPoint.X,
            MaxY = e.MaxPoint.Y,
        };

        /// <summary>Polyline kín, 4 đỉnh (hoặc 5 với đỉnh cuối trùng đỉnh đầu), không cung, cạnh song song trục X/Y.</summary>
        public static bool IsRectangle(Polyline pl)
        {
            var n = pl.NumberOfVertices;
            var pts = new List<Point2d>();
            for (var i = 0; i < n; i++)
            {
                if (Math.Abs(pl.GetBulgeAt(i)) > 1e-9) return false;
                pts.Add(pl.GetPoint2dAt(i));
            }

            var size = pl.GeometricExtents.MaxPoint - pl.GeometricExtents.MinPoint;
            var tol = Math.Max(size.X, size.Y) * 1e-6;

            if (pts.Count == 5 && pts[0].GetDistanceTo(pts[4]) <= tol) pts.RemoveAt(4);
            else if (!pl.Closed) return false;
            if (pts.Count != 4) return false;

            for (var i = 0; i < 4; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % 4];
                var horizontal = Math.Abs(a.Y - b.Y) <= tol;
                var vertical = Math.Abs(a.X - b.X) <= tol;
                if (horizontal == vertical) return false; // xiên hoặc cạnh dài 0
            }
            return true;
        }
    }
}

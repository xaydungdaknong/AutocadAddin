using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace DwgToDxf.Printing
{
    /// <summary>
    /// Một ô chữ trong khung tên (ví dụ ký hiệu bản vẽ, tên bản vẽ), dùng để đọc/ghi cùng ô đó trên mọi khung.
    /// Khung là block có attribute: nhận theo tên attribute (tag).
    /// Còn lại: nhận theo vị trí tương đối của chữ trong khung (TEXT/MTEXT nằm trong khung, gần vị trí đó nhất).
    /// </summary>
    public class SheetField
    {
        /// <summary>Tên attribute. Null: nhận theo vị trí.</summary>
        public string Tag { get; set; }

        /// <summary>Vị trí tương đối trong khung (0..1 theo chiều rộng/cao).</summary>
        public double RelX { get; set; }
        public double RelY { get; set; }

        public override string ToString() =>
            Tag != null ? "attribute \"" + Tag + "\"" : string.Format("chữ tại {0:0}% ngang, {1:0}% đứng của khung", RelX * 100, RelY * 100);

        /// <summary>
        /// Cho người dùng bấm vào một chữ (attribute, TEXT hoặc MTEXT) nằm trong một khung.
        /// Trả về null nếu hủy hoặc nhấn Enter (khi cho phép bỏ qua).
        /// </summary>
        public static SheetField Pick(Editor ed, Database db, IList<Frame> frames, string message, bool allowNone, out bool skipped)
        {
            skipped = false;
            var pno = new PromptNestedEntityOptions(message) { AllowNone = allowNone };
            while (true)
            {
                var r = ed.GetNestedEntity(pno);
                if (r.Status == PromptStatus.None) { skipped = true; return null; }
                if (r.Status != PromptStatus.OK) return null;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ent = tr.GetObject(r.ObjectId, OpenMode.ForRead) as Entity;
                    tr.Commit();

                    if (ent is AttributeReference att)
                        return new SheetField { Tag = att.Tag };

                    if (!(ent is DBText) && !(ent is MText))
                    {
                        ed.WriteMessage("\nHãy bấm vào một chữ (attribute, TEXT hoặc MTEXT).");
                        continue;
                    }
                    if (r.GetContainers().Length > 0)
                    {
                        ed.WriteMessage("\nChữ này nằm trong block nhưng không phải attribute nên không sửa riêng từng khung được. Hãy dùng attribute, hoặc chữ vẽ rời ngoài block.");
                        continue;
                    }

                    var c = Center(ent);
                    var frame = FrameOrder.At(frames, c.X, c.Y);
                    if (frame == null)
                    {
                        ed.WriteMessage("\nChữ này không nằm trong khung nào.");
                        continue;
                    }
                    return new SheetField
                    {
                        RelX = (c.X - frame.MinX) / frame.Width,
                        RelY = (c.Y - frame.MinY) / frame.Height,
                    };
                }
            }
        }

        /// <summary>Tìm ô chữ này trong từng khung. Khung không tìm thấy có giá trị ObjectId.Null.</summary>
        public Dictionary<Frame, ObjectId> Locate(Transaction tr, ObjectId spaceId, IList<Frame> frames)
        {
            var result = new Dictionary<Frame, ObjectId>();
            if (Tag != null)
            {
                // Attribute của chính block khung; nếu khung là polyline (hoặc block khác) thì tìm
                // attribute cùng tên của block nằm trong khung (ví dụ block khung tên đặt trong vùng in).
                List<KeyValuePair<ObjectId, Point3d>> loose = null;
                foreach (var f in frames)
                {
                    result[f] = ObjectId.Null;
                    if (!f.Id.IsNull && tr.GetObject(f.Id, OpenMode.ForRead) is BlockReference own)
                        result[f] = FindTag(tr, own);
                    if (!result[f].IsNull) continue;

                    if (loose == null) loose = AttributesInSpace(tr, spaceId);
                    var hit = loose.FirstOrDefault(a => f.Contains(a.Value.X, a.Value.Y));
                    if (!hit.Key.IsNull) result[f] = hit.Key;
                }
                return result;
            }

            // Gom mọi chữ rời trong không gian một lần, rồi chọn chữ gần vị trí tương đối nhất trong từng khung.
            var texts = new List<KeyValuePair<ObjectId, Point3d>>();
            var space = (BlockTableRecord)tr.GetObject(spaceId, OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                var dxf = id.ObjectClass.DxfName;
                if (dxf != "TEXT" && dxf != "MTEXT") continue;
                var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                try { texts.Add(new KeyValuePair<ObjectId, Point3d>(id, Center(ent))); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { /* chữ rỗng, không có extents */ }
            }

            foreach (var f in frames)
            {
                var target = new Point3d(f.MinX + RelX * f.Width, f.MinY + RelY * f.Height, 0);
                var tol = Math.Min(f.Width, f.Height) * 0.05;
                var best = texts
                    .Where(t => f.Contains(t.Value.X, t.Value.Y))
                    .Select(t => new { t.Key, D = Distance2d(t.Value, target) })
                    .Where(t => t.D <= tol)
                    .OrderBy(t => t.D)
                    .FirstOrDefault();
                result[f] = best == null ? ObjectId.Null : best.Key;
            }
            return result;
        }

        public static string Read(Transaction tr, ObjectId id)
        {
            if (id.IsNull) return "";
            var o = tr.GetObject(id, OpenMode.ForRead);
            if (o is AttributeReference a) return a.IsMTextAttribute ? a.MTextAttribute.Text : a.TextString;
            if (o is DBText t) return t.TextString;
            if (o is MText m) return m.Text;
            return "";
        }

        public static void Write(Transaction tr, ObjectId id, string value)
        {
            var o = tr.GetObject(id, OpenMode.ForWrite);
            if (o is AttributeReference a) a.TextString = value;
            else if (o is DBText t) t.TextString = value;
            else if (o is MText m) m.Contents = value;
        }

        private ObjectId FindTag(Transaction tr, BlockReference br)
        {
            foreach (ObjectId aid in br.AttributeCollection)
            {
                var a = (AttributeReference)tr.GetObject(aid, OpenMode.ForRead);
                if (string.Equals(a.Tag, Tag, StringComparison.OrdinalIgnoreCase)) return aid;
            }
            return ObjectId.Null;
        }

        private List<KeyValuePair<ObjectId, Point3d>> AttributesInSpace(Transaction tr, ObjectId spaceId)
        {
            var list = new List<KeyValuePair<ObjectId, Point3d>>();
            var space = (BlockTableRecord)tr.GetObject(spaceId, OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                if (id.ObjectClass.DxfName != "INSERT") continue;
                var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                var aid = FindTag(tr, br);
                if (aid.IsNull) continue;
                var a = (AttributeReference)tr.GetObject(aid, OpenMode.ForRead);
                list.Add(new KeyValuePair<ObjectId, Point3d>(aid, a.Position));
            }
            return list;
        }

        private static Point3d Center(Entity e)
        {
            var x = e.GeometricExtents;
            return new Point3d((x.MinPoint.X + x.MaxPoint.X) / 2, (x.MinPoint.Y + x.MaxPoint.Y) / 2, 0);
        }

        private static double Distance2d(Point3d a, Point3d b) =>
            Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}

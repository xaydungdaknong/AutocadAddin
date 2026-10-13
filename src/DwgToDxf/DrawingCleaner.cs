using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace DwgToDxf
{
    /// <summary>
    /// Làm sạch một Database trước khi xuất DXF, để file DXF chỉ còn hình học
    /// và chữ phẳng, dễ đọc bằng máy:
    /// audit, bind xref, phá block (attribute thành text), xóa đối tượng rỗng, purge.
    /// Database phải đang là WorkingDatabase khi gọi.
    /// </summary>
    public static class DrawingCleaner
    {
        private const int MaxExplodePasses = 20;

        public static void Clean(Database db, IList<string> log)
        {
            // Mỗi bước lỗi chỉ được ghi log, các bước sau và việc xuất DXF vẫn chạy.
            Step("audit", () => Audit(db, log), log);
            Step("xref", () => BindXrefs(db, log), log);
            Step("block", () => ExplodeBlocks(db, log), log);
            Step("rac", () => EraseEmptyEntities(db, log), log);
            Step("purge", () => PurgeAll(db, log), log);
        }

        private static void Step(string name, Action action, IList<string> log)
        {
            try { action(); }
            catch (System.Exception ex) { log.Add(name + ": loi " + ex.Message); }
        }

        /// <summary>Kiểm tra và sửa lỗi cấu trúc (giống lệnh AUDIT, trả lời Yes).</summary>
        private static void Audit(Database db, IList<string> log)
        {
            // Database.Audit không có trong mọi bản API, nên gọi qua reflection.
            var audit = typeof(Database).GetMethod("Audit", new[] { typeof(bool), typeof(bool) });
            if (audit == null)
            {
                log.Add("audit: API khong co, bo qua");
                return;
            }
            try
            {
                audit.Invoke(db, new object[] { true, false });
                log.Add("audit: xong");
            }
            catch (System.Exception ex)
            {
                log.Add("audit: loi " + (ex.InnerException ?? ex).Message);
            }
        }

        /// <summary>
        /// Bind (kiểu Insert) mọi xref tìm thấy; detach xref không tìm thấy file
        /// để DXF không còn tham chiếu ra ngoài.
        /// </summary>
        private static void BindXrefs(Database db, IList<string> log)
        {
            var unloaded = new ObjectIdCollection();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (btr.IsFromExternalReference && btr.IsUnloaded)
                        unloaded.Add(id);
                }
                tr.Commit();
            }
            if (unloaded.Count > 0)
            {
                try { db.ReloadXrefs(unloaded); }
                catch (System.Exception ex) { log.Add("xref: khong reload duoc xref dang unload: " + ex.Message); }
            }

            db.ResolveXrefs(false, false);

            var toBind = new ObjectIdCollection();
            var toDetach = new List<(ObjectId Id, string Name)>();
            var graph = db.GetHostDwgXrefGraph(true);
            for (int i = 1; i < graph.NumNodes; i++) // node 0 là bản vẽ chính
            {
                var node = graph.GetXrefNode(i);
                if (node.IsNested) continue; // bind xref cha sẽ kéo theo xref con
                if (node.XrefStatus == XrefStatus.Resolved)
                    toBind.Add(node.BlockTableRecordId);
                else
                    toDetach.Add((node.BlockTableRecordId, node.Name + " (" + node.XrefStatus + ")"));
            }

            if (toBind.Count > 0)
            {
                try
                {
                    db.BindXrefs(toBind, true);
                    log.Add("xref: da bind " + toBind.Count);
                }
                catch (System.Exception ex)
                {
                    log.Add("xref: bind loi " + ex.Message);
                }
            }

            foreach (var x in toDetach)
            {
                try
                {
                    db.DetachXref(x.Id);
                    log.Add("xref: khong tim thay, da detach " + x.Name);
                }
                catch (System.Exception ex)
                {
                    log.Add("xref: detach loi " + x.Name + ": " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Phá mọi block reference trong Model và các Layout, lặp lại cho block lồng nhau.
        /// Attribute nhìn thấy được chuyển thành TEXT/MTEXT giữ nguyên giá trị.
        /// Bảng (TABLE) được giữ nguyên.
        /// </summary>
        private static void ExplodeBlocks(Database db, IList<string> log)
        {
            var failed = new HashSet<ObjectId>();
            int exploded = 0;

            for (int pass = 0; pass < MaxExplodePasses; pass++)
            {
                int thisPass = 0;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    foreach (ObjectId btrId in bt)
                    {
                        var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                        if (!btr.IsLayout) continue;

                        var refs = btr.Cast<ObjectId>()
                            .Where(id => !failed.Contains(id) && id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference))))
                            .ToList();
                        if (refs.Count == 0) continue;
                        btr.UpgradeOpen();

                        foreach (var id in refs)
                        {
                            var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                            if (br is Table) { failed.Add(id); continue; }

                            try
                            {
                                foreach (var ent in ExplodeOne(tr, br))
                                {
                                    btr.AppendEntity(ent);
                                    tr.AddNewlyCreatedDBObject(ent, true);
                                    if (ent is DBText t && !(t.HorizontalMode == TextHorizontalMode.TextLeft && t.VerticalMode == TextVerticalMode.TextBase))
                                        t.AdjustAlignment(db);
                                }
                                br.UpgradeOpen();
                                br.Erase();
                                thisPass++;
                            }
                            catch (System.Exception ex)
                            {
                                failed.Add(id);
                                log.Add("block: khong pha duoc " + br.Name + " (" + ex.Message + "), giu nguyen");
                            }
                        }
                    }
                    tr.Commit();
                }
                exploded += thisPass;
                if (thisPass == 0) break;
            }
            log.Add("block: da pha " + exploded + " block reference");
        }

        private static List<Entity> ExplodeOne(Transaction tr, BlockReference br)
        {
            var result = new List<Entity>();
            var parts = new DBObjectCollection();
            br.Explode(parts);

            foreach (DBObject obj in parts)
            {
                if (obj is AttributeDefinition ad)
                {
                    // Attribute không phải hằng số được lấy từ AttributeReference bên dưới.
                    if (ad.Constant && !ad.Invisible)
                        result.Add(InheritFromBlock(ToText(ad), br));
                    ad.Dispose();
                    continue;
                }
                if (obj is Entity ent)
                    result.Add(InheritFromBlock(ent, br));
                else
                    obj.Dispose();
            }

            foreach (ObjectId arId in br.AttributeCollection)
            {
                var ar = (AttributeReference)tr.GetObject(arId, OpenMode.ForRead);
                if (ar.Invisible) continue;

                Entity text;
                if (ar.IsMTextAttribute)
                {
                    var m = ar.MTextAttribute; // bản sao MText mới
                    m.SetPropertiesFrom(ar);
                    text = m;
                }
                else
                {
                    text = ToText(ar);
                }
                result.Add(InheritFromBlock(text, br));
            }
            return result;
        }

        /// <summary>Đối tượng trên layer 0 hoặc thuộc tính ByBlock lấy theo block chứa nó, như khi hiển thị.</summary>
        private static Entity InheritFromBlock(Entity ent, BlockReference br)
        {
            if (string.Equals(ent.Layer, "0", StringComparison.OrdinalIgnoreCase))
                ent.LayerId = br.LayerId;
            if (ent.Color.IsByBlock)
                ent.Color = br.Color;
            if (string.Equals(ent.Linetype, "BYBLOCK", StringComparison.OrdinalIgnoreCase))
                ent.LinetypeId = br.LinetypeId;
            if (ent.LineWeight == LineWeight.ByBlock)
                ent.LineWeight = br.LineWeight;
            return ent;
        }

        private static DBText ToText(DBText src)
        {
            var t = new DBText();
            t.SetPropertiesFrom(src);
            t.Normal = src.Normal;
            t.TextStyleId = src.TextStyleId;
            t.TextString = src.TextString;
            t.Height = src.Height;
            t.WidthFactor = src.WidthFactor;
            t.Oblique = src.Oblique;
            t.Rotation = src.Rotation;
            t.Thickness = src.Thickness;
            t.IsMirroredInX = src.IsMirroredInX;
            t.IsMirroredInY = src.IsMirroredInY;
            t.Position = src.Position;
            t.HorizontalMode = src.HorizontalMode;
            t.VerticalMode = src.VerticalMode;
            if (!(src.HorizontalMode == TextHorizontalMode.TextLeft && src.VerticalMode == TextVerticalMode.TextBase))
                t.AlignmentPoint = src.AlignmentPoint;
            return t;
        }

        /// <summary>Xóa chữ rỗng và đường thẳng dài bằng 0 trong Model và các Layout.</summary>
        private static void EraseEmptyEntities(Database db, IList<string> log)
        {
            int erased = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId btrId in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    if (!btr.IsLayout) continue;
                    foreach (ObjectId id in btr)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        bool empty =
                            (ent is DBText t && string.IsNullOrWhiteSpace(t.TextString)) ||
                            (ent is MText m && string.IsNullOrWhiteSpace(m.Text)) ||
                            (ent is Line l && l.Length < Tolerance.Global.EqualPoint);
                        if (!empty) continue;
                        try
                        {
                            ent.UpgradeOpen();
                            ent.Erase();
                            erased++;
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception) { /* layer khóa hoặc đối tượng đặc biệt: bỏ qua */ }
                    }
                }
                tr.Commit();
            }
            log.Add("rac: da xoa " + erased + " doi tuong rong");
        }

        /// <summary>Purge lặp lại cho tới khi không còn gì để xóa (giống PURGE All, gồm lồng nhau và RegApp).</summary>
        private static void PurgeAll(Database db, IList<string> log)
        {
            int total = 0;
            for (int pass = 0; pass < 20; pass++)
            {
                var ids = new ObjectIdCollection();
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    AddTable(tr, db.BlockTableId, ids);
                    AddTable(tr, db.LayerTableId, ids);
                    AddTable(tr, db.LinetypeTableId, ids);
                    AddTable(tr, db.TextStyleTableId, ids);
                    AddTable(tr, db.DimStyleTableId, ids);
                    AddTable(tr, db.RegAppTableId, ids);
                    AddDictionary(tr, db.MLStyleDictionaryId, ids);
                    AddDictionary(tr, db.TableStyleDictionaryId, ids);
                    AddDictionary(tr, db.MLeaderStyleDictionaryId, ids);
                    AddDictionary(tr, db.MaterialDictionaryId, ids);
                    tr.Commit();
                }

                db.Purge(ids);
                if (ids.Count == 0) break;

                int erased = 0;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in ids)
                    {
                        try
                        {
                            tr.GetObject(id, OpenMode.ForWrite).Erase();
                            erased++;
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception) { /* layer 0, Standard, layer hiện hành...: không xóa được */ }
                    }
                    tr.Commit();
                }
                total += erased;
                if (erased == 0) break;
            }
            log.Add("purge: da xoa " + total + " muc khong dung");
        }

        private static void AddTable(Transaction tr, ObjectId tableId, ObjectIdCollection ids)
        {
            var table = (SymbolTable)tr.GetObject(tableId, OpenMode.ForRead);
            foreach (ObjectId id in table)
            {
                if (tableId == table.Database.BlockTableId)
                {
                    var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (btr.IsLayout || btr.IsFromExternalReference) continue;
                }
                ids.Add(id);
            }
        }

        private static void AddDictionary(Transaction tr, ObjectId dictId, ObjectIdCollection ids)
        {
            if (dictId.IsNull) return;
            var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForRead);
            foreach (DBDictionaryEntry e in dict)
                ids.Add(e.Value);
        }
    }
}

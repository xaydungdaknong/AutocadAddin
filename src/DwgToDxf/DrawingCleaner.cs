using System;
using System.Collections.Generic;
using System.Linq;
#if !ACAD2010
using Autodesk.AutoCAD.ApplicationServices;
#endif
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

            // Layer bị khóa làm phá block và xóa đối tượng thất bại; mở khóa tạm, xong khóa lại.
            var locked = new List<ObjectId>();
            Step("layer", () => UnlockLayers(db, locked, log), log);
            try
            {
                Step("block", () => ExplodeBlocks(db, log), log);
                Step("rac", () => EraseEmptyEntities(db, log), log);
            }
            finally
            {
                Step("layer", () => RelockLayers(db, locked), log);
            }

            Step("purge", () => PurgeAll(db, log), log);
        }

        private static void UnlockLayers(Database db, List<ObjectId> locked, IList<string> log)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt)
                {
                    var layer = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (!layer.IsLocked) continue;
                    layer.UpgradeOpen();
                    layer.IsLocked = false;
                    locked.Add(id);
                }
                tr.Commit();
            }
            if (locked.Count > 0)
                log.Add("layer: mo khoa tam " + locked.Count + " layer");
        }

        private static void RelockLayers(Database db, List<ObjectId> locked)
        {
            if (locked.Count == 0) return;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in locked)
                {
                    if (id.IsErased) continue;
                    ((LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite)).IsLocked = true;
                }
                tr.Commit();
            }
        }

        private static void Step(string name, Action action, IList<string> log)
        {
            try { action(); }
            catch (System.Exception ex) { log.Add(name + ": loi " + ex.Message); }
        }

        /// <summary>Kiểm tra và sửa lỗi cấu trúc (giống lệnh AUDIT, trả lời Yes).</summary>
        private static void Audit(Database db, IList<string> log)
        {
            // Audit là extension method trong AcCoreMgd (ApplicationServices.DatabaseExtension),
            // không phải method của Database, nên trước đây tìm bằng reflection luôn bị bỏ qua.
#if ACAD2010
            // API .NET của AutoCAD 2010 không có Audit cho database.
            log.Add("audit: AutoCAD 2010 khong ho tro, bo qua");
#else
            db.Audit(true, false);
            log.Add("audit: xong");
#endif
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

                            List<Entity> parts = null;
                            try
                            {
                                parts = ExplodeOne(tr, br, out var nested);
                                // Mở block để ghi trước khi thêm phần đã phá: nếu không mở được
                                // (ví dụ layer khóa) thì chưa có gì thay đổi, không sinh hình trùng.
                                br.UpgradeOpen();
                                foreach (var ent in parts)
                                {
                                    btr.AppendEntity(ent);
                                    tr.AddNewlyCreatedDBObject(ent, true);
                                    if (ent is DBText t && !(t.HorizontalMode == TextHorizontalMode.TextLeft && t.VerticalMode == TextVerticalMode.TextBase))
                                        t.AdjustAlignment(db);
                                }
                                parts = null;
                                br.Erase();
                                thisPass++;

                                try
                                {
                                    if (!CopyNestedAttributes(tr, nested))
                                        log.Add("block: " + br.Name + " co block long nhau chua attribute, khong chep duoc attribute cua block con");
                                }
                                catch (System.Exception ex)
                                {
                                    log.Add("block: " + br.Name + " loi chep attribute block con: " + ex.Message);
                                }
                            }
                            catch (System.Exception ex)
                            {
                                failed.Add(id);
                                log.Add("block: khong pha duoc " + br.Name + " (" + ex.Message + "), giu nguyen");
                            }
                            finally
                            {
                                if (parts != null)
                                    foreach (var ent in parts)
                                        if (ent.ObjectId.IsNull) ent.Dispose();
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

        /// <summary>
        /// Block con (lồng trong block đang phá) kèm attribute của nó trong định nghĩa block.
        /// BlockReference.Explode trả về bản sao block con không có attribute, nên phải chép lại.
        /// Null Copy nghĩa là không ghép được bản sao với block con gốc.
        /// </summary>
        private sealed class NestedRef
        {
            public BlockReference Copy;
            public BlockReference Source;
            public Matrix3d Transform;
        }

        private static List<Entity> ExplodeOne(Transaction tr, BlockReference br, out List<NestedRef> nested)
        {
            var result = new List<Entity>();
            var parts = new DBObjectCollection();
            br.Explode(parts);
            nested = MatchNestedRefs(tr, br, parts);

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

        /// <summary>
        /// Ghép các block con trong kết quả Explode với block con gốc trong định nghĩa block.
        /// Explode trả về đối tượng theo đúng thứ tự trong định nghĩa, nên ghép theo vị trí
        /// và kiểm tra cùng kiểu. Trả về null Copy nếu không ghép được.
        /// </summary>
        private static List<NestedRef> MatchNestedRefs(Transaction tr, BlockReference br, DBObjectCollection parts)
        {
            var list = new List<NestedRef>();
            var def = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            var sources = def.Cast<ObjectId>().ToList();

            var withAttributes = new List<(int Index, BlockReference Source)>();
            for (int i = 0; i < sources.Count; i++)
            {
                if (tr.GetObject(sources[i], OpenMode.ForRead) is BlockReference src && !(src is Table) && src.AttributeCollection.Count > 0)
                    withAttributes.Add((i, src));
            }
            if (withAttributes.Count == 0) return list;

            bool aligned = !(br is MInsertBlock) && parts.Count == sources.Count;
            foreach (var w in withAttributes)
            {
                var copy = aligned ? parts[w.Index] as BlockReference : null;
                if (copy != null && copy.BlockTableRecord != w.Source.BlockTableRecord) copy = null;
                list.Add(new NestedRef { Copy = copy, Source = w.Source, Transform = br.BlockTransform });
            }
            return list;
        }

        /// <summary>Chép attribute của block con gốc sang bản sao vừa thêm vào bản vẽ. False nếu có block con không ghép được.</summary>
        private static bool CopyNestedAttributes(Transaction tr, List<NestedRef> nested)
        {
            bool ok = true;
            foreach (var n in nested)
            {
                if (n.Copy == null || n.Copy.ObjectId.IsNull) { ok = false; continue; }
                if (n.Copy.AttributeCollection.Count > 0) continue; // API đã tự chép

                foreach (ObjectId arId in n.Source.AttributeCollection)
                {
                    var src = (AttributeReference)tr.GetObject(arId, OpenMode.ForRead);
                    var ar = (AttributeReference)src.GetTransformedCopy(n.Transform);
                    n.Copy.AttributeCollection.AppendAttribute(ar);
                    tr.AddNewlyCreatedDBObject(ar, true);
                    // Layer 0 / ByBlock của attribute được xử lý khi phá block con ở lượt sau.
                }
            }
            return ok;
        }

        /// <summary>
        /// Đối tượng trên layer 0 hoặc thuộc tính ByBlock lấy theo block chứa nó, như khi hiển thị.
        /// Nếu block chứa có thuộc tính ByLayer mà đối tượng nằm trên layer khác, lấy giá trị thực của layer block chứa.
        /// </summary>
        private static Entity InheritFromBlock(Entity ent, BlockReference br)
        {
            if (string.Equals(ent.Layer, "0", StringComparison.OrdinalIgnoreCase))
                ent.LayerId = br.LayerId;

            bool sameLayer = ent.LayerId == br.LayerId;
            LayerTableRecord brLayer = null;
            LayerTableRecord BrLayer()
            {
                if (brLayer == null)
                    brLayer = (LayerTableRecord)br.LayerId.GetObject(OpenMode.ForRead);
                return brLayer;
            }

            if (ent.Color.IsByBlock)
                ent.Color = br.Color.IsByLayer && !sameLayer ? BrLayer().Color : br.Color;

            if (string.Equals(ent.Linetype, "BYBLOCK", StringComparison.OrdinalIgnoreCase))
            {
                bool brByLayer = string.Equals(br.Linetype, "BYLAYER", StringComparison.OrdinalIgnoreCase);
                ent.LinetypeId = brByLayer && !sameLayer ? BrLayer().LinetypeObjectId : br.LinetypeId;
            }

            if (ent.LineWeight == LineWeight.ByBlock)
                ent.LineWeight = br.LineWeight == LineWeight.ByLayer && !sameLayer ? BrLayer().LineWeight : br.LineWeight;

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
                            (ent is DBText t && Compat.IsBlank(t.TextString)) ||
                            (ent is MText m && Compat.IsBlank(m.Text)) ||
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

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace DwgToDxf.Printing
{
    /// <summary>
    /// Công cụ làm việc với các khung theo thứ tự in: đánh số hiệu bản vẽ vào khung tên,
    /// lập bảng danh mục bản vẽ, vẽ vùng in.
    /// </summary>
    public static class SheetTools
    {
        public const string AreaLayer = "VUNG_IN";

        // Giá trị nhập lần trước, dùng làm mặc định trong phiên làm việc.
        private static string _prefix = "";
        private static string _suffix = "";
        private static int _start = 1;
        private static int _digits = 2;
        private static bool _withTotal;
        private static double _listHeight;
        private static int _rowsPerColumn;

        // ---------- đánh số bản vẽ ----------

        /// <summary>Ghi số hiệu bản vẽ vào cùng một ô chữ của mọi khung, theo thứ tự in.</summary>
        public static void Number(Document doc, ObjectId spaceId, List<Frame> order)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            var field = SheetField.Pick(ed, db, order, "\nBấm vào ô số hiệu bản vẽ trong một khung (attribute hoặc chữ): ", false, out _);
            if (field == null) return;
            ed.WriteMessage("\nÔ số hiệu: {0}.", field);

            var prefix = AskText(ed, "Tiền tố (ví dụ KC-)", _prefix);
            if (prefix == null) return;
            var suffix = AskText(ed, "Hậu tố", _suffix);
            if (suffix == null) return;
            var start = AskInt(ed, "Số bắt đầu", _start, 0, 100000);
            if (start < 0) return;
            var digits = AskInt(ed, "Số chữ số (2: 01, 02...)", _digits, 1, 6);
            if (digits < 0) return;

            var pko = new PromptKeywordOptions("\nGhi kèm tổng số bản vẽ (ví dụ 01/10)? [Yes/No] <" + (_withTotal ? "Yes" : "No") + ">: ", "Yes No");
            pko.Keywords.Default = _withTotal ? "Yes" : "No";
            pko.AllowNone = true;
            var kr = ed.GetKeywords(pko);
            if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
            var withTotal = (kr.Status == PromptStatus.None ? pko.Keywords.Default : kr.StringResult) == "Yes";

            _prefix = prefix; _suffix = suffix; _start = start; _digits = digits; _withTotal = withTotal;

            var fmt = "D" + digits;
            var total = (start + order.Count - 1).ToString(fmt);
            var missing = new List<int>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var cells = field.Locate(tr, spaceId, order);
                for (var i = 0; i < order.Count; i++)
                {
                    var id = cells[order[i]];
                    if (id.IsNull)
                    {
                        missing.Add(i + 1);
                        continue;
                    }
                    var value = prefix + (start + i).ToString(fmt) + suffix + (withTotal ? "/" + total : "");
                    SheetField.Write(tr, id, value);
                }
                tr.Commit();
            }

            ed.WriteMessage("\nĐã đánh số {0}/{1} khung.", order.Count - missing.Count, order.Count);
            if (missing.Count > 0)
                ed.WriteMessage(" Không tìm thấy ô số hiệu ở khung thứ {0}.", string.Join(", ", missing.Select(n => n.ToString()).ToArray()));
            ed.Regen();
        }

        // ---------- danh mục bản vẽ ----------

        /// <summary>Vẽ bảng danh mục bản vẽ (STT, ký hiệu, tên bản vẽ) theo thứ tự in.</summary>
        public static void DrawList(Document doc, ObjectId spaceId, List<Frame> order)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            var numberField = SheetField.Pick(ed, db, order,
                "\nBấm vào ô ký hiệu bản vẽ trong một khung <Enter: bỏ qua, chỉ dùng STT>: ", true, out var skipped);
            if (numberField == null && !skipped) return;
            var titleField = SheetField.Pick(ed, db, order, "\nBấm vào ô tên bản vẽ trong một khung: ", false, out _);
            if (titleField == null) return;

            var rows = new List<string[]>();
            var missing = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var numbers = numberField?.Locate(tr, spaceId, order);
                var titles = titleField.Locate(tr, spaceId, order);
                for (var i = 0; i < order.Count; i++)
                {
                    var title = OneLine(SheetField.Read(tr, titles[order[i]]));
                    if (titles[order[i]].IsNull) missing++;
                    var number = numbers == null ? "" : OneLine(SheetField.Read(tr, numbers[order[i]]));
                    rows.Add(new[] { (i + 1).ToString(), number, title });
                }
                tr.Commit();
            }

            var dfltHeight = _listHeight > 0 ? _listHeight : Nice(order[0].Height / 120.0);
            var pdo = new PromptDoubleOptions("\nChiều cao chữ <" + dfltHeight + ">: ")
            {
                AllowNone = true, AllowZero = false, AllowNegative = false, DefaultValue = dfltHeight, UseDefaultValue = true,
            };
            var hr = ed.GetDouble(pdo);
            if (hr.Status != PromptStatus.OK) return;
            var h = hr.Value;

            var perColumn = AskInt(ed, "Số dòng mỗi cột (0: một cột)", _rowsPerColumn, 0, 100000);
            if (perColumn < 0) return;
            _listHeight = h; _rowsPerColumn = perColumn;

            var pr = ed.GetPoint("\nĐiểm đặt bảng (góc trên trái): ");
            if (pr.Status != PromptStatus.OK) return;
            var origin = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(spaceId, OpenMode.ForWrite);
                var writer = new TableWriter(db, tr, space, h, rows.Select(r => r[2]).Concat(new[] { "TÊN BẢN VẼ" }).Max(s => s.Length));
                var chunk = perColumn > 0 ? perColumn : Math.Max(1, rows.Count);
                var x = origin.X;
                for (var i = 0; i < rows.Count; i += chunk)
                {
                    writer.Draw(new Point3d(x, origin.Y, origin.Z), rows.Skip(i).Take(chunk).ToList(), i == 0);
                    x += writer.Width + 4 * h;
                }
                tr.Commit();
            }

            ed.WriteMessage("\nĐã lập danh mục {0} bản vẽ.", rows.Count);
            if (missing > 0) ed.WriteMessage(" {0} khung không tìm thấy ô tên bản vẽ (để trống).", missing);
        }

        /// <summary>Vẽ từng khối bảng: tiêu đề (chỉ khối đầu), dòng tiêu đề cột, các dòng dữ liệu.</summary>
        private sealed class TableWriter
        {
            private readonly Database _db;
            private readonly Transaction _tr;
            private readonly BlockTableRecord _space;
            private readonly double _h, _row;
            private readonly double[] _widths;

            public TableWriter(Database db, Transaction tr, BlockTableRecord space, double h, int longestTitle)
            {
                _db = db; _tr = tr; _space = space; _h = h;
                _row = 2.2 * h;
                _widths = new[] { 5 * h, 14 * h, Math.Max(30 * h, (longestTitle * 0.85 + 3) * h) };
            }

            public double Width => _widths.Sum();

            public void Draw(Point3d topLeft, List<string[]> rows, bool withTitle)
            {
                var y = topLeft.Y;
                if (withTitle)
                {
                    Text("DANH MỤC BẢN VẼ", new Point3d(topLeft.X + Width / 2, y - _row / 2, 0), 1.4 * _h, true);
                    y -= 1.5 * _row;
                }

                var top = y;
                var lines = new List<string[]> { new[] { "STT", "KÝ HIỆU", "TÊN BẢN VẼ" } };
                lines.AddRange(rows);
                for (var r = 0; r < lines.Count; r++)
                {
                    var cx = topLeft.X;
                    for (var c = 0; c < 3; c++)
                    {
                        var center = c < 2 || r == 0;
                        var at = center
                            ? new Point3d(cx + _widths[c] / 2, y - _row / 2, 0)
                            : new Point3d(cx + _h, y - _row / 2, 0);
                        if (!string.IsNullOrEmpty(lines[r][c])) Text(lines[r][c], at, _h, center);
                        cx += _widths[c];
                    }
                    y -= _row;
                    Line(topLeft.X, y, topLeft.X + Width, y);
                }

                // Khung ngoài, đường kẻ dưới dòng tiêu đề cột và các đường dọc.
                Line(topLeft.X, top, topLeft.X + Width, top);
                var x = topLeft.X;
                Line(x, top, x, y);
                foreach (var w in _widths)
                {
                    x += w;
                    Line(x, top, x, y);
                }
            }

            private void Line(double x1, double y1, double x2, double y2)
            {
                var l = new Line(new Point3d(x1, y1, 0), new Point3d(x2, y2, 0));
                l.SetDatabaseDefaults(_db);
                _space.AppendEntity(l);
                _tr.AddNewlyCreatedDBObject(l, true);
            }

            private void Text(string s, Point3d at, double height, bool center)
            {
                var t = new DBText { TextString = s, Height = height };
                t.SetDatabaseDefaults(_db);
                t.Height = height;
                t.HorizontalMode = center ? TextHorizontalMode.TextCenter : TextHorizontalMode.TextLeft;
                t.VerticalMode = TextVerticalMode.TextVerticalMid;
                t.Position = at;
                t.AlignmentPoint = at;
                _space.AppendEntity(t);
                _tr.AddNewlyCreatedDBObject(t, true);
                t.AdjustAlignment(_db);
            }
        }

        // ---------- vẽ vùng in ----------

        /// <summary>
        /// Vẽ các hình chữ nhật vùng in trên layer VUNG_IN (không in ra giấy) và đặt cấu hình in
        /// nhận khung theo layer này, để INMODEL/INLAYOUT/INNHIEUFILE tự tìm được.
        /// </summary>
        public static void DrawAreas(Document doc)
        {
            var ed = doc.Editor;
            var db = doc.Database;
            EnsureAreaLayer(db);

            var count = 0;
            while (true)
            {
                var ppo = new PromptPointOptions(string.Format("\nGóc thứ nhất của vùng in thứ {0} [Xong] <Xong>: ", count + 1), "Xong");
                ppo.AllowNone = true;
                var a = ed.GetPoint(ppo);
                if (a.Status != PromptStatus.OK) break;
                var b = ed.GetCorner("\nGóc đối diện: ", a.Value);
                if (b.Status != PromptStatus.OK) break;

                var ucs = ed.CurrentUserCoordinateSystem;
                Point3d p1 = a.Value.TransformBy(ucs), p2 = b.Value.TransformBy(ucs);
                double x1 = Math.Min(p1.X, p2.X), y1 = Math.Min(p1.Y, p2.Y), x2 = Math.Max(p1.X, p2.X), y2 = Math.Max(p1.Y, p2.Y);
                if (x2 - x1 <= 0 || y2 - y1 <= 0) continue;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var pl = new Polyline();
                    pl.SetDatabaseDefaults(db);
                    pl.AddVertexAt(0, new Point2d(x1, y1), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(x2, y1), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(x2, y2), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(x1, y2), 0, 0, 0);
                    pl.Closed = true;
                    pl.Layer = AreaLayer;
                    pl.ColorIndex = 256; // theo layer
                    space.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                    tr.Commit();
                }
                count++;
                ed.UpdateScreen();
            }

            var p = ProfileStore.Current;
            p.Area = AreaMode.Frame;
            p.FrameBlock = null;
            p.FrameLayer = AreaLayer;
            ed.WriteMessage("\nĐã vẽ {0} vùng in trên layer {1} (không in ra giấy). Cấu hình in \"{2}\" đã chuyển sang nhận khung theo layer này;" +
                            " bấm Lưu trong CAIDATIN để giữ cho lần sau.", count, AreaLayer, p.Name);
        }

        private static void EnsureAreaLayer(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(AreaLayer))
                {
                    lt.UpgradeOpen();
                    var layer = new LayerTableRecord
                    {
                        Name = AreaLayer,
                        Color = Color.FromColorIndex(ColorMethod.ByAci, 6),
                        IsPlottable = false,
                    };
                    lt.Add(layer);
                    tr.AddNewlyCreatedDBObject(layer, true);
                }
                tr.Commit();
            }
        }

        // ---------- nhập liệu ----------

        /// <summary>Nhập chữ (có dấu cách). Enter giữ giá trị cũ, "." để xóa. Null nếu hủy.</summary>
        private static string AskText(Editor ed, string label, string current)
        {
            var pso = new PromptStringOptions("\n" + label + (current.Length > 0 ? " (nhập . để bỏ) <" + current + ">" : " <không có>") + ": ")
            {
                AllowSpaces = true,
            };
            var r = ed.GetString(pso);
            if (r.Status != PromptStatus.OK) return null;
            var s = r.StringResult;
            if (s == ".") return "";
            return s.Length == 0 ? current : s;
        }

        /// <returns>Số đã nhập, -1 nếu hủy.</returns>
        private static int AskInt(Editor ed, string label, int current, int min, int max)
        {
            var pio = new PromptIntegerOptions("\n" + label + " <" + current + ">: ")
            {
                AllowNone = true, AllowZero = min <= 0, AllowNegative = false,
                LowerLimit = min, UpperLimit = max, DefaultValue = current, UseDefaultValue = true,
            };
            var r = ed.GetInteger(pio);
            return r.Status == PromptStatus.OK ? r.Value : -1;
        }

        private static string OneLine(string s) =>
            (s ?? "").Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();

        /// <summary>Làm tròn chiều cao chữ về số đẹp (1, 2, 2.5, 5 x 10^n).</summary>
        private static double Nice(double v)
        {
            if (v <= 0) return 2.5;
            var p = Math.Pow(10, Math.Floor(Math.Log10(v)));
            foreach (var m in new[] { 1, 2, 2.5, 5, 10 })
                if (v <= m * p * 1.2) return m * p;
            return 10 * p;
        }
    }
}

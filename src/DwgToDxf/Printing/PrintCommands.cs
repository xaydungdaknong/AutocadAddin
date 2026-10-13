using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using WinForms = System.Windows.Forms;

[assembly: CommandClass(typeof(DwgToDxf.Printing.PrintCommands))]

namespace DwgToDxf.Printing
{
    public class PrintCommands
    {
        /// <summary>Lệnh INMODEL/INLAYOUT được gọi từ CAIDATIN: dùng thẳng vùng in của cấu hình, không hỏi lại.</summary>
        private static bool _useProfileArea;

        /// <summary>CAIDATIN: công cụ cài đặt in (cấu hình lưu được, chọn khung/vùng in, máy in, in nhiều file).</summary>
        [CommandMethod("CAIDATIN", CommandFlags.Modal)]
        public void PrintSetup() => ShowSetup();

        /// <summary>INNHIEUFILE: mở công cụ cài đặt in để in nhiều file trong một thư mục.</summary>
        [CommandMethod("INNHIEUFILE", CommandFlags.Modal)]
        public void PrintFolder() => ShowSetup();

        /// <summary>INNHIEUFILECHAY: in mọi bản vẽ trong thư mục theo cấu hình đang dùng (không hỏi gì thêm).</summary>
        [CommandMethod("INNHIEUFILECHAY", CommandFlags.Session)]
        public void RunBatch()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
            var result = BatchPrinter.Run(ProfileStore.Current, msg => ed?.WriteMessage(msg));
            ed?.WriteMessage("\n" + result + "\n");
        }

        /// <summary>INMODEL: in các khung/vùng trong Model, xem và đổi thứ tự trước khi in.</summary>
        [CommandMethod("INMODEL", CommandFlags.Modal)]
        public void PrintModel()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;

            // Đang ở layout thì chuyển sang tab Model.
            if (Convert.ToInt16(AcApp.GetSystemVariable("TILEMODE")) == 0)
                LayoutManager.Current.CurrentLayout = "Model";

            PrintCurrentSpace(doc, inLayout: false);
        }

        /// <summary>
        /// INLAYOUT: in layout hiện hành. Layout chỉ có một khung giấy thì in cả layout;
        /// layout có nhiều khung thì chọn khung, xem và đổi thứ tự như INMODEL.
        /// </summary>
        [CommandMethod("INLAYOUT", CommandFlags.Modal | CommandFlags.NoTileMode)]
        public void PrintLayout()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;

            // Đang đứng trong viewport (MSPACE) thì chuyển ra giấy để chọn khung.
            if (Convert.ToInt16(AcApp.GetSystemVariable("CVPORT")) != 1)
                doc.Editor.SwitchToPaperSpace();

            PrintCurrentSpace(doc, inLayout: true);
        }

        private static void ShowSetup()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            PrintSetupForm.Mode mode = PrintSetupForm.Mode.Setup;
            SetupAction action;
            using (var ps = CurrentPageSetup(doc.Database))
            using (var form = new PrintSetupForm(mode, ps, doc.Database.PlotStyleMode))
            {
                if (Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form) != WinForms.DialogResult.OK) return;
                ProfileStore.Current = form.Result;
                action = form.Action;
            }

            switch (action)
            {
                case SetupAction.PrintModel:
                    _useProfileArea = true;
                    doc.SendStringToExecute("INMODEL ", true, false, false);
                    break;
                case SetupAction.PrintLayout:
                    _useProfileArea = true;
                    doc.SendStringToExecute("INLAYOUT ", true, false, false);
                    break;
                case SetupAction.Batch:
                    doc.SendStringToExecute("INNHIEUFILECHAY ", true, false, false);
                    break;
            }
        }

        // ---------- in không gian hiện hành ----------

        private static void PrintCurrentSpace(Document doc, bool inLayout)
        {
            var ed = doc.Editor;
            var db = doc.Database;
            var profile = ProfileStore.Current;
            var useProfile = _useProfileArea && profile.HasArea;
            _useProfileArea = false;

            ObjectId layoutId, spaceId = db.CurrentSpaceId;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                layoutId = ((BlockTableRecord)tr.GetObject(spaceId, OpenMode.ForRead)).LayoutId;
                tr.Commit();
            }

            var area = useProfile ? FromProfile(doc, spaceId, profile, askRegion: false) : AskArea(doc, spaceId, inLayout, profile);
            if (area == null) return;

            List<Frame> frames = area.Frames;
            if (frames != null && frames.Count == 0)
            {
                ed.WriteMessage("\nKhông tìm thấy khung nào.");
                return;
            }
            if (inLayout && area.FromRule && frames.Count == 1)
            {
                ed.WriteMessage("\nLayout chỉ có một khung, in cả layout.");
                frames = null;
            }

            ArrangeAndPlot(doc, layoutId, frames);
        }

        private class AreaResult
        {
            /// <summary>Null: in cả layout / cả bản vẽ.</summary>
            public List<Frame> Frames;

            /// <summary>Khung tìm theo quy tắc (block/layer), sắp theo thứ tự của cấu hình.</summary>
            public bool FromRule;
        }

        /// <summary>Hỏi vùng in: chọn khung mẫu, dùng cấu hình, quét vùng, hoặc in toàn bộ.</summary>
        private static AreaResult AskArea(Document doc, ObjectId spaceId, bool inLayout, PrintProfile profile)
        {
            var ed = doc.Editor;
            ed.WriteMessage("\nCấu hình in \"{0}\": {1}, máy in {2}.", profile.Name, profile.AreaDescription(),
                string.IsNullOrEmpty(profile.Device) ? "(chưa chọn)" : profile.Device);

            var keywords = new List<string>();
            if (profile.HasArea) keywords.Add("CauHinh");
            keywords.Add("Vung");
            keywords.Add("ToanBo");
            var dflt = profile.HasArea ? "CauHinh" : inLayout ? "ToanBo" : null;

            var peo = new PromptEntityOptions(
                "\nChọn một khung mẫu (block hoặc polyline chữ nhật) hoặc [" + string.Join("/", keywords) + "]" +
                (dflt != null ? " <" + dflt + ">" : "") + ": ",
                string.Join(" ", keywords));
            if (dflt != null)
            {
                peo.Keywords.Default = dflt;
                peo.AllowNone = true;
            }

            while (true)
            {
                var r = ed.GetEntity(peo);
                var kw = r.Status == PromptStatus.Keyword ? r.StringResult
                    : r.Status == PromptStatus.None ? dflt
                    : null;

                if (r.Status == PromptStatus.OK)
                {
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        var why = FrameFinder.CheckSample(tr, r.ObjectId);
                        if (why != null)
                        {
                            ed.WriteMessage("\n" + why);
                            tr.Commit();
                            continue;
                        }
                        FrameFinder.RuleFromSample(tr, r.ObjectId, out var block, out var layer);
                        tr.Commit();
                        profile.Area = AreaMode.Frame;
                        profile.FrameBlock = block;
                        profile.FrameLayer = layer;
                    }
                    return FromProfile(doc, spaceId, profile, askRegion: true);
                }

                switch (kw)
                {
                    case "CauHinh": return FromProfile(doc, spaceId, profile, askRegion: profile.Area == AreaMode.Frame);
                    case "Vung": return PickWindows(doc, profile);
                    case "ToanBo": return new AreaResult();
                    default: return null;
                }
            }
        }

        /// <summary>Lấy vùng in theo cấu hình.</summary>
        private static AreaResult FromProfile(Document doc, ObjectId spaceId, PrintProfile profile, bool askRegion)
        {
            switch (profile.Area)
            {
                case AreaMode.Window:
                    return new AreaResult { Frames = new List<Frame> { profile.WindowFrame() } };
                case AreaMode.Whole:
                    return new AreaResult();
            }

            IEnumerable<ObjectId> candidates = null;
            if (askRegion)
            {
                var psr = doc.Editor.GetSelection(new PromptSelectionOptions
                {
                    MessageForAdding = "\nChọn vùng chứa các khung cần in <Tất cả>: ",
                });
                if (psr.Status == PromptStatus.Cancel) return null;
                if (psr.Status == PromptStatus.OK) candidates = psr.Value.GetObjectIds();
            }

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var frames = FrameFinder.Find(tr, spaceId, profile.FrameBlock, profile.FrameLayer, candidates);
                tr.Commit();
                doc.Editor.WriteMessage("\nTìm thấy {0} khung.", frames.Count);
                return new AreaResult { Frames = FrameOrder.Sort(frames, profile.Order), FromRule = true };
            }
        }

        /// <summary>Quét lần lượt từng vùng chữ nhật cần in; thứ tự in là thứ tự quét.</summary>
        private static AreaResult PickWindows(Document doc, PrintProfile profile)
        {
            var ed = doc.Editor;
            var frames = new List<Frame>();
            using (var overlay = new FrameNumberOverlay(doc.Database))
            {
                while (true)
                {
                    var ppo = new PromptPointOptions(string.Format(
                        "\nGóc thứ nhất của vùng in thứ {0} [Xong] <Xong>: ", frames.Count + 1), "Xong");
                    ppo.AllowNone = true;
                    var a = ed.GetPoint(ppo);
                    if (a.Status == PromptStatus.None || a.Status == PromptStatus.Keyword) break;
                    if (a.Status != PromptStatus.OK) return null;

                    var b = ed.GetCorner("\nGóc đối diện: ", a.Value);
                    if (b.Status != PromptStatus.OK) return null;

                    var ucs = ed.CurrentUserCoordinateSystem;
                    Point3d p1 = a.Value.TransformBy(ucs), p2 = b.Value.TransformBy(ucs);
                    var f = new Frame
                    {
                        MinX = Math.Min(p1.X, p2.X), MinY = Math.Min(p1.Y, p2.Y),
                        MaxX = Math.Max(p1.X, p2.X), MaxY = Math.Max(p1.Y, p2.Y),
                    };
                    if (f.Width <= 0 || f.Height <= 0) continue;
                    frames.Add(f);
                    overlay.Show(frames, frames);
                    ed.UpdateScreen();
                }
            }

            if (frames.Count == 1)
            {
                // Nhớ vùng này vào cấu hình đang dùng để in lại lần sau.
                profile.Area = AreaMode.Window;
                profile.WindowMinX = frames[0].MinX;
                profile.WindowMinY = frames[0].MinY;
                profile.WindowMaxX = frames[0].MaxX;
                profile.WindowMaxY = frames[0].MaxY;
            }
            return frames.Count == 0 ? null : new AreaResult { Frames = frames };
        }

        // ---------- thứ tự và in ----------

        /// <summary>Hiện số thứ tự ảo, cho đổi thứ tự (hàng, cột, bấm chọn, sửa tay), rồi in.</summary>
        /// <param name="frames">Null: in cả layout / cả bản vẽ.</param>
        private static void ArrangeAndPlot(Document doc, ObjectId layoutId, List<Frame> frames)
        {
            var ed = doc.Editor;
            var order = frames == null ? null : new List<Frame>(frames);

            using (var overlay = new FrameNumberOverlay(doc.Database))
            {
                while (true)
                {
                    if (order != null)
                    {
                        overlay.Show(frames, order);
                        ed.UpdateScreen();
                    }

                    var pko = order != null
                        ? new PromptKeywordOptions("\nThứ tự in [Hang/Cot/Bam/Sua/CaiDat/In/Thoat] <In>: ",
                            "Hang Cot Bam Sua CaiDat In Thoat")
                        : new PromptKeywordOptions("\nIn toàn bộ [CaiDat/In/Thoat] <In>: ", "CaiDat In Thoat");
                    pko.Keywords.Default = "In";
                    pko.AllowNone = true;
                    var pr = ed.GetKeywords(pko);
                    if (pr.Status != PromptStatus.OK && pr.Status != PromptStatus.None) return;

                    var kw = pr.Status == PromptStatus.None ? "In" : pr.StringResult;
                    switch (kw)
                    {
                        case "Hang":
                            order = FrameOrder.ByRows(frames);
                            break;
                        case "Cot":
                            order = FrameOrder.ByColumns(frames);
                            break;
                        case "Bam":
                            order = PickOrder(ed, overlay, frames);
                            if (order == null) return;
                            break;
                        case "Sua":
                            if (!EditOrder(ed, overlay, frames, order)) return;
                            break;
                        case "Thoat":
                            return;
                        case "CaiDat":
                            overlay.Clear();
                            if (!ShowPrintDialog(doc, order?.Count ?? 1)) continue;
                            Plot(doc, layoutId, order);
                            return;
                        default:
                            overlay.Clear();
                            if (!ReadyToPrint(ProfileStore.Current) && !ShowPrintDialog(doc, order?.Count ?? 1)) continue;
                            Plot(doc, layoutId, order);
                            return;
                    }
                }
            }
        }

        /// <summary>Người dùng bấm lần lượt vào các khung; khung chưa bấm được xếp sau theo hàng.</summary>
        private static List<Frame> PickOrder(Editor ed, FrameNumberOverlay overlay, List<Frame> frames)
        {
            var picked = new List<Frame>();
            while (picked.Count < frames.Count)
            {
                overlay.Show(frames, picked);
                ed.UpdateScreen();

                var ppo = new PromptPointOptions(string.Format(
                    "\nBấm vào khung thứ {0} [Xong] <Xong>: ", picked.Count + 1), "Xong");
                ppo.AllowNone = true;
                var r = ed.GetPoint(ppo);
                if (r.Status == PromptStatus.None || r.Status == PromptStatus.Keyword) break;
                if (r.Status != PromptStatus.OK) return null;

                var p = r.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                var f = FrameOrder.At(frames, p.X, p.Y);
                if (f == null) ed.WriteMessage("\nĐiểm bấm không nằm trong khung nào.");
                else if (picked.Contains(f)) ed.WriteMessage("\nKhung này đã có số {0}.", picked.IndexOf(f) + 1);
                else picked.Add(f);
            }

            foreach (var f in FrameOrder.ByRows(frames))
                if (!picked.Contains(f)) picked.Add(f);
            return picked;
        }

        /// <summary>Sửa thủ công: bấm vào một khung, nhập số thứ tự mới, các khung khác dồn theo.</summary>
        private static bool EditOrder(Editor ed, FrameNumberOverlay overlay, List<Frame> frames, List<Frame> order)
        {
            while (true)
            {
                overlay.Show(frames, order);
                ed.UpdateScreen();

                var ppo = new PromptPointOptions("\nBấm vào khung cần đổi số [Xong] <Xong>: ", "Xong");
                ppo.AllowNone = true;
                var r = ed.GetPoint(ppo);
                if (r.Status == PromptStatus.None || r.Status == PromptStatus.Keyword) return true;
                if (r.Status != PromptStatus.OK) return false;

                var p = r.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                var f = FrameOrder.At(frames, p.X, p.Y);
                if (f == null)
                {
                    ed.WriteMessage("\nĐiểm bấm không nằm trong khung nào.");
                    continue;
                }

                var current = order.IndexOf(f) + 1;
                var pio = new PromptIntegerOptions(string.Format(
                    "\nSố mới cho khung {0} (1-{1}) <{0}>: ", current, order.Count))
                {
                    LowerLimit = 1,
                    UpperLimit = order.Count,
                    AllowNone = true,
                    AllowZero = false,
                    AllowNegative = false,
                };
                var ir = ed.GetInteger(pio);
                if (ir.Status == PromptStatus.None) continue;
                if (ir.Status != PromptStatus.OK) return false;

                FrameOrder.MoveTo(order, f, ir.Value);
            }
        }

        /// <summary>Cấu hình đã có máy in dùng được chưa (để in ngay không cần hộp thoại).</summary>
        private static bool ReadyToPrint(PrintProfile p)
        {
            if (string.IsNullOrEmpty(p.Device)) return false;
            foreach (string d in PlotSettingsValidator.Current.GetPlotDeviceList())
                if (string.Equals(d, p.Device, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Hộp thoại máy in, khổ giấy, tỉ lệ, màu nét in. True nếu người dùng bấm In.</summary>
        private static bool ShowPrintDialog(Document doc, int pageCount)
        {
            using (var ps = CurrentPageSetup(doc.Database))
            using (var form = new PrintSetupForm(PrintSetupForm.Mode.Print, ps, doc.Database.PlotStyleMode, pageCount))
            {
                if (Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form) != WinForms.DialogResult.OK) return false;
                ProfileStore.Current = form.Result;
                return true;
            }
        }

        private static void Plot(Document doc, ObjectId layoutId, List<Frame> order)
        {
            var ed = doc.Editor;
            var profile = ProfileStore.Current;

            string layoutName;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                layoutName = ((Layout)tr.GetObject(layoutId, OpenMode.ForRead)).LayoutName;
                tr.Commit();
            }

            string output = null;
            if (PrintProfile.IsFileDevice(profile.Device, out var ext))
            {
                output = OutputFile(doc.Name, profile.OutputFolder, layoutName, ext);
                if (File.Exists(output))
                {
                    var pko = new PromptKeywordOptions("\nFile " + output + " đã có. Ghi đè? [Yes/No] <Yes>: ", "Yes No");
                    pko.Keywords.Default = "Yes";
                    pko.AllowNone = true;
                    var r = ed.GetKeywords(pko);
                    if ((r.Status != PromptStatus.OK && r.Status != PromptStatus.None) || r.StringResult == "No") return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(output));
            }

            var pages = new List<PlotPage>();
            if (order == null) pages.Add(new PlotPage { LayoutId = layoutId });
            else foreach (var f in order) pages.Add(new PlotPage { LayoutId = layoutId, Frame = f });

            try
            {
                // Ghi khung nhìn Model hiện tại vào bản vẽ để tính đúng cửa sổ in.
                if (doc.Database.TileMode) ed.UpdateTiledViewportsInDatabase();

                var done = FramePlotter.Plot(doc, pages, profile, output);
                if (done < 0) ed.WriteMessage("\nĐã hủy in.");
                else if (output != null) ed.WriteMessage("\nĐã in {0} trang ra {1}\n", done, output);
                else ed.WriteMessage("\nĐã gửi {0} trang tới {1}.\n", done, profile.Device);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nLỗi khi in: {0}\n", ex.Message);
            }
        }

        /// <summary>File đầu ra: thư mục của cấu hình (hoặc cạnh bản vẽ), tên "bản vẽ-layout.ext".</summary>
        public static string OutputFile(string dwgPath, string folder, string suffix, string ext)
        {
            if (string.IsNullOrEmpty(folder))
                folder = Path.IsPathRooted(dwgPath)
                    ? Path.GetDirectoryName(dwgPath)
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var name = Path.GetFileNameWithoutExtension(dwgPath) + (string.IsNullOrEmpty(suffix) ? "" : "-" + suffix);
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return Path.Combine(folder, name + "." + (string.IsNullOrEmpty(ext) ? "pdf" : ext));
        }

        /// <summary>Bản sao page setup của layout hiện hành (dùng cho hộp thoại).</summary>
        private static PlotSettings CurrentPageSetup(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var layout = (Layout)tr.GetObject(space.LayoutId, OpenMode.ForRead);
                var ps = new PlotSettings(layout.ModelType);
                ps.CopyFrom(layout);
                tr.Commit();
                return ps;
            }
        }
    }
}

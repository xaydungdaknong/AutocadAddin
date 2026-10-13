using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using WinForms = System.Windows.Forms;

[assembly: CommandClass(typeof(DwgToDxf.Printing.PrintCommands))]

namespace DwgToDxf.Printing
{
    public class PrintCommands
    {
        /// <summary>INMODEL: chọn các khung trong Model, xem và đổi thứ tự in, in ra PDF hoặc máy in.</summary>
        [CommandMethod("INMODEL", CommandFlags.Modal)]
        public void PrintModel()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            // Đang ở layout thì chuyển sang tab Model.
            if (Convert.ToInt16(AcApp.GetSystemVariable("TILEMODE")) == 0)
                LayoutManager.Current.CurrentLayout = "Model";

            var layoutId = CurrentLayoutId(doc.Database);

            var frames = SelectFrames(doc, allowWholeLayout: false);
            if (frames == null) return;
            if (frames.Count == 0)
            {
                ed.WriteMessage("\nKhông tìm thấy khung nào.");
                return;
            }

            ArrangeAndPlot(doc, layoutId, frames);
        }

        /// <summary>
        /// INLAYOUT: in layout hiện hành. Layout chỉ có một khung giấy thì nhấn Enter để in cả layout
        /// theo page setup; layout có nhiều khung thì chọn khung mẫu như INMODEL.
        /// </summary>
        [CommandMethod("INLAYOUT", CommandFlags.Modal | CommandFlags.NoTileMode)]
        public void PrintLayout()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            // Đang đứng trong viewport (MSPACE) thì chuyển ra giấy để chọn khung.
            if (Convert.ToInt16(AcApp.GetSystemVariable("CVPORT")) != 1)
                ed.SwitchToPaperSpace();

            var layoutId = CurrentLayoutId(doc.Database);

            var frames = SelectFrames(doc, allowWholeLayout: true);
            if (frames == null) return;

            if (frames.Count <= 1)
            {
                // Một khung giấy (hoặc nhấn Enter): in thẳng cả layout theo page setup.
                if (frames.Count == 1)
                    ed.WriteMessage("\nLayout chỉ có một khung, in cả layout.");
                PlotWithOutputChoice(doc, layoutId, new List<Frame>());
                return;
            }

            ArrangeAndPlot(doc, layoutId, frames);
        }

        /// <returns>Null nếu hủy. Danh sách rỗng nếu người dùng chọn in cả layout.</returns>
        private static List<Frame> SelectFrames(Document doc, bool allowWholeLayout)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            var peo = new PromptEntityOptions(allowWholeLayout
                ? "\nChọn một khung mẫu (block hoặc polyline chữ nhật) [CaLayout] <CaLayout>: "
                : "\nChọn một khung mẫu (block hoặc polyline chữ nhật): ");
            if (allowWholeLayout)
            {
                peo.Keywords.Add("CaLayout");
                peo.Keywords.Default = "CaLayout";
                peo.AllowNone = true;
            }

            ObjectId sampleId;
            while (true)
            {
                var per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.None || per.Status == PromptStatus.Keyword) return new List<Frame>();
                if (per.Status != PromptStatus.OK) return null;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var why = FrameFinder.CheckSample(tr, per.ObjectId);
                    tr.Commit();
                    if (why == null)
                    {
                        sampleId = per.ObjectId;
                        break;
                    }
                    ed.WriteMessage("\n" + why);
                }
            }

            // Chọn vùng chứa các khung, Enter = cả không gian hiện hành.
            var pso = new PromptSelectionOptions
            {
                MessageForAdding = "\nChọn vùng chứa các khung cần in <Tất cả>: ",
            };
            var psr = ed.GetSelection(pso);
            IEnumerable<ObjectId> candidates;
            if (psr.Status == PromptStatus.OK) candidates = psr.Value.GetObjectIds();
            else if (psr.Status == PromptStatus.Cancel) return null;
            else candidates = null;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var frames = FrameFinder.Find(tr, db, sampleId, candidates);
                tr.Commit();
                ed.WriteMessage("\nTìm thấy {0} khung.", frames.Count);
                return frames;
            }
        }

        /// <summary>Hiện số thứ tự ảo, cho đổi thứ tự (hàng, cột, bấm chọn, sửa tay), rồi in.</summary>
        private static bool ArrangeAndPlot(Document doc, ObjectId layoutId, List<Frame> frames)
        {
            var ed = doc.Editor;
            var order = FrameOrder.ByRows(frames);

            using (var overlay = new FrameNumberOverlay(doc.Database))
            {
                while (true)
                {
                    overlay.Show(frames, order);
                    ed.UpdateScreen();

                    var pko = new PromptKeywordOptions(
                        "\nThứ tự in [Hang/Cot/Bam/Sua/In/Thoat] <In>: ", "Hang Cot Bam Sua In Thoat");
                    pko.Keywords.Default = "In";
                    pko.AllowNone = true;
                    var pr = ed.GetKeywords(pko);
                    if (pr.Status != PromptStatus.OK && pr.Status != PromptStatus.None) return false;

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
                            if (order == null) return false;
                            break;
                        case "Sua":
                            if (!EditOrder(ed, overlay, frames, order)) return false;
                            break;
                        case "Thoat":
                            return false;
                        default:
                            overlay.Clear();
                            ed.UpdateScreen();
                            return PlotWithOutputChoice(doc, layoutId, order);
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

            picked.AddRange(FrameOrder.ByRows(frames.Except(picked)));
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

        private static bool PlotWithOutputChoice(Document doc, ObjectId layoutId, List<Frame> order)
        {
            var ed = doc.Editor;

            var pko = new PromptKeywordOptions("\nIn ra [Pdf/Mayin] <Pdf>: ", "Pdf Mayin");
            pko.Keywords.Default = "Pdf";
            pko.AllowNone = true;
            var pr = ed.GetKeywords(pko);
            if (pr.Status != PromptStatus.OK && pr.Status != PromptStatus.None) return false;
            var toPdf = pr.StringResult != "Mayin";

            string pdf = null;
            if (toPdf)
            {
                pdf = AskPdfFile(doc, layoutId);
                if (pdf == null) return false;
            }

            try
            {
                var pages = FramePlotter.Plot(doc, new PlotJob
                {
                    LayoutId = layoutId,
                    Frames = order,
                    ToPdf = toPdf,
                    PdfFile = pdf,
                });

                if (pages < 0) ed.WriteMessage("\nĐã hủy in.");
                else if (toPdf) ed.WriteMessage("\nĐã in {0} trang ra {1}\n", pages, pdf);
                else ed.WriteMessage("\nĐã gửi {0} trang tới máy in.\n", pages);
                return pages >= 0;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nLỗi khi in: {0}\n", ex.Message);
                return false;
            }
        }

        private static string AskPdfFile(Document doc, ObjectId layoutId)
        {
            string layoutName;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                layoutName = ((Layout)tr.GetObject(layoutId, OpenMode.ForRead)).LayoutName;
                tr.Commit();
            }

            var dwg = doc.Name;
            var folder = Path.IsPathRooted(dwg) ? Path.GetDirectoryName(dwg) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var name = Path.GetFileNameWithoutExtension(dwg) + "-" + layoutName + ".pdf";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');

            using (var dlg = new WinForms.SaveFileDialog())
            {
                dlg.Title = "Lưu file PDF";
                dlg.Filter = "PDF (*.pdf)|*.pdf";
                dlg.InitialDirectory = folder;
                dlg.FileName = name;
                dlg.OverwritePrompt = true;
                return dlg.ShowDialog() == WinForms.DialogResult.OK ? dlg.FileName : null;
            }
        }

        private static ObjectId CurrentLayoutId(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var id = space.LayoutId;
                tr.Commit();
                return id;
            }
        }
    }
}

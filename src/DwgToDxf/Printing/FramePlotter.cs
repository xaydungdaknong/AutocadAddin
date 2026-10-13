using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace DwgToDxf.Printing
{
    public class PlotJob
    {
        /// <summary>Layout dùng làm page setup (Model hoặc layout giấy).</summary>
        public ObjectId LayoutId { get; set; }

        /// <summary>Các khung in theo thứ tự. Rỗng: in cả layout theo page setup.</summary>
        public List<Frame> Frames { get; set; } = new List<Frame>();

        public PlotOptions Options { get; set; }
    }

    /// <summary>
    /// In nhiều khung trong một lần in (một file PDF nhiều trang, hoặc một lệnh in gửi máy in).
    /// Bắt đầu từ page setup của layout, đè máy in, khổ giấy, tỉ lệ, bảng nét in theo PlotOptions.
    /// Mỗi khung in kiểu Window, căn giữa, tự xoay giấy theo chiều khung.
    /// </summary>
    public static class FramePlotter
    {
        public const string PdfDevice = "DWG To PDF.pc3";

        /// <returns>Số trang đã in, -1 nếu người dùng hủy.</returns>
        public static int Plot(Document doc, PlotJob job)
        {
            if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
                throw new InvalidOperationException("AutoCAD đang in một bản vẽ khác, hãy thử lại sau.");

            var db = doc.Database;
            var ed = doc.Editor;
            var oldBackground = AcApp.GetSystemVariable("BACKGROUNDPLOT");
            AcApp.SetSystemVariable("BACKGROUNDPLOT", 0);
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var layout = (Layout)tr.GetObject(job.LayoutId, OpenMode.ForRead);
                    var pages = job.Frames.Count > 0
                        ? job.Frames.Select(f => Settings(ed, layout, job.Options, f)).ToList()
                        : new List<PlotSettings> { Settings(ed, layout, job.Options, null) };

                    var done = Run(layout, pages, job.Options.OutputFile, doc.Name);
                    foreach (var p in pages) p.Dispose();
                    tr.Commit();
                    return done;
                }
            }
            finally
            {
                AcApp.SetSystemVariable("BACKGROUNDPLOT", oldBackground);
            }
        }

        private static PlotSettings Settings(Editor ed, Layout layout, PlotOptions o, Frame frame)
        {
            var ps = new PlotSettings(layout.ModelType);
            ps.CopyFrom(layout);

            var psv = PlotSettingsValidator.Current;
            psv.SetPlotConfigurationName(ps, o.Device, null);
            psv.RefreshLists(ps);
            psv.SetCanonicalMediaName(ps, PickMedia(psv, ps, o.Media ?? layout.CanonicalMediaName));
            if (o.StyleSheet != null) psv.SetCurrentStyleSheet(ps, o.StyleSheet);

            if (frame != null)
            {
                psv.SetPlotWindowArea(ps, ToDcs(ed, frame));
                psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
            }

            switch (o.Scale)
            {
                case PlotOptions.ScaleMode.Fit:
                    psv.SetUseStandardScale(ps, true);
                    psv.SetStdScaleType(ps, StdScaleType.ScaleToFit);
                    break;
                case PlotOptions.ScaleMode.Custom:
                    // 1 mm trên giấy = N đơn vị bản vẽ.
                    psv.SetPlotPaperUnits(ps, PlotPaperUnit.Millimeters);
                    psv.SetUseStandardScale(ps, false);
                    psv.SetCustomPrintScale(ps, new CustomScale(1, o.ScaleDenominator));
                    break;
            }

            if (frame == null) return ps; // in cả layout: giữ kiểu in, hướng giấy của page setup

            psv.SetPlotCentered(ps, true);

            // Xoay giấy cho cùng chiều với khung (ngang/đứng).
            var paper = ps.PlotPaperSize;
            var paperLandscape = paper.X >= paper.Y;
            var frameLandscape = frame.Width >= frame.Height;
            psv.SetPlotRotation(ps, paperLandscape == frameLandscape ? PlotRotation.Degrees000 : PlotRotation.Degrees090);
            return ps;
        }

        /// <summary>Khổ giấy đã chọn nếu máy in có, nếu không thì ưu tiên A3 (full bleed).</summary>
        private static string PickMedia(PlotSettingsValidator psv, PlotSettings ps, string wanted)
        {
            var list = psv.GetCanonicalMediaNameList(ps).Cast<string>().ToList();
            if (list.Count == 0) return null;
            return list.FirstOrDefault(m => string.Equals(m, wanted, StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(m => m.IndexOf("full_bleed_A3", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? list.FirstOrDefault(m => m.IndexOf("A3", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? list[0];
        }

        /// <summary>Cửa sổ in tính trong hệ tọa độ màn hình (DCS) của khung nhìn hiện hành.</summary>
        private static Extents2d ToDcs(Editor ed, Frame f)
        {
            Matrix3d worldToEye;
            using (var view = ed.GetCurrentView())
            {
                worldToEye = (Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target) *
                              Matrix3d.Displacement(view.Target - Point3d.Origin) *
                              Matrix3d.PlaneToWorld(view.ViewDirection)).Inverse();
            }

            var pts = new[]
            {
                new Point3d(f.MinX, f.MinY, 0), new Point3d(f.MaxX, f.MinY, 0),
                new Point3d(f.MaxX, f.MaxY, 0), new Point3d(f.MinX, f.MaxY, 0),
            }.Select(p => p.TransformBy(worldToEye)).ToList();

            return new Extents2d(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
        }

        private static int Run(Layout layout, List<PlotSettings> pages, string outputFile, string docName)
        {
            var count = pages.Count;
            using (var pe = PlotFactory.CreatePublishEngine())
            using (var ppd = new PlotProgressDialog(false, count, true))
            {
                ppd.set_PlotMsgString(PlotMessageIndex.DialogTitle, "Đang in " + count + " trang");
                ppd.set_PlotMsgString(PlotMessageIndex.CancelJobButtonMessage, "Hủy cả lệnh in");
                ppd.set_PlotMsgString(PlotMessageIndex.CancelSheetButtonMessage, "Hủy trang này");
                ppd.set_PlotMsgString(PlotMessageIndex.SheetSetProgressCaption, "Tiến độ");
                ppd.set_PlotMsgString(PlotMessageIndex.SheetProgressCaption, "Trang hiện tại");
                ppd.LowerPlotProgressRange = 0;
                ppd.UpperPlotProgressRange = 100;
                ppd.PlotProgressPos = 0;
                ppd.OnBeginPlot();
                ppd.IsVisible = true;
                pe.BeginPlot(ppd, null);

                for (var i = 0; i < count; i++)
                {
                    using (var pi = new PlotInfo { Layout = layout.ObjectId, OverrideSettings = pages[i] })
                    using (var piv = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled })
                    using (var ppi = new PlotPageInfo())
                    {
                        piv.Validate(pi);

                        if (i == 0)
                            pe.BeginDocument(pi, docName, null, 1, outputFile != null, outputFile);

                        ppd.set_PlotMsgString(PlotMessageIndex.Status, "Trang " + (i + 1) + "/" + count);
                        ppd.OnBeginSheet();
                        ppd.LowerSheetProgressRange = 0;
                        ppd.UpperSheetProgressRange = 100;
                        ppd.SheetProgressPos = 0;

                        pe.BeginPage(ppi, pi, i == count - 1, null);
                        pe.BeginGenerateGraphics(null);
                        ppd.SheetProgressPos = 50;
                        pe.EndGenerateGraphics(null);
                        pe.EndPage(null);
                        ppd.SheetProgressPos = 100;
                        ppd.OnEndSheet();
                        ppd.PlotProgressPos = (i + 1) * 100 / count;
                    }
                }

                pe.EndDocument(null);
                ppd.PlotProgressPos = 100;
                ppd.OnEndPlot();
                pe.EndPlot(null);
                return ppd.IsPlotCancelled ? -1 : count;
            }
        }
    }
}

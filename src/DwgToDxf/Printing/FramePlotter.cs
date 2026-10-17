using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
#if ACAD2010
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
#endif

namespace DwgToDxf.Printing
{
    /// <summary>Một trang in: một khung (hoặc vùng) trong một layout, hoặc cả layout nếu Frame = null.</summary>
    public class PlotPage
    {
        public ObjectId LayoutId { get; set; }
        public Frame Frame { get; set; }
    }

    /// <summary>
    /// In nhiều trang trong một lần in: một file nhiều trang (PDF, DWF) hoặc một lệnh in gửi máy in.
    /// Các trang có thể thuộc nhiều layout của cùng một bản vẽ.
    /// Mỗi trang bắt đầu từ page setup của layout, đè máy in, khổ giấy, tỉ lệ, bảng nét in theo cấu hình in.
    /// Khung/vùng in kiểu Window, căn giữa, tự xoay giấy theo chiều khung. Page setup trong bản vẽ không bị đổi.
    /// </summary>
    public static class FramePlotter
    {
        private sealed class PageSettings
        {
            public Layout Layout;
            public PlotSettings Settings;
        }

        public const string PdfDevice = "DWG To PDF.pc3";

        /// <param name="outputFile">File đầu ra khi máy in chỉ in ra file. Null: gửi tới máy in.</param>
        /// <returns>Số trang đã in, -1 nếu người dùng hủy.</returns>
        public static int Plot(Document doc, IList<PlotPage> pages, PrintProfile profile, string outputFile)
        {
            if (pages.Count == 0) return 0;
            if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
                throw new InvalidOperationException("AutoCAD đang in một bản vẽ khác, hãy thử lại sau.");
            if (string.IsNullOrEmpty(profile.Device))
                throw new InvalidOperationException("Cấu hình in chưa chọn máy in.");

            var db = doc.Database;
            var oldBackground = AcApp.GetSystemVariable("BACKGROUNDPLOT");
            AcApp.SetSystemVariable("BACKGROUNDPLOT", 0);
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var modelToDcs = ModelWorldToDcs(tr, db);
                    var settings = new List<PageSettings>();
                    try
                    {
                        foreach (var page in pages)
                        {
                            var layout = (Layout)tr.GetObject(page.LayoutId, OpenMode.ForRead);
                            settings.Add(new PageSettings { Layout = layout, Settings = Settings(layout, profile, page.Frame, modelToDcs) });
                        }
                        var done = Run(settings, outputFile, doc.Name);
                        tr.Commit();
                        return done;
                    }
                    finally
                    {
                        foreach (var s in settings) s.Settings.Dispose();
                    }
                }
            }
            finally
            {
                AcApp.SetSystemVariable("BACKGROUNDPLOT", oldBackground);
            }
        }

        private static PlotSettings Settings(Layout layout, PrintProfile o, Frame frame, Matrix3d modelToDcs)
        {
            var ps = new PlotSettings(layout.ModelType);
            ps.CopyFrom(layout);

            var psv = PlotSettingsValidator.Current;
            psv.SetPlotConfigurationName(ps, o.Device, null);
            psv.RefreshLists(ps);
            var media = PickMedia(psv, ps, o.Media ?? layout.CanonicalMediaName);
            if (media != null) psv.SetCanonicalMediaName(ps, media);
            if (!string.IsNullOrEmpty(o.StyleSheet)) psv.SetCurrentStyleSheet(ps, o.StyleSheet);

            if (frame != null)
            {
                psv.SetPlotWindowArea(ps, ToDcs(frame, layout.ModelType ? modelToDcs : Matrix3d.Identity));
                psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
            }
            else
            {
                psv.SetPlotType(ps, layout.ModelType
                    ? Autodesk.AutoCAD.DatabaseServices.PlotType.Extents
                    : Autodesk.AutoCAD.DatabaseServices.PlotType.Layout);
            }

            if (o.Scale == ScaleMode.Custom && o.ScaleDenominator > 0)
            {
                // 1 mm trên giấy = N đơn vị bản vẽ.
                psv.SetPlotPaperUnits(ps, PlotPaperUnit.Millimeters);
                psv.SetUseStandardScale(ps, false);
                psv.SetCustomPrintScale(ps, new CustomScale(1, o.ScaleDenominator));
            }
            else
            {
                psv.SetUseStandardScale(ps, true);
                psv.SetStdScaleType(ps, StdScaleType.ScaleToFit);
            }

            psv.SetPlotCentered(ps, true);

            // Tự xoay giấy cho cùng chiều với vùng in: khung ngang in giấy ngang, khung dọc in giấy dọc.
            // In cả bản vẽ trong Model thì xét theo extents; in cả layout thì giữ hướng của page setup.
            var shape = frame ?? (layout.ModelType ? ModelExtents(layout.Database) : null);
            if (shape != null)
            {
                var paper = ps.PlotPaperSize;
                var paperLandscape = paper.X >= paper.Y;
                var frameLandscape = shape.Width >= shape.Height;
                psv.SetPlotRotation(ps, paperLandscape == frameLandscape ? PlotRotation.Degrees000 : PlotRotation.Degrees090);
            }
            return ps;
        }

        /// <summary>Extents của Model, hoặc null nếu bản vẽ trống.</summary>
        private static Frame ModelExtents(Database db)
        {
            var f = new Frame { MinX = db.Extmin.X, MinY = db.Extmin.Y, MaxX = db.Extmax.X, MaxY = db.Extmax.Y };
            return f.Width > 0 && f.Height > 0 && f.Width < 1e19 ? f : null;
        }

        /// <summary>Khổ giấy đã chọn nếu máy in có, nếu không thì ưu tiên A3 (full bleed).</summary>
        public static string PickMedia(PlotSettingsValidator psv, PlotSettings ps, string wanted)
        {
            var list = psv.GetCanonicalMediaNameList(ps).Cast<string>().ToList();
            if (list.Count == 0) return null;
            return list.FirstOrDefault(m => string.Equals(m, wanted, StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(m => m.IndexOf("full_bleed_A3", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? list.FirstOrDefault(m => m.IndexOf("A3", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? list[0];
        }

        /// <summary>
        /// Cửa sổ in trong Model tính theo hệ tọa độ màn hình (DCS) của khung nhìn Model đang hoạt động.
        /// Với mặt bằng thông thường (nhìn từ trên xuống, không xoay) DCS chỉ lệch WCS một đoạn bằng target.
        /// </summary>
        private static Matrix3d ModelWorldToDcs(Transaction tr, Database db)
        {
            var vpt = (ViewportTable)tr.GetObject(db.ViewportTableId, OpenMode.ForRead);
            if (!vpt.Has("*Active")) return Matrix3d.Identity;
            var vp = (ViewportTableRecord)tr.GetObject(vpt["*Active"], OpenMode.ForRead);
            var eyeToWorld = Matrix3d.Rotation(-vp.ViewTwist, vp.ViewDirection, vp.Target) *
                             Matrix3d.Displacement(vp.Target - Point3d.Origin) *
                             Matrix3d.PlaneToWorld(vp.ViewDirection);
            return eyeToWorld.Inverse();
        }

        private static Extents2d ToDcs(Frame f, Matrix3d worldToDcs)
        {
            var pts = new[]
            {
                new Point3d(f.MinX, f.MinY, 0), new Point3d(f.MaxX, f.MinY, 0),
                new Point3d(f.MaxX, f.MaxY, 0), new Point3d(f.MinX, f.MaxY, 0),
            }.Select(p => p.TransformBy(worldToDcs)).ToList();

            return new Extents2d(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
        }

        private static int Run(List<PageSettings> pages, string outputFile, string docName)
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
                    using (var pi = new PlotInfo { Layout = pages[i].Layout.ObjectId, OverrideSettings = pages[i].Settings })
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

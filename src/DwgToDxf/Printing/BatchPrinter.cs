using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
#if ACAD2010
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
#endif

namespace DwgToDxf.Printing
{
    /// <summary>
    /// In mọi bản vẽ trong một thư mục theo một cấu hình in.
    /// Mỗi bản vẽ được mở (chỉ đọc), tìm khung/vùng in trong Model và/hoặc các layout, in ra một file
    /// nhiều trang (PDF: "tên bản vẽ.pdf") hoặc gửi tới máy in, rồi đóng lại không lưu.
    /// Bản vẽ đang mở sẵn được in luôn và giữ nguyên.
    /// </summary>
    public static class BatchPrinter
    {
        /// <returns>Dòng tóm tắt kết quả.</returns>
        public static string Run(PrintProfile p, Action<string> say)
        {
            if (string.IsNullOrEmpty(p.BatchFolder) || !Directory.Exists(p.BatchFolder))
                return "Chưa chọn thư mục bản vẽ. Mở CAIDATIN để chọn.";
            if (string.IsNullOrEmpty(p.Device))
                return "Cấu hình in chưa chọn máy in. Mở CAIDATIN để chọn.";
            if (p.Area == AreaMode.Frame && !p.HasFrameRule)
                return "Cấu hình in chưa có khung mẫu. Mở CAIDATIN, bấm \"Chọn khung mẫu...\".";

            var files = Directory.GetFiles(p.BatchFolder, "*.dwg",
                    p.BatchSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => string.Equals(Path.GetExtension(f), ".dwg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (files.Count == 0) return "Không có file DWG nào trong " + p.BatchFolder + ".";

            var toFile = PrintProfile.IsFileDevice(p.Device, out var ext);
            var log = new StringBuilder();
            log.AppendLine("In nhiều file " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            log.AppendLine("Cấu hình: " + p.Name + " | " + p.AreaDescription() + " | " + p.Device);
            log.AppendLine("Thư mục: " + p.BatchFolder);
            log.AppendLine();

            int ok = 0, failed = 0, pagesTotal = 0;
            var cancelled = false;
            for (var i = 0; i < files.Count && !cancelled; i++)
            {
                var file = files[i];
                say?.Invoke(string.Format("\n[{0}/{1}] {2}", i + 1, files.Count, Path.GetFileName(file)));
                try
                {
                    var output = toFile ? OutputFor(file, p, ext) : null;
                    var pages = PrintOne(file, p, output);
                    if (pages < 0)
                    {
                        cancelled = true;
                        log.AppendLine("HỦY   " + file);
                    }
                    else if (pages == 0)
                    {
                        log.AppendLine("BỎ QUA " + file + " (không có khung/vùng nào để in)");
                    }
                    else
                    {
                        ok++;
                        pagesTotal += pages;
                        log.AppendLine("OK    " + file + " | " + pages + " trang" + (output != null ? " -> " + output : ""));
                    }
                }
                catch (System.Exception ex)
                {
                    failed++;
                    log.AppendLine("LỖI   " + file + " | " + ex.Message);
                    say?.Invoke(" lỗi: " + ex.Message);
                }
            }

            var summary = string.Format("Đã in {0}/{1} bản vẽ, {2} trang.{3}{4}", ok, files.Count, pagesTotal,
                failed > 0 ? " " + failed + " file lỗi." : "", cancelled ? " Đã dừng theo yêu cầu." : "");
            log.AppendLine();
            log.AppendLine(summary);

            var logFolder = !string.IsNullOrEmpty(p.OutputFolder) ? p.OutputFolder : p.BatchFolder;
            try
            {
                Directory.CreateDirectory(logFolder);
                var logFile = Path.Combine(logFolder, "InNhieuFile_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
                File.WriteAllText(logFile, log.ToString(), Encoding.UTF8);
                summary += " Nhật ký: " + logFile;
            }
            catch (System.Exception)
            {
                // Không ghi được nhật ký (thư mục chỉ đọc): vẫn báo kết quả ở dòng lệnh.
            }
            return summary;
        }

        /// <summary>
        /// File đầu ra khi in nhiều file: thư mục đầu ra giữ cấu trúc thư mục con của thư mục nguồn,
        /// hoặc cạnh bản vẽ nếu không chọn thư mục đầu ra.
        /// </summary>
        private static string OutputFor(string dwg, PrintProfile p, string ext)
        {
            string folder = null;
            if (!string.IsNullOrEmpty(p.OutputFolder))
            {
                var rel = Path.GetDirectoryName(dwg).Substring(p.BatchFolder.TrimEnd('\\', '/').Length).TrimStart('\\', '/');
                folder = Path.Combine(p.OutputFolder, rel);
                Directory.CreateDirectory(folder);
            }
            return PrintCommands.OutputFile(dwg, folder, null, ext);
        }

        /// <returns>Số trang đã in, 0 nếu không có gì để in, -1 nếu người dùng hủy.</returns>
        private static int PrintOne(string file, PrintProfile p, string output)
        {
            var dm = AcApp.DocumentManager;
            var doc = dm.Cast<Document>().FirstOrDefault(d =>
                string.Equals(Path.GetFullPath(d.Name), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));
            var opened = doc == null;
            if (opened) doc = dm.Open(file, true);
            else dm.MdiActiveDocument = doc;

            try
            {
                using (doc.LockDocument())
                {
                    var pages = Pages(doc.Database, p);
                    if (pages.Count == 0) return 0;
                    return FramePlotter.Plot(doc, pages, p, output);
                }
            }
            finally
            {
                if (opened) doc.CloseAndDiscard();
            }
        }

        /// <summary>Các trang cần in của một bản vẽ: Model trước, sau đó các layout theo thứ tự tab.</summary>
        public static List<PlotPage> Pages(Database db, PrintProfile p)
        {
            var pages = new List<PlotPage>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layouts = ((DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead))
                    .Cast<DBDictionaryEntry>()
                    .Select(e => (Layout)tr.GetObject(e.Value, OpenMode.ForRead))
                    .OrderBy(l => l.TabOrder)
                    .ToList();

                foreach (var layout in layouts)
                {
                    if (layout.ModelType ? p.BatchSpaces == BatchSpaces.Layouts : p.BatchSpaces == BatchSpaces.Model) continue;
                    if (!layout.ModelType && IsEmptyLayout(tr, layout)) continue;

                    switch (p.Area)
                    {
                        case AreaMode.Whole:
                            pages.Add(new PlotPage { LayoutId = layout.ObjectId });
                            break;
                        case AreaMode.Window:
                            pages.Add(new PlotPage { LayoutId = layout.ObjectId, Frame = p.WindowFrame() });
                            break;
                        default:
                            var frames = FrameFinder.Find(tr, layout.BlockTableRecordId, p.FrameBlock, p.FrameLayer, null);
                            if (frames.Count >= 2 || (layout.ModelType && frames.Count == 1))
                                pages.AddRange(FrameOrder.Sort(frames, p.Order)
                                    .Select(f => new PlotPage { LayoutId = layout.ObjectId, Frame = f }));
                            else if (!layout.ModelType)
                                pages.Add(new PlotPage { LayoutId = layout.ObjectId }); // layout một khung: in cả layout
                            break;
                    }
                }
                tr.Commit();
            }
            return pages;
        }

        /// <summary>Layout chưa vẽ gì: chỉ có viewport giấy mặc định (hoặc trống hẳn).</summary>
        private static bool IsEmptyLayout(Transaction tr, Layout layout)
        {
            var btr = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
            var viewports = 0;
            foreach (ObjectId id in btr)
            {
                if (id.ObjectClass.DxfName != "VIEWPORT") return false;
                if (++viewports > 1) return false;
            }
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace DwgToDxf
{
    /// <summary>Tùy chọn cho một lần chuyển đổi hàng loạt.</summary>
    public class ConvertOptions
    {
        public string SourceFolder { get; set; }

        /// <summary>Thư mục đích. Null hoặc rỗng: ghi DXF cạnh file DWG gốc.</summary>
        public string OutputFolder { get; set; }

        public bool IncludeSubfolders { get; set; }

        public DwgVersion Version { get; set; } = DwgVersion.AC1032; // DXF 2018

        /// <summary>Audit, bind xref, phá block, xóa rác, purge trước khi xuất.</summary>
        public bool Clean { get; set; } = true;

        /// <summary>Số chữ số thập phân khi ghi DXF (0..16).</summary>
        public int Precision { get; set; } = 16;
    }

    public class ConvertResult
    {
        public int Total { get; set; }
        public int Succeeded { get; set; }
        public List<string> Failed { get; } = new List<string>();
        public bool Cancelled { get; set; }
        public string LogFile { get; set; }
    }

    /// <summary>
    /// Đọc từng file DWG vào một Database phụ (không mở lên màn hình)
    /// rồi ghi ra DXF bằng Database.DxfOut.
    /// </summary>
    public static class DxfConverter
    {
        public static ConvertResult Run(ConvertOptions options)
        {
            var result = new ConvertResult();
            var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;

            var search = options.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.EnumerateFiles(options.SourceFolder, "*.dwg", search)
                .Where(f => string.Equals(Path.GetExtension(f), ".dwg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            result.Total = files.Count;

            var logFolder = string.IsNullOrWhiteSpace(options.OutputFolder) ? options.SourceFolder : options.OutputFolder;
            Directory.CreateDirectory(logFolder);
            result.LogFile = Path.Combine(logFolder, "DWG2DXF_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");

            var log = new StringBuilder();
            log.AppendLine("DWG -> DXF  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            log.AppendLine("Nguon: " + options.SourceFolder + (options.IncludeSubfolders ? " (gom thu muc con)" : ""));
            log.AppendLine("Dich : " + (string.IsNullOrWhiteSpace(options.OutputFolder) ? "(cung thu muc voi file goc)" : options.OutputFolder));
            log.AppendLine("Phien ban DXF: " + VersionName(options.Version));
            log.AppendLine("Lam sach truoc: " + (options.Clean ? "co" : "khong"));
            log.AppendLine("So file: " + files.Count);
            log.AppendLine();

            if (files.Count == 0)
            {
                File.WriteAllText(result.LogFile, log.ToString(), Encoding.UTF8);
                return result;
            }

            var meter = new ProgressMeter();
            meter.SetLimit(files.Count);
            meter.Start("Dang chuyen DWG sang DXF...");
            try
            {
                foreach (var src in files)
                {
                    if (HostApplicationServices.Current.UserBreak())
                    {
                        result.Cancelled = true;
                        log.AppendLine("Nguoi dung da huy (Esc).");
                        break;
                    }

                    var dst = GetOutputPath(src, options);
                    try
                    {
                        var steps = new List<string>();
                        ConvertOne(src, dst, options.Version, options.Precision, options.Clean, steps);
                        result.Succeeded++;
                        log.AppendLine("OK    " + src + " -> " + dst);
                        foreach (var step in steps)
                            log.AppendLine("        " + step);
                    }
                    catch (System.Exception ex)
                    {
                        result.Failed.Add(src);
                        log.AppendLine("LOI   " + src + " : " + ex.Message);
                        ed?.WriteMessage("\nLoi: {0} ({1})", Path.GetFileName(src), ex.Message);
                    }
                    meter.MeterProgress();
                    System.Windows.Forms.Application.DoEvents();
                }
            }
            finally
            {
                meter.Stop();
                meter.Dispose();
            }

            log.AppendLine();
            log.AppendLine(string.Format("Thanh cong: {0}/{1}, loi: {2}", result.Succeeded, result.Total, result.Failed.Count));
            File.WriteAllText(result.LogFile, log.ToString(), Encoding.UTF8);
            return result;
        }

        public static void ConvertOne(string dwgPath, string dxfPath, DwgVersion version, int precision, bool clean, IList<string> steps)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dxfPath));

            var previous = HostApplicationServices.WorkingDatabase;
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(dwgPath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                db.CloseInput(true);
                try
                {
                    // Bind, explode, purge và ghi file đều dựa vào WorkingDatabase; đổi tạm rồi trả lại.
                    HostApplicationServices.WorkingDatabase = db;
                    if (clean)
                        DrawingCleaner.Clean(db, steps);
                    db.DxfOut(dxfPath, precision, version);
                }
                finally
                {
                    HostApplicationServices.WorkingDatabase = previous;
                }
            }
        }

        private static string GetOutputPath(string src, ConvertOptions options)
        {
            var name = Path.GetFileNameWithoutExtension(src) + ".dxf";
            if (string.IsNullOrWhiteSpace(options.OutputFolder))
                return Path.Combine(Path.GetDirectoryName(src), name);

            // Giữ nguyên cấu trúc thư mục con bên trong thư mục đích.
            var relativeDir = GetRelativePath(options.SourceFolder, Path.GetDirectoryName(src));
            return Path.Combine(options.OutputFolder, relativeDir, name);
        }

        private static string GetRelativePath(string root, string path)
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return full.Substring(rootFull.Length).TrimEnd(Path.DirectorySeparatorChar);
        }

        public static string VersionName(DwgVersion v)
        {
            switch (v)
            {
                case DwgVersion.AC1032: return "2018";
                case DwgVersion.AC1027: return "2013";
                case DwgVersion.AC1024: return "2010";
                case DwgVersion.AC1021: return "2007";
                case DwgVersion.AC1800: return "2004";
                case DwgVersion.AC1015: return "2000";
                case DwgVersion.AC1009: return "R12";
                default: return v.ToString();
            }
        }

        public static DwgVersion ParseVersion(string keyword)
        {
            switch ((keyword ?? string.Empty).ToUpperInvariant().TrimStart('V'))
            {
                case "2013": return DwgVersion.AC1027;
                case "2010": return DwgVersion.AC1024;
                case "2007": return DwgVersion.AC1021;
                case "2004": return DwgVersion.AC1800;
                case "2000": return DwgVersion.AC1015;
                case "R12": return DwgVersion.AC1009;
                default: return DwgVersion.AC1032;
            }
        }
    }
}

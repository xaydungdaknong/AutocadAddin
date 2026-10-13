using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using WinForms = System.Windows.Forms;

[assembly: CommandClass(typeof(DwgToDxf.Commands))]

namespace DwgToDxf
{
    public class Commands
    {
        /// <summary>
        /// DWG2DXF: chọn thư mục bằng hộp thoại, hỏi các tùy chọn ở dòng lệnh.
        /// </summary>
        [CommandMethod("DWG2DXF", CommandFlags.Modal)]
        public void Dwg2DxfDialog()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;

            var source = PickFolder("Chọn thư mục chứa các file DWG cần chuyển");
            if (source == null) return;

            if (!AskOptions(ed, out var includeSub, out var useOtherFolder, out var version, out var clean)) return;

            string output = null;
            if (useOtherFolder)
            {
                output = PickFolder("Chọn thư mục lưu file DXF");
                if (output == null) return;
            }

            Execute(ed, new ConvertOptions
            {
                SourceFolder = source,
                OutputFolder = output,
                IncludeSubfolders = includeSub,
                Version = version,
                Clean = clean,
            });
        }

        /// <summary>
        /// -DWG2DXF: bản dòng lệnh, nhập đường dẫn bằng chữ (dùng được trong file script .scr).
        /// </summary>
        [CommandMethod("-DWG2DXF", CommandFlags.Modal)]
        public void Dwg2DxfCommandLine()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;

            var src = ed.GetString(new PromptStringOptions("\nThư mục nguồn: ") { AllowSpaces = true });
            if (src.Status != PromptStatus.OK) return;
            var source = src.StringResult.Trim().Trim('"');
            if (!Directory.Exists(source))
            {
                ed.WriteMessage("\nKhông tìm thấy thư mục: {0}", source);
                return;
            }

            if (!AskOptions(ed, out var includeSub, out var useOtherFolder, out var version, out var clean)) return;

            string output = null;
            if (useOtherFolder)
            {
                var dst = ed.GetString(new PromptStringOptions("\nThư mục đích: ") { AllowSpaces = true });
                if (dst.Status != PromptStatus.OK) return;
                output = dst.StringResult.Trim().Trim('"');
            }

            Execute(ed, new ConvertOptions
            {
                SourceFolder = source,
                OutputFolder = output,
                IncludeSubfolders = includeSub,
                Version = version,
                Clean = clean,
            });
        }

        private static bool AskOptions(Editor ed, out bool includeSub, out bool useOtherFolder, out Autodesk.AutoCAD.DatabaseServices.DwgVersion version, out bool clean)
        {
            includeSub = false;
            useOtherFolder = false;
            version = Autodesk.AutoCAD.DatabaseServices.DwgVersion.AC1032;
            clean = true;

            var sub = new PromptKeywordOptions("\nGồm cả thư mục con? [Yes/No] <No>: ", "Yes No");
            sub.Keywords.Default = "No";
            sub.AllowNone = true;
            var r1 = ed.GetKeywords(sub);
            if (r1.Status != PromptStatus.OK && r1.Status != PromptStatus.None) return false;
            includeSub = r1.StringResult == "Yes";

            var dest = new PromptKeywordOptions("\nLưu DXF vào đâu? [Same/Other] <Same>: ", "Same Other");
            dest.Keywords.Default = "Same";
            dest.AllowNone = true;
            var r2 = ed.GetKeywords(dest);
            if (r2.Status != PromptStatus.OK && r2.Status != PromptStatus.None) return false;
            useOtherFolder = r2.StringResult == "Other";

            var ver = new PromptKeywordOptions(
                "\nPhiên bản DXF [V2018/V2013/V2010/V2007/V2004/V2000/R12] <V2018>: ",
                "V2018 V2013 V2010 V2007 V2004 V2000 R12");
            ver.Keywords.Default = "V2018";
            ver.AllowNone = true;
            var r3 = ed.GetKeywords(ver);
            if (r3.Status != PromptStatus.OK && r3.Status != PromptStatus.None) return false;
            version = DxfConverter.ParseVersion(r3.StringResult);

            var cl = new PromptKeywordOptions(
                "\nLàm sạch trước khi xuất (audit, bind xref, phá block, purge)? [Yes/No] <Yes>: ", "Yes No");
            cl.Keywords.Default = "Yes";
            cl.AllowNone = true;
            var r4 = ed.GetKeywords(cl);
            if (r4.Status != PromptStatus.OK && r4.Status != PromptStatus.None) return false;
            clean = r4.StringResult != "No";

            return true;
        }

        private static void Execute(Editor ed, ConvertOptions options)
        {
            var result = DxfConverter.Run(options);

            if (result.Total == 0)
            {
                ed.WriteMessage("\nKhông có file DWG nào trong {0}.", options.SourceFolder);
                return;
            }

            ed.WriteMessage("\nĐã chuyển {0}/{1} file sang DXF {2}.",
                result.Succeeded, result.Total, DxfConverter.VersionName(options.Version));
            if (result.Failed.Count > 0)
                ed.WriteMessage("\n{0} file lỗi.", result.Failed.Count);
            if (result.Cancelled)
                ed.WriteMessage("\nĐã dừng theo yêu cầu (Esc).");
            ed.WriteMessage("\nNhật ký: {0}\n", result.LogFile);
        }

        private static string PickFolder(string description)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = description;
                dlg.ShowNewFolderButton = true;
                return dlg.ShowDialog() == WinForms.DialogResult.OK ? dlg.SelectedPath : null;
            }
        }
    }
}

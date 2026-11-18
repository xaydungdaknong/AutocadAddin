using System;
using System.IO;

namespace DwgToDxf
{
    /// <summary>
    /// Các hàm thuần (không dùng API AutoCAD) để tính đường dẫn và đọc từ khóa phiên bản.
    /// Tách riêng để kiểm thử tự động được mà không cần AutoCAD.
    /// </summary>
    public static class PathUtil
    {
        /// <summary>Bỏ dấu nháy, khoảng trắng thừa và đổi thành đường dẫn tuyệt đối. Rỗng trả về null.</summary>
        public static string NormalizeFolder(string input)
        {
            var s = (input ?? string.Empty).Trim().Trim('"').Trim();
            if (s.Length == 0) return null;
            return Path.GetFullPath(s);
        }

        /// <summary>
        /// Đường dẫn file DXF cho một file DWG. outputFolder rỗng: cạnh file gốc.
        /// Có outputFolder: giữ nguyên cấu trúc thư mục con so với sourceFolder.
        /// </summary>
        public static string GetOutputPath(string dwgPath, string sourceFolder, string outputFolder)
        {
            var name = Path.GetFileNameWithoutExtension(dwgPath) + ".dxf";
            if (outputFolder == null || outputFolder.Trim().Length == 0)
                return Path.Combine(Path.GetDirectoryName(dwgPath), name);

            // Path.Combine 2 tham số: .NET 3.5 (bản AutoCAD 2010) không có bản 3 tham số.
            var relativeDir = GetRelativePath(sourceFolder, Path.GetDirectoryName(dwgPath));
            return Path.Combine(Path.Combine(outputFolder, relativeDir), name);
        }

        /// <summary>Đường dẫn của path tính từ root. Rỗng nếu path không nằm trong root.</summary>
        public static string GetRelativePath(string root, string path)
        {
            var sep = Path.DirectorySeparatorChar;
            var rootFull = Path.GetFullPath(root).TrimEnd(sep, Path.AltDirectorySeparatorChar) + sep;
            var full = Path.GetFullPath(path).TrimEnd(sep, Path.AltDirectorySeparatorChar) + sep;
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return full.Substring(rootFull.Length).TrimEnd(sep);
        }

        /// <summary>True nếu file có phần mở rộng .dwg (không phân biệt hoa thường; loại .dwg.bak, .dwgx...).</summary>
        public static bool IsDwg(string path) =>
            string.Equals(Path.GetExtension(path), ".dwg", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Đổi từ khóa phiên bản (V2018, 2013, r12...) thành tên chuẩn: 2018, 2013, 2010, 2007, 2004, 2000, R12.
        /// Từ khóa không nhận ra hoặc rỗng: 2018.
        /// </summary>
        public static string NormalizeVersionKeyword(string keyword)
        {
            var k = (keyword ?? string.Empty).Trim().ToUpperInvariant();
            if (k.StartsWith("V")) k = k.Substring(1);
            switch (k)
            {
                case "2013": case "2010": case "2007": case "2004": case "2000": return k;
                case "R12": case "12": return "R12";
                default: return "2018";
            }
        }
    }
}

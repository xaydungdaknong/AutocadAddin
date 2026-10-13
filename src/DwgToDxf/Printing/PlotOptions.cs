using System;
using System.Globalization;
using Autodesk.AutoCAD.PlottingServices;

namespace DwgToDxf.Printing
{
    /// <summary>Thiết lập in người dùng chọn trong hộp thoại, đè lên page setup của layout.</summary>
    public class PlotOptions
    {
        /// <summary>Máy in / file .pc3, ví dụ "DWG To PDF.pc3".</summary>
        public string Device { get; set; }

        /// <summary>Tên khổ giấy chuẩn (canonical) của máy in.</summary>
        public string Media { get; set; }

        /// <summary>Bảng nét in (.ctb/.stb). Null: giữ theo page setup.</summary>
        public string StyleSheet { get; set; }

        public ScaleMode Scale { get; set; } = ScaleMode.Fit;

        /// <summary>Mẫu số tỉ lệ 1:N (mm giấy : đơn vị bản vẽ) khi Scale = Custom.</summary>
        public double ScaleDenominator { get; set; } = 1;

        /// <summary>Đường dẫn file khi máy in chỉ in ra file (PDF, DWF...). Null: gửi tới máy in.</summary>
        public string OutputFile { get; set; }

        /// <summary>Lần in trước trong phiên làm việc, để mở hộp thoại lần sau với cùng lựa chọn.</summary>
        public static PlotOptions Last { get; set; }

        public enum ScaleMode
        {
            Fit,
            PageSetup,
            Custom,
        }

        /// <summary>Máy in này có bắt buộc in ra file không (PDF, DWF, PNG...).</summary>
        public static bool IsFileDevice(string device, out string extension)
        {
            extension = null;
            try
            {
                var pc = PlotConfigManager.SetCurrentConfig(device);
                extension = pc.DefaultFileExtension;
                return pc.PlotToFileCapability == PlotToFileCapability.MustPlotToFile;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return false;
            }
        }

        /// <summary>Đọc tỉ lệ dạng "1:100", "1/100" hoặc "100". Trả về mẫu số, hoặc 0 nếu sai.</summary>
        public static double ParseScale(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var parts = text.Split(new[] { ':', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1 && TryNumber(parts[0], out var n) && n > 0) return n;
            if (parts.Length == 2 && TryNumber(parts[0], out var a) && TryNumber(parts[1], out var b) && a > 0 && b > 0) return b / a;
            return 0;
        }

        private static bool TryNumber(string s, out double v) =>
            double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}

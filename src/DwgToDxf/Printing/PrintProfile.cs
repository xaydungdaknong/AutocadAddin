using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Autodesk.AutoCAD.PlottingServices;

namespace DwgToDxf.Printing
{
    public enum ScaleMode
    {
        /// <summary>Vừa khổ giấy.</summary>
        Fit,

        /// <summary>Tỉ lệ 1:N (1 mm giấy = N đơn vị bản vẽ).</summary>
        Custom,
    }

    public enum AreaMode
    {
        /// <summary>Các khung tên/khung giấy: block cùng tên hoặc polyline chữ nhật cùng layer.</summary>
        Frame,

        /// <summary>Một vùng chữ nhật cố định (tọa độ lưu trong cấu hình).</summary>
        Window,

        /// <summary>Cả layout (theo layout) hoặc cả bản vẽ trong Model (extents).</summary>
        Whole,
    }

    public enum OrderMode
    {
        Rows,
        Columns,
    }

    public enum BatchSpaces
    {
        Model,
        Layouts,
        Both,
    }

    /// <summary>
    /// Một cấu hình in đặt tên được, lưu lại để dùng nhiều lần: máy in, khổ giấy, tỉ lệ, màu nét in,
    /// cách lấy vùng in, thứ tự in và các thiết lập in nhiều file.
    /// </summary>
    public class PrintProfile
    {
        public string Name { get; set; } = "Mặc định";

        // Máy in
        public string Device { get; set; }
        public string Media { get; set; }

        /// <summary>Bảng nét in (.ctb/.stb). Null: giữ theo page setup.</summary>
        public string StyleSheet { get; set; }

        public ScaleMode Scale { get; set; } = ScaleMode.Fit;
        public double ScaleDenominator { get; set; } = 1;

        // Vùng in
        public AreaMode Area { get; set; } = AreaMode.Frame;

        /// <summary>Khung là block: tên block (tên gốc nếu là dynamic block).</summary>
        public string FrameBlock { get; set; }

        /// <summary>Khung là polyline chữ nhật: layer của polyline.</summary>
        public string FrameLayer { get; set; }

        public double WindowMinX { get; set; }
        public double WindowMinY { get; set; }
        public double WindowMaxX { get; set; }
        public double WindowMaxY { get; set; }

        public OrderMode Order { get; set; } = OrderMode.Rows;

        // Đầu ra và in nhiều file
        /// <summary>Thư mục lưu file in (PDF...). Trống: cạnh bản vẽ.</summary>
        public string OutputFolder { get; set; }

        public string BatchFolder { get; set; }
        public bool BatchSubfolders { get; set; }
        public BatchSpaces BatchSpaces { get; set; } = BatchSpaces.Layouts;

        [XmlIgnore]
        public bool HasFrameRule => !string.IsNullOrEmpty(FrameBlock) || !string.IsNullOrEmpty(FrameLayer);

        [XmlIgnore]
        public bool HasWindow => WindowMaxX > WindowMinX && WindowMaxY > WindowMinY;

        /// <summary>Cấu hình có đủ để lấy vùng in mà không cần hỏi thêm không.</summary>
        [XmlIgnore]
        public bool HasArea =>
            Area == AreaMode.Whole || (Area == AreaMode.Frame && HasFrameRule) || (Area == AreaMode.Window && HasWindow);

        public string AreaDescription()
        {
            switch (Area)
            {
                case AreaMode.Frame:
                    if (!string.IsNullOrEmpty(FrameBlock)) return "khung là block \"" + FrameBlock + "\"";
                    if (!string.IsNullOrEmpty(FrameLayer)) return "khung là polyline chữ nhật trên layer \"" + FrameLayer + "\"";
                    return "khung (chưa chọn khung mẫu)";
                case AreaMode.Window:
                    return HasWindow
                        ? string.Format(CultureInfo.InvariantCulture, "vùng ({0:0.##},{1:0.##}) - ({2:0.##},{3:0.##})",
                            WindowMinX, WindowMinY, WindowMaxX, WindowMaxY)
                        : "vùng (chưa chọn)";
                default:
                    return "cả layout / cả bản vẽ";
            }
        }

        public Frame WindowFrame() => new Frame
        {
            MinX = WindowMinX, MinY = WindowMinY, MaxX = WindowMaxX, MaxY = WindowMaxY,
        };

        public PrintProfile Clone() => (PrintProfile)MemberwiseClone();

        /// <summary>Máy in này có bắt buộc in ra file không (PDF, DWF...).</summary>
        public static bool IsFileDevice(string device, out string extension)
        {
            extension = null;
            if (string.IsNullOrEmpty(device)) return false;
            try
            {
                var pc = PlotConfigManager.SetCurrentConfig(device);
                extension = (pc.DefaultFileExtension ?? "").TrimStart('.');
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

    /// <summary>File lưu các cấu hình in.</summary>
    public class PrintProfileFile
    {
        public string Active { get; set; }
        public List<PrintProfile> Profiles { get; set; } = new List<PrintProfile>();
    }

    /// <summary>
    /// Đọc/ghi cấu hình in ở %APPDATA%\AutocadAddin\CauHinhIn.xml.
    /// <see cref="Current"/> là cấu hình đang dùng trong phiên AutoCAD (kể cả thay đổi chưa lưu).
    /// </summary>
    public static class ProfileStore
    {
        private static PrintProfile _current;

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutocadAddin", "CauHinhIn.xml");

        public static PrintProfile Current
        {
            get
            {
                if (_current == null)
                {
                    var file = Load();
                    _current = (file.Profiles.FirstOrDefault(p => p.Name == file.Active) ?? file.Profiles.FirstOrDefault())?.Clone()
                               ?? new PrintProfile();
                }
                return _current;
            }
            set => _current = value;
        }

        public static PrintProfileFile Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new PrintProfileFile();
                using (var s = File.OpenRead(FilePath))
                    return (PrintProfileFile)new XmlSerializer(typeof(PrintProfileFile)).Deserialize(s) ?? new PrintProfileFile();
            }
            catch (Exception)
            {
                // File hỏng: bắt đầu lại từ danh sách rỗng, không làm hỏng lệnh in.
                return new PrintProfileFile();
            }
        }

        public static void Save(PrintProfileFile file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var tmp = FilePath + ".tmp";
            using (var s = File.Create(tmp))
                new XmlSerializer(typeof(PrintProfileFile)).Serialize(s, file);
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);
        }

        /// <summary>Lưu (thêm hoặc ghi đè theo tên) và đặt làm cấu hình đang dùng.</summary>
        public static void SaveProfile(PrintProfile profile)
        {
            var file = Load();
            file.Profiles.RemoveAll(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            file.Profiles.Add(profile.Clone());
            file.Profiles.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            file.Active = profile.Name;
            Save(file);
            _current = profile.Clone();
        }

        public static void DeleteProfile(string name)
        {
            var file = Load();
            file.Profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(file.Active, name, StringComparison.OrdinalIgnoreCase)) file.Active = null;
            Save(file);
        }

        public static void SetActive(string name)
        {
            var file = Load();
            file.Active = name;
            Save(file);
        }
    }
}

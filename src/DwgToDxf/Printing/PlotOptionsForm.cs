using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PlotSettings = Autodesk.AutoCAD.DatabaseServices.PlotSettings;
using PlotSettingsValidator = Autodesk.AutoCAD.DatabaseServices.PlotSettingsValidator;

namespace DwgToDxf.Printing
{
    /// <summary>
    /// Hộp thoại chọn máy in, khổ giấy, tỉ lệ, màu nét in (bảng .ctb/.stb) và file đầu ra.
    /// Giá trị ban đầu lấy theo lần in trước trong phiên, nếu chưa có thì theo page setup.
    /// </summary>
    public class PlotOptionsForm : Form
    {
        private const string FitText = "Vừa khổ giấy (Fit)";
        private const string PageSetupText = "Theo page setup";
        private static readonly string[] StandardScales =
            { "1:1", "1:2", "1:5", "1:10", "1:20", "1:25", "1:50", "1:75", "1:100", "1:200", "1:250", "1:500", "1:1000" };

        private readonly PlotSettings _ps;
        private readonly string _pageSetupStyle;
        private readonly bool _colorDependent;
        private readonly string _defaultFile;

        private readonly ComboBox _device = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
        private readonly ComboBox _media = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
        private readonly ComboBox _scale = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 340 };
        private readonly ComboBox _style = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
        private readonly TextBox _file = new TextBox { Width = 300 };
        private readonly Button _browse = new Button { Text = "...", Width = 34 };
        private readonly Label _fileLabel = new Label { Text = "Lưu vào file:", AutoSize = true, Anchor = AnchorStyles.Left };

        private string _fileExtension;

        public PlotOptions Result { get; private set; }

        /// <param name="ps">Bản sao page setup của layout (form sẽ thay đổi bản sao này khi đổi máy in).</param>
        /// <param name="wholeLayout">In cả layout: có thêm lựa chọn tỉ lệ "Theo page setup".</param>
        /// <param name="defaultFile">Đường dẫn file mặc định, không có phần mở rộng.</param>
        public PlotOptionsForm(PlotSettings ps, bool colorDependent, bool wholeLayout, string defaultFile, int pageCount)
        {
            _ps = ps;
            _pageSetupStyle = ps.CurrentStyleSheet;
            _colorDependent = colorDependent;
            _defaultFile = defaultFile;

            Text = pageCount > 1 ? "In " + pageCount + " khung" : "In";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Font = new Font("Segoe UI", 9f);

            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(10), Dock = DockStyle.Fill };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            AddRow(grid, "Máy in:", _device);
            AddRow(grid, "Khổ giấy:", _media);
            AddRow(grid, "Tỉ lệ:", _scale);
            AddRow(grid, "Màu nét in:", _style);

            var filePanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            filePanel.Controls.Add(_file);
            filePanel.Controls.Add(_browse);
            grid.Controls.Add(_fileLabel);
            grid.Controls.Add(filePanel);

            var ok = new Button { Text = "In", DialogResult = DialogResult.None, Width = 90 };
            var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Width = 90 };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            grid.Controls.Add(new Label());
            grid.Controls.Add(buttons);

            Controls.Add(grid);
            AcceptButton = ok;
            CancelButton = cancel;

            ok.Click += (s, e) => Accept();
            _browse.Click += (s, e) => Browse();
            FillInitial(wholeLayout);
            _device.SelectedIndexChanged += (s, e) => DeviceChanged(null);
        }

        private static void AddRow(TableLayoutPanel grid, string label, Control control)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left });
            grid.Controls.Add(control);
        }

        private void FillInitial(bool wholeLayout)
        {
            var last = PlotOptions.Last;
            var psv = PlotSettingsValidator.Current;

            // Máy in
            var devices = psv.GetPlotDeviceList().Cast<string>()
                .Where(d => !string.Equals(d, "None", StringComparison.OrdinalIgnoreCase)).ToList();
            _device.Items.AddRange(devices.Cast<object>().ToArray());

            // Tỉ lệ
            _scale.Items.Add(FitText);
            if (wholeLayout) _scale.Items.Add(PageSetupText);
            _scale.Items.AddRange(StandardScales.Cast<object>().ToArray());
            if (last != null && last.Scale == PlotOptions.ScaleMode.Custom)
                _scale.Text = "1:" + last.ScaleDenominator;
            else
                _scale.Text = wholeLayout && (last == null || last.Scale == PlotOptions.ScaleMode.PageSetup) ? PageSetupText : FitText;

            // Màu nét in: chỉ bảng cùng kiểu với bản vẽ (.ctb hoặc .stb).
            var ext = _colorDependent ? ".ctb" : ".stb";
            _style.Items.Add(new StyleItem(null, PageSetupText + (string.IsNullOrEmpty(_pageSetupStyle) ? " (không có)" : " (" + _pageSetupStyle + ")")));
            foreach (var s in psv.GetPlotStyleSheetList().Cast<string>()
                         .Where(s => s.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(s => StyleRank(s)).ThenBy(s => s, StringComparer.OrdinalIgnoreCase))
                _style.Items.Add(new StyleItem(s, StyleLabel(s)));
            _style.SelectedIndex = 0;
            if (last?.StyleSheet != null)
            {
                var match = _style.Items.Cast<StyleItem>().FirstOrDefault(i =>
                    string.Equals(i.Name, last.StyleSheet, StringComparison.OrdinalIgnoreCase));
                if (match != null) _style.SelectedItem = match;
            }

            // Chọn máy in: lần trước > page setup > DWG To PDF.
            var wanted = new[] { last?.Device, _ps.PlotConfigurationName, FramePlotter.PdfDevice }
                .FirstOrDefault(d => d != null && devices.Any(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase)));
            var wantedMedia = last != null && string.Equals(last.Device, wanted, StringComparison.OrdinalIgnoreCase)
                ? last.Media
                : _ps.CanonicalMediaName;
            if (wanted != null)
            {
                _device.SelectedItem = devices.First(x => string.Equals(x, wanted, StringComparison.OrdinalIgnoreCase));
                DeviceChanged(wantedMedia);
            }
        }

        /// <summary>Nạp lại danh sách khổ giấy và ô file theo máy in đang chọn.</summary>
        private void DeviceChanged(string wantedMedia)
        {
            var device = _device.SelectedItem as string;
            if (device == null) return;

            var previous = wantedMedia ?? (_media.SelectedItem as MediaItem)?.Name;
            var psv = PlotSettingsValidator.Current;
            _media.Items.Clear();
            try
            {
                psv.SetPlotConfigurationName(_ps, device, null);
                psv.RefreshLists(_ps);
                foreach (var m in psv.GetCanonicalMediaNameList(_ps).Cast<string>())
                    _media.Items.Add(new MediaItem(m, psv.GetLocaleMediaName(_ps, m)));
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, "Không đọc được máy in \"" + device + "\":\n" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            var items = _media.Items.Cast<MediaItem>().ToList();
            _media.SelectedItem =
                items.FirstOrDefault(i => string.Equals(i.Name, previous, StringComparison.OrdinalIgnoreCase))
                ?? items.FirstOrDefault(i => i.Name.IndexOf("full_bleed_A3", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? items.FirstOrDefault(i => i.Name.IndexOf("A3", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? items.FirstOrDefault();

            var toFile = PlotOptions.IsFileDevice(device, out _fileExtension);
            if (string.IsNullOrEmpty(_fileExtension)) _fileExtension = "pdf";
            _fileExtension = _fileExtension.TrimStart('.');
            _file.Enabled = _browse.Enabled = _fileLabel.Enabled = toFile;
            if (toFile)
                _file.Text = string.IsNullOrWhiteSpace(_file.Text)
                    ? _defaultFile + "." + _fileExtension
                    : Path.ChangeExtension(_file.Text, _fileExtension);
        }

        private void Browse()
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Lưu file in";
                dlg.Filter = _fileExtension.ToUpperInvariant() + " (*." + _fileExtension + ")|*." + _fileExtension;
                dlg.OverwritePrompt = true;
                try
                {
                    dlg.InitialDirectory = Path.GetDirectoryName(_file.Text);
                    dlg.FileName = Path.GetFileName(_file.Text);
                }
                catch (ArgumentException)
                {
                    // Đường dẫn gõ tay không hợp lệ: để hộp thoại tự chọn thư mục.
                }
                if (dlg.ShowDialog(this) == DialogResult.OK) _file.Text = dlg.FileName;
            }
        }

        private void Accept()
        {
            var device = _device.SelectedItem as string;
            if (device == null)
            {
                MessageBox.Show(this, "Hãy chọn máy in.", Text);
                return;
            }

            var options = new PlotOptions
            {
                Device = device,
                Media = (_media.SelectedItem as MediaItem)?.Name,
                StyleSheet = (_style.SelectedItem as StyleItem)?.Name,
            };

            var scale = _scale.Text.Trim();
            if (scale == FitText) options.Scale = PlotOptions.ScaleMode.Fit;
            else if (scale == PageSetupText) options.Scale = PlotOptions.ScaleMode.PageSetup;
            else
            {
                var n = PlotOptions.ParseScale(scale);
                if (n <= 0)
                {
                    MessageBox.Show(this, "Tỉ lệ không hợp lệ. Gõ dạng 1:100 hoặc chọn \"" + FitText + "\".", Text);
                    return;
                }
                options.Scale = PlotOptions.ScaleMode.Custom;
                options.ScaleDenominator = n;
            }

            if (_file.Enabled)
            {
                var path = _file.Text.Trim().Trim('"');
                if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path))
                {
                    MessageBox.Show(this, "Hãy chọn nơi lưu file.", Text);
                    return;
                }
                if (File.Exists(path) && MessageBox.Show(this, "File đã có, ghi đè?\n" + path, Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                options.OutputFile = path;
            }

            Result = options;
            PlotOptions.Last = options;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Đưa các bảng thông dụng lên đầu danh sách.</summary>
        private static int StyleRank(string s)
        {
            var n = s.ToLowerInvariant();
            if (n.StartsWith("monochrome")) return 0;
            if (n.StartsWith("acad")) return 1;
            if (n.StartsWith("grayscale")) return 2;
            return 3;
        }

        private static string StyleLabel(string s)
        {
            var n = s.ToLowerInvariant();
            if (n.StartsWith("monochrome")) return s + " (đen trắng)";
            if (n.StartsWith("acad")) return s + " (màu theo bản vẽ)";
            if (n.StartsWith("grayscale")) return s + " (thang xám)";
            return s;
        }

        private sealed class MediaItem
        {
            public MediaItem(string name, string label)
            {
                Name = name;
                Label = string.IsNullOrEmpty(label) ? name : label;
            }

            public string Name { get; }
            public string Label { get; }
            public override string ToString() => Label;
        }

        private sealed class StyleItem
        {
            public StyleItem(string name, string label)
            {
                Name = name;
                Label = label;
            }

            public string Name { get; }
            public string Label { get; }
            public override string ToString() => Label;
        }
    }
}

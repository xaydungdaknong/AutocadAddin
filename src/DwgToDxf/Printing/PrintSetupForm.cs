using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
#if ACAD2010
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
#else
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
#endif
using OpenMode = Autodesk.AutoCAD.DatabaseServices.OpenMode;
using PlotSettings = Autodesk.AutoCAD.DatabaseServices.PlotSettings;
using PlotSettingsValidator = Autodesk.AutoCAD.DatabaseServices.PlotSettingsValidator;

namespace DwgToDxf.Printing
{
    public enum SetupAction
    {
        None,
        Print,
        PrintModel,
        PrintLayout,
        Batch,

        /// <summary>Chỉ dùng ở bản AutoCAD 2010: đóng hộp thoại để chọn trên bản vẽ rồi mở lại.</summary>
        PickFrame,
        PickWindow,
    }

    /// <summary>
    /// Công cụ cài đặt in: chọn/lưu cấu hình, vùng in (nút chọn khung mẫu, chọn vùng), máy in, khổ giấy,
    /// tỉ lệ, màu nét in, thư mục lưu file và thiết lập in nhiều file.
    /// Chế độ <see cref="Mode.Print"/> chỉ hiện phần máy in, dùng ngay trong lệnh INMODEL/INLAYOUT.
    /// </summary>
    public class PrintSetupForm : Form
    {
        public enum Mode
        {
            Setup,
            Print,
        }

        private const string FitText = "Vừa khổ giấy (Fit)";
        private static readonly string[] StandardScales =
            { "1:1", "1:2", "1:5", "1:10", "1:20", "1:25", "1:50", "1:75", "1:100", "1:200", "1:250", "1:500", "1:1000" };

        private readonly PlotSettings _ps;
        private readonly bool _colorDependent;
        private readonly Mode _mode;
        private PrintProfile _profile;

        // Cấu hình
        private readonly ComboBox _profileName = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 230 };

        // Vùng in
        private readonly RadioButton _areaFrame = new RadioButton { Text = "Khung", AutoSize = true };
        private readonly RadioButton _areaWindow = new RadioButton { Text = "Vùng", AutoSize = true };
        private readonly RadioButton _areaWhole = new RadioButton { Text = "Cả layout / cả bản vẽ", AutoSize = true };
        private readonly Label _frameInfo = new Label { AutoSize = true, MaximumSize = new Size(260, 0) };
        private readonly Label _windowInfo = new Label { AutoSize = true, MaximumSize = new Size(260, 0) };
        private readonly ComboBox _order = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };

        // Máy in
        private readonly ComboBox _device = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
        private readonly ComboBox _media = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
        private readonly ComboBox _scale = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 340 };
        private readonly ComboBox _style = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
        private readonly TextBox _outputFolder = new TextBox { Width = 300 };

        // In nhiều file
        private readonly TextBox _batchFolder = new TextBox { Width = 300 };
        private readonly CheckBox _batchSub = new CheckBox { Text = "Gồm cả thư mục con", AutoSize = true };
        private readonly ComboBox _batchSpaces = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };

        public SetupAction Action { get; private set; }

        /// <summary>Cấu hình sau khi người dùng bấm một nút in.</summary>
        public PrintProfile Result { get; private set; }

        /// <param name="ps">Bản sao page setup của layout hiện hành (form sẽ đổi máy in trên bản sao này).</param>
        public PrintSetupForm(Mode mode, PlotSettings ps, bool colorDependent, int pageCount = 0)
        {
            _mode = mode;
            _ps = ps;
            _colorDependent = colorDependent;
            _profile = ProfileStore.Current.Clone();

            Text = mode == Mode.Setup ? "Cài đặt in" : pageCount > 1 ? "In " + pageCount + " trang" : "In";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Font = new Font("Segoe UI", 9f);

            var root = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Padding = new Padding(10),
            };
            Controls.Add(root);

            // --- Cấu hình ---
            var profileRow = Row();
            profileRow.Controls.Add(Caption("Cấu hình:"));
            profileRow.Controls.Add(_profileName);
            var save = Btn("Lưu", 60);
            var delete = Btn("Xóa", 60);
            profileRow.Controls.Add(save);
            profileRow.Controls.Add(delete);
            root.Controls.Add(profileRow);

            // --- Vùng in ---
            if (mode == Mode.Setup)
            {
                var area = Grid(3);
                var pickFrame = Btn("Chọn khung mẫu...", 130);
                var pickWindow = Btn("Chọn vùng...", 130);
                area.Controls.Add(_areaFrame);
                area.Controls.Add(_frameInfo);
                area.Controls.Add(pickFrame);
                area.Controls.Add(_areaWindow);
                area.Controls.Add(_windowInfo);
                area.Controls.Add(pickWindow);
                area.Controls.Add(_areaWhole);
                area.SetColumnSpan(_areaWhole, 3);
                area.Controls.Add(Caption("Thứ tự in:"));
                area.Controls.Add(_order);
                _order.Items.AddRange(new object[] { "Theo hàng (trái → phải, trên → dưới)", "Theo cột (trên → dưới, trái → phải)" });
                root.Controls.Add(Group("Vùng in", area));

                pickFrame.Click += (s, e) => PickFrame();
                pickWindow.Click += (s, e) => PickWindow();
            }

            // --- Máy in ---
            var printer = Grid(2);
            printer.Controls.Add(Caption("Máy in:"));
            printer.Controls.Add(_device);
            printer.Controls.Add(Caption("Khổ giấy:"));
            printer.Controls.Add(_media);
            printer.Controls.Add(Caption("Tỉ lệ:"));
            printer.Controls.Add(_scale);
            printer.Controls.Add(Caption("Màu nét in:"));
            printer.Controls.Add(_style);
            printer.Controls.Add(Caption("Lưu file vào:"));
            var outRow = Row();
            outRow.Controls.Add(_outputFolder);
            var browseOut = Btn("...", 34);
            outRow.Controls.Add(browseOut);
            printer.Controls.Add(outRow);
            printer.Controls.Add(new Label());
            printer.Controls.Add(new Label
            {
                Text = "Để trống: lưu PDF cạnh bản vẽ. Chỉ dùng khi máy in ra file (PDF, DWF).",
                AutoSize = true, ForeColor = SystemColors.GrayText,
            });
            root.Controls.Add(Group("Máy in", printer));
            browseOut.Click += (s, e) => BrowseFolder(_outputFolder, "Chọn thư mục lưu file in");

            // --- In nhiều file ---
            if (mode == Mode.Setup)
            {
                var batch = Grid(2);
                batch.Controls.Add(Caption("Thư mục bản vẽ:"));
                var folderRow = Row();
                folderRow.Controls.Add(_batchFolder);
                var browseBatch = Btn("...", 34);
                folderRow.Controls.Add(browseBatch);
                batch.Controls.Add(folderRow);
                batch.Controls.Add(new Label());
                batch.Controls.Add(_batchSub);
                batch.Controls.Add(Caption("In trong:"));
                batch.Controls.Add(_batchSpaces);
                _batchSpaces.Items.AddRange(new object[] { "Model", "Các layout", "Model và các layout" });
                root.Controls.Add(Group("In nhiều file", batch));
                browseBatch.Click += (s, e) => BrowseFolder(_batchFolder, "Chọn thư mục chứa các file DWG cần in");
            }

            // --- Nút ---
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Anchor = AnchorStyles.Right };
            var cancel = Btn("Hủy", 80);
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(cancel);
            if (mode == Mode.Setup)
            {
                var batchBtn = Btn("In nhiều file", 100);
                var layoutBtn = Btn("In layout này", 100);
                var modelBtn = Btn("In Model", 90);
                buttons.Controls.Add(batchBtn);
                buttons.Controls.Add(layoutBtn);
                buttons.Controls.Add(modelBtn);
                batchBtn.Click += (s, e) => Finish(SetupAction.Batch);
                layoutBtn.Click += (s, e) => Finish(SetupAction.PrintLayout);
                modelBtn.Click += (s, e) => Finish(SetupAction.PrintModel);
                AcceptButton = modelBtn;
            }
            else
            {
                var ok = Btn("In", 90);
                buttons.Controls.Add(ok);
                ok.Click += (s, e) => Finish(SetupAction.Print);
                AcceptButton = ok;
            }
            root.Controls.Add(buttons);
            CancelButton = cancel;

            save.Click += (s, e) => SaveProfile();
            delete.Click += (s, e) => DeleteProfile();
            _profileName.SelectionChangeCommitted += (s, e) => LoadProfile(_profileName.SelectedItem as string);

            FillLists();
            ShowProfile(_profile);
            _device.SelectedIndexChanged += (s, e) => DeviceChanged(null);
        }

        // ---------- dựng giao diện ----------

        private static FlowLayoutPanel Row() =>
            new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };

        private static TableLayoutPanel Grid(int columns)
        {
            var g = new TableLayoutPanel { ColumnCount = columns, AutoSize = true, Dock = DockStyle.Fill };
            for (var i = 0; i < columns; i++) g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return g;
        }

        private static GroupBox Group(string text, Control content)
        {
            var g = new GroupBox { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8) };
            g.Controls.Add(content);
            return g;
        }

        private static Label Caption(string text) => new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left };

        private static Button Btn(string text, int width) => new Button { Text = text, Width = width, AutoSize = true };

        // ---------- danh sách ----------

        private void FillLists()
        {
            var psv = PlotSettingsValidator.Current;

            foreach (var p in ProfileStore.Load().Profiles) _profileName.Items.Add(p.Name);

            _device.Items.AddRange(psv.GetPlotDeviceList().Cast<string>()
                .Where(d => !string.Equals(d, "None", StringComparison.OrdinalIgnoreCase)).Cast<object>().ToArray());

            _scale.Items.Add(FitText);
            _scale.Items.AddRange(StandardScales.Cast<object>().ToArray());

            // Màu nét in: chỉ bảng cùng kiểu với bản vẽ (.ctb hoặc .stb).
            var ext = _colorDependent ? ".ctb" : ".stb";
            var current = _ps.CurrentStyleSheet;
            _style.Items.Add(new Item(null, "Theo page setup" + (string.IsNullOrEmpty(current) ? "" : " (" + current + ")")));
            foreach (var s in psv.GetPlotStyleSheetList().Cast<string>()
                         .Where(s => s.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(StyleRank).ThenBy(s => s, StringComparer.OrdinalIgnoreCase))
                _style.Items.Add(new Item(s, StyleLabel(s)));
        }

        /// <summary>Đưa cấu hình lên giao diện.</summary>
        private void ShowProfile(PrintProfile p)
        {
            _profile = p.Clone();
            _profileName.Text = p.Name;

            _areaFrame.Checked = p.Area == AreaMode.Frame;
            _areaWindow.Checked = p.Area == AreaMode.Window;
            _areaWhole.Checked = p.Area == AreaMode.Whole;
            UpdateAreaLabels();
            if (_order.Items.Count > 0) _order.SelectedIndex = p.Order == OrderMode.Columns ? 1 : 0;

            var devices = _device.Items.Cast<string>().ToList();
            var device = new[] { p.Device, _ps.PlotConfigurationName, FramePlotter.PdfDevice }
                .FirstOrDefault(d => d != null && devices.Any(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase)));
            if (device != null)
            {
                _device.SelectedItem = devices.First(x => string.Equals(x, device, StringComparison.OrdinalIgnoreCase));
                DeviceChanged(string.Equals(device, p.Device, StringComparison.OrdinalIgnoreCase) ? p.Media : _ps.CanonicalMediaName);
            }

            _scale.Text = p.Scale == ScaleMode.Custom ? "1:" + p.ScaleDenominator : FitText;

            _style.SelectedItem = _style.Items.Cast<Item>().FirstOrDefault(i =>
                string.Equals(i.Name, p.StyleSheet, StringComparison.OrdinalIgnoreCase)) ?? _style.Items[0];

            _outputFolder.Text = p.OutputFolder ?? "";
            _batchFolder.Text = p.BatchFolder ?? "";
            _batchSub.Checked = p.BatchSubfolders;
            if (_batchSpaces.Items.Count > 0) _batchSpaces.SelectedIndex = (int)p.BatchSpaces;
        }

        private void UpdateAreaLabels()
        {
            var frame = _profile.Clone();
            frame.Area = AreaMode.Frame;
            var window = _profile.Clone();
            window.Area = AreaMode.Window;
            _frameInfo.Text = frame.AreaDescription();
            _windowInfo.Text = window.AreaDescription();
        }

        /// <summary>Đọc giao diện vào cấu hình. Trả về null (kèm thông báo) nếu có ô sai.</summary>
        private PrintProfile ReadProfile()
        {
            var p = _profile.Clone();
            p.Name = Compat.IsBlank(_profileName.Text) ? "Mặc định" : _profileName.Text.Trim();

            if (_mode == Mode.Setup)
            {
                p.Area = _areaWindow.Checked ? AreaMode.Window : _areaWhole.Checked ? AreaMode.Whole : AreaMode.Frame;
                p.Order = _order.SelectedIndex == 1 ? OrderMode.Columns : OrderMode.Rows;
                p.BatchFolder = _batchFolder.Text.Trim().Trim('"');
                p.BatchSubfolders = _batchSub.Checked;
                p.BatchSpaces = (BatchSpaces)Math.Max(0, _batchSpaces.SelectedIndex);
            }

            p.Device = _device.SelectedItem as string;
            p.Media = (_media.SelectedItem as Item)?.Name;
            p.StyleSheet = (_style.SelectedItem as Item)?.Name;

            var scale = _scale.Text.Trim();
            if (scale == FitText || scale.Length == 0)
            {
                p.Scale = ScaleMode.Fit;
            }
            else
            {
                var n = PrintProfile.ParseScale(scale);
                if (n <= 0)
                {
                    MessageBox.Show(this, "Tỉ lệ không hợp lệ. Gõ dạng 1:100 hoặc chọn \"" + FitText + "\".", Text);
                    return null;
                }
                p.Scale = ScaleMode.Custom;
                p.ScaleDenominator = n;
            }

            var output = _outputFolder.Text.Trim().Trim('"');
            p.OutputFolder = output.Length == 0 ? null : output;
            if (p.OutputFolder != null && !Path.IsPathRooted(p.OutputFolder))
            {
                MessageBox.Show(this, "Thư mục lưu file phải là đường dẫn đầy đủ, ví dụ D:\\BanIn.", Text);
                return null;
            }
            return p;
        }

        // ---------- cấu hình ----------

        private void LoadProfile(string name)
        {
            var p = ProfileStore.Load().Profiles.FirstOrDefault(x => x.Name == name);
            if (p != null) ShowProfile(p);
        }

        private void SaveProfile()
        {
            var p = ReadProfile();
            if (p == null) return;
            ProfileStore.SaveProfile(p);
            if (!_profileName.Items.Cast<string>().Any(n => string.Equals(n, p.Name, StringComparison.OrdinalIgnoreCase)))
                _profileName.Items.Add(p.Name);
            _profile = p.Clone();
            MessageBox.Show(this, "Đã lưu cấu hình \"" + p.Name + "\".", Text);
        }

        private void DeleteProfile()
        {
            var name = _profileName.Text.Trim();
            if (!_profileName.Items.Cast<string>().Contains(name)) return;
            if (MessageBox.Show(this, "Xóa cấu hình \"" + name + "\"?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            ProfileStore.DeleteProfile(name);
            _profileName.Items.Remove(name);
            _profileName.Text = "";
        }

        private void Finish(SetupAction action)
        {
            var p = ReadProfile();
            if (p == null) return;

            if (string.IsNullOrEmpty(p.Device))
            {
                MessageBox.Show(this, "Hãy chọn máy in.", Text);
                return;
            }
            if (action == SetupAction.Batch && !Directory.Exists(p.BatchFolder))
            {
                MessageBox.Show(this, "Hãy chọn thư mục chứa bản vẽ cần in.", Text);
                return;
            }
            if (action == SetupAction.Batch && p.Area == AreaMode.Frame && !p.HasFrameRule)
            {
                MessageBox.Show(this, "In nhiều file theo khung cần chọn khung mẫu trước (nút \"Chọn khung mẫu...\").", Text);
                return;
            }

            // Lần sau mở lại vẫn còn: lưu cấu hình dưới tên đang chọn.
            ProfileStore.SaveProfile(p);
            Result = p;
            Action = action;
            DialogResult = DialogResult.OK;
            Close();
        }

        // ---------- chọn khung mẫu / vùng trên bản vẽ ----------

        private void PickFrame() => Pick(SetupAction.PickFrame);

        private void PickWindow() => Pick(SetupAction.PickWindow);

        private void Pick(SetupAction what)
        {
#if ACAD2010
            // AutoCAD 2010 không cho tạm ẩn hộp thoại WinForms để chọn trên bản vẽ:
            // đóng hộp thoại, lệnh CAIDATIN chọn xong sẽ mở lại với các giá trị đang sửa.
            var p = ReadProfile();
            if (p == null) return;
            Result = p;
            Action = what;
            DialogResult = DialogResult.OK;
            Close();
#else
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            bool picked;
            using (ed.StartUserInteraction(Handle))
                picked = what == SetupAction.PickFrame ? PickFrameOnDrawing(_profile) : PickWindowOnDrawing(_profile);
            if (!picked) return;
            (what == SetupAction.PickFrame ? _areaFrame : _areaWindow).Checked = true;
            UpdateAreaLabels();
#endif
        }

        /// <summary>Chọn khung mẫu trên bản vẽ, ghi quy tắc nhận diện khung vào cấu hình.</summary>
        public static bool PickFrameOnDrawing(PrintProfile profile)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var peo = new PromptEntityOptions("\nChọn một khung mẫu (block hoặc polyline chữ nhật): ");
            while (true)
            {
                var r = ed.GetEntity(peo);
                if (r.Status != PromptStatus.OK) return false;
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var why = FrameFinder.CheckSample(tr, r.ObjectId);
                    if (why == null)
                    {
                        FrameFinder.RuleFromSample(tr, r.ObjectId, out var block, out var layer);
                        tr.Commit();
                        profile.Area = AreaMode.Frame;
                        profile.FrameBlock = block;
                        profile.FrameLayer = layer;
                        return true;
                    }
                    ed.WriteMessage("\n" + why);
                    tr.Commit();
                }
            }
        }

        /// <summary>Quét một vùng chữ nhật trên bản vẽ, ghi tọa độ vào cấu hình.</summary>
        public static bool PickWindowOnDrawing(PrintProfile profile)
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            var a = ed.GetPoint("\nGóc thứ nhất của vùng in: ");
            if (a.Status != PromptStatus.OK) return false;
            var b = ed.GetCorner("\nGóc đối diện: ", a.Value);
            if (b.Status != PromptStatus.OK) return false;

            var ucs = ed.CurrentUserCoordinateSystem;
            Point3d p1 = a.Value.TransformBy(ucs), p2 = b.Value.TransformBy(ucs);
            profile.Area = AreaMode.Window;
            profile.WindowMinX = Math.Min(p1.X, p2.X);
            profile.WindowMinY = Math.Min(p1.Y, p2.Y);
            profile.WindowMaxX = Math.Max(p1.X, p2.X);
            profile.WindowMaxY = Math.Max(p1.Y, p2.Y);
            return true;
        }

        // ---------- máy in ----------

        /// <summary>Nạp lại danh sách khổ giấy theo máy in đang chọn.</summary>
        private void DeviceChanged(string wantedMedia)
        {
            var device = _device.SelectedItem as string;
            if (device == null) return;

            var previous = wantedMedia ?? (_media.SelectedItem as Item)?.Name;
            var psv = PlotSettingsValidator.Current;
            _media.Items.Clear();
            try
            {
                psv.SetPlotConfigurationName(_ps, device, null);
                psv.RefreshLists(_ps);
                foreach (var m in psv.GetCanonicalMediaNameList(_ps).Cast<string>())
                    _media.Items.Add(new Item(m, psv.GetLocaleMediaName(_ps, m)));
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, "Không đọc được máy in \"" + device + "\":\n" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            var items = _media.Items.Cast<Item>().ToList();
            _media.SelectedItem =
                items.FirstOrDefault(i => string.Equals(i.Name, previous, StringComparison.OrdinalIgnoreCase))
                ?? items.FirstOrDefault(i => i.Name.IndexOf("full_bleed_A3", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? items.FirstOrDefault(i => i.Name.IndexOf("A3", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? items.FirstOrDefault();
        }

        private void BrowseFolder(TextBox box, string description)
        {
            using (var dlg = new FolderBrowserDialog { Description = description, ShowNewFolderButton = true })
            {
                if (Directory.Exists(box.Text)) dlg.SelectedPath = box.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.SelectedPath;
            }
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

        private sealed class Item
        {
            public Item(string name, string label)
            {
                Name = name;
                Label = string.IsNullOrEmpty(label) ? name : label;
            }

            public string Name { get; }
            public string Label { get; }
            public override string ToString() => Label;
        }
    }
}

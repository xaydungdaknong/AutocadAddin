# AutocadAddin

Addin .NET cho **AutoCAD 2023** và **AutoCAD 2010**, gồm hai nhóm lệnh trong cùng một file `DwgToDxf.dll`:

- [`DWG2DXF`](#dwg2dxf): chuyển hàng loạt file `.dwg` trong một thư mục sang `.dxf`.
- [`CAIDATIN`, `INMODEL`, `INLAYOUT`, `INNHIEUFILE`](#in-ấn): cài đặt và lưu cấu hình in, in nhiều khung trong Model hoặc Layout (xem và đổi thứ tự trước khi in), in hàng loạt nhiều file trong thư mục.

## DWG2DXF

Chuyển hàng loạt file `.dwg` trong một thư mục sang `.dxf`.

Mỗi file được đọc vào một database phụ (không mở lên màn hình), làm sạch trong bộ nhớ rồi ghi ra bằng `Database.DxfOut`. File DWG gốc không bị thay đổi.

Mục tiêu là file DXF chỉ còn hình học và chữ phẳng, không tham chiếu ra ngoài, để phần mềm khác (hoặc AI như Claude) đọc chính xác nhất.

### Lệnh

| Lệnh | Cách dùng |
|---|---|
| `DWG2DXF` | Hộp thoại chọn thư mục nguồn, sau đó hỏi các tùy chọn ở dòng lệnh. |
| `-DWG2DXF` | Bản dòng lệnh, gõ đường dẫn bằng chữ. Dùng được trong file script `.scr`. |

Các tùy chọn được hỏi:

1. **Gồm cả thư mục con?** `Yes` / `No` (mặc định `No`).
2. **Lưu DXF vào đâu?** `Same` = cạnh file DWG gốc (mặc định), `Other` = chọn thư mục đích. Khi chọn thư mục đích, cấu trúc thư mục con được giữ nguyên.
3. **Phiên bản DXF**: `V2018` (mặc định), `V2013`, `V2010`, `V2007`, `V2004`, `V2000`, `R12`. Bản cho AutoCAD 2010 chỉ có từ `V2010` trở xuống (mặc định `V2010`).
4. **Làm sạch trước khi xuất?** `Yes` (mặc định) / `No`.

### Các bước làm sạch

Chạy theo thứ tự cho từng file. Bước nào lỗi chỉ ghi vào nhật ký, các bước sau và việc xuất DXF vẫn tiếp tục.

1. **Audit**: kiểm tra và sửa lỗi cấu trúc bản vẽ (như lệnh `AUDIT` trả lời Yes).
2. **Xref**: reload xref đang unload, bind kiểu Insert mọi xref tìm thấy (tên block và layer giữ nguyên, không có tiền tố `$0$`). Xref không tìm thấy file được detach và ghi tên vào nhật ký.
3. **Block**: phá (explode) mọi block trong Model và các Layout, lặp lại cho block lồng nhau, kể cả dynamic block và block vừa bind từ xref.
   - Attribute nhìn thấy được chuyển thành TEXT/MTEXT giữ nguyên giá trị. Attribute ẩn bị bỏ.
   - Đối tượng trên layer 0 hoặc có màu, linetype, lineweight ByBlock lấy theo block chứa nó, giống như khi hiển thị.
   - Bảng (TABLE) được giữ nguyên. Block không phá được (ví dụ scale không đều) được giữ nguyên và ghi vào nhật ký.
4. **Rác**: xóa TEXT/MTEXT rỗng và đường thẳng dài bằng 0.
5. **Purge**: lặp lại tới khi hết các block, layer, linetype, text style, dim style, RegApp, multiline style, table style, mleader style, material không dùng.

File DXF trùng tên sẽ bị ghi đè. Nhấn `Esc` để dừng giữa chừng. Sau khi chạy, một file nhật ký `DWG2DXF_yyyyMMdd_HHmmss.log` được ghi vào thư mục đích (hoặc thư mục nguồn), liệt kê file thành công và file lỗi kèm lý do.

Ví dụ script `.scr`:

```
-DWG2DXF
"D:\BanVe\Duong312"
Yes
Other
"D:\BanVe\Duong312_DXF"
V2018
Yes
```

## In ấn

| Lệnh | Dùng khi |
|---|---|
| `CAIDATIN` | Mở công cụ cài đặt in: tạo, lưu, chọn cấu hình in; từ đây bấm nút để in Model, in layout hoặc in nhiều file. |
| `INMODEL` | In các khung/vùng trong Model. Đang ở layout thì lệnh tự chuyển sang tab Model. |
| `INLAYOUT` | In layout hiện hành. Layout chỉ có một khung giấy thì in cả layout; nhiều khung thì chọn khung như `INMODEL`. |
| `INNHIEUFILE` | Giống `CAIDATIN`, dùng để in nhiều file: chọn thư mục ở mục "In nhiều file" rồi bấm **In nhiều file**. |
| `INNHIEUFILECHAY` | In nhiều file ngay theo cấu hình đang dùng, không mở hộp thoại (dùng trong script). |

### Quy trình gọn nhất

1. Gõ `CAIDATIN` một lần, đặt tên cấu hình (ví dụ `A3 đen trắng`), bấm **Chọn khung mẫu...** rồi chọn khung tên trên bản vẽ, chọn máy in, khổ giấy, tỉ lệ, màu nét in, bấm **Lưu**.
2. Từ đó mỗi lần in chỉ cần: `INMODEL` (hoặc `INLAYOUT`) → `Enter` (dùng cấu hình) → `Enter` (lấy tất cả khung) → xem số thứ tự → `Enter` (in). File PDF được lưu ngay, không hỏi thêm.
3. In cả thư mục: `INNHIEUFILE` → chọn thư mục → **In nhiều file**.

### Cấu hình in (`CAIDATIN`)

Hộp thoại gồm:

- **Cấu hình**: ô chọn/gõ tên cấu hình, nút **Lưu** (thêm mới hoặc ghi đè theo tên) và **Xóa**. Mọi cấu hình lưu ở `%APPDATA%\AutocadAddin\CauHinhIn.xml`, dùng chung cho mọi bản vẽ và mọi lần mở AutoCAD. Bấm một nút in cũng tự lưu cấu hình đang chọn.
- **Vùng in**:
  - `Khung` + nút **Chọn khung mẫu...**: bấm vào một khung trên bản vẽ. Khung mẫu là block thì lấy mọi block cùng tên (kể cả dynamic block khác kích thước); là polyline chữ nhật kín thì lấy mọi polyline chữ nhật kín trên **cùng layer**. Khung nằm trọn trong khung khác (ô trong khung tên) tự được bỏ qua.
  - `Vùng` + nút **Chọn vùng...**: quét một hình chữ nhật. Tọa độ được lưu, hợp với các bản vẽ dùng chung một khung đặt cùng vị trí.
  - `Cả layout / cả bản vẽ`: in cả layout theo layout, trong Model in toàn bộ bản vẽ (extents).
  - **Thứ tự in**: theo hàng hoặc theo cột (dùng khi in nhiều khung).
- **Máy in**:
  - **Máy in**: mọi máy in/plotter của Windows và file `.pc3`. Máy in ra file (`DWG To PDF.pc3`, DWF...) gộp tất cả trang vào **một file nhiều trang**.
  - **Khổ giấy**: danh sách khổ của máy in đang chọn.
  - **Tỉ lệ**: `Vừa khổ giấy (Fit)`, các tỉ lệ `1:1` ... `1:1000`, hoặc gõ tay dạng `1:150` (1 mm giấy = 150 đơn vị bản vẽ).
  - **Màu nét in**: `monochrome.ctb` (đen trắng), `acad.ctb` (màu theo bản vẽ), `grayscale.ctb` (thang xám)... hoặc giữ theo page setup. Chỉ hiện bảng `.ctb` hoặc `.stb` đúng kiểu của bản vẽ.
  - **Lưu file vào**: thư mục lưu PDF. Để trống thì lưu cạnh bản vẽ. Tên file: `<tên bản vẽ>-<tên layout>.pdf` khi in một bản vẽ, `<tên bản vẽ>.pdf` khi in nhiều file.
- **In nhiều file**: thư mục chứa DWG, có gồm thư mục con không, in trong `Model`, `Các layout` hay `Model và các layout`.
- Nút **In Model**, **In layout này**, **In nhiều file**, **Hủy**.

### In một bản vẽ (`INMODEL`, `INLAYOUT`)

1. Dòng lệnh nhắc cấu hình đang dùng, rồi hỏi `Chọn một khung mẫu hoặc [CauHinh/Vung/ToanBo]`:
   - Bấm vào một khung: dùng khung đó làm mẫu (và nhớ vào cấu hình đang dùng).
   - `CauHinh` (mặc định khi cấu hình đã có vùng in): dùng vùng in của cấu hình.
   - `Vung`: quét lần lượt từng hình chữ nhật cần in, mỗi hình một trang, `Enter` để xong. Thứ tự in là thứ tự quét.
   - `ToanBo`: in cả layout / cả bản vẽ.
2. Khi dùng khung: chọn vùng chứa các khung, hoặc `Enter` để lấy tất cả.
3. **Xem thứ tự in.** Mỗi khung được viền xanh lơ và hiện số thứ tự màu đỏ ở giữa. Số này chỉ là hình ảo trên màn hình, không ghi vào bản vẽ. Dòng nhắc `Thứ tự in [Hang/Cot/Bam/Sua/CaiDat/In/Thoat] <In>`:
   - `Hang`: theo hàng, trên xuống dưới, trái sang phải. `Cot`: theo cột, trái sang phải, trên xuống dưới.
   - `Bam`: bấm lần lượt vào các khung theo thứ tự muốn in, `Enter` để dừng; khung chưa bấm xếp tiếp theo hàng.
   - `Sua`: sửa thủ công. Bấm vào một khung, gõ số mới; các khung khác tự dồn số.
   - `CaiDat`: mở hộp thoại máy in, khổ giấy, tỉ lệ, màu nét in trước khi in.
   - `In` (hoặc `Enter`): in ngay theo cấu hình. Nếu cấu hình chưa có máy in, hộp thoại máy in tự mở.

   Khung hơi lệch nhau hoặc khác cỡ vẫn được xếp đúng hàng/cột: khung có tâm nằm trong chiều cao (chiều rộng) của khung đầu hàng (đầu cột) được coi là cùng hàng (cột).
4. File PDF đã có sẵn thì lệnh hỏi ghi đè.

Gọi từ nút **In Model** / **In layout này** của `CAIDATIN` thì bước 1 và 2 được bỏ qua: lệnh dùng thẳng vùng in của cấu hình và lấy tất cả khung.

### In nhiều file

Mỗi bản vẽ trong thư mục được mở chỉ đọc, in, rồi đóng lại không lưu (bản vẽ đang mở sẵn thì in luôn và giữ nguyên). Với mỗi bản vẽ:

- Model: in các khung tìm được theo thứ tự của cấu hình. Không có khung nào thì bỏ qua Model.
- Mỗi layout (theo thứ tự tab): có từ 2 khung trở lên thì in từng khung; một khung hoặc không có khung thì in cả layout. Layout trống (chưa vẽ gì) được bỏ qua.
- Tất cả trang của một bản vẽ vào một file PDF `<tên bản vẽ>.pdf`. Chọn thư mục "Lưu file vào" thì cấu trúc thư mục con được giữ nguyên.
- Nhật ký `InNhieuFile_yyyyMMdd_HHmmss.log` ghi vào thư mục lưu file (hoặc thư mục bản vẽ): file nào in được bao nhiêu trang, file nào lỗi và lý do. Bấm **Hủy cả lệnh in** trên hộp tiến độ để dừng.

### Thiết lập in khác

- Lineweight, kiểu tô bóng và các thiết lập khác không có trong hộp thoại lấy theo page setup của Model/layout. Page setup trong bản vẽ không bị thay đổi.
- **Tự xoay giấy theo hướng khung:** khung/vùng in ngang (rộng hơn cao) thì in giấy ngang, khung dọc thì in giấy dọc, tính riêng cho từng trang nên một file PDF có thể lẫn trang ngang và trang dọc. In toàn bộ Model cũng xoay theo hình dạng bản vẽ. In cả layout giữ hướng giấy của page setup. Mỗi khung in kiểu Window, căn giữa.
- Khung xoay nghiêng được in theo hình chữ nhật bao ngoài song song trục.
- Lệnh tạm đặt `BACKGROUNDPLOT` = 0 trong lúc in rồi trả lại giá trị cũ.

## Build

Yêu cầu: Windows, Visual Studio 2019/2022 (workload ".NET desktop development"), .NET Framework 4.8 Developer Pack.

1. Mở `src/DwgToDxf/DwgToDxf.csproj` bằng Visual Studio.
2. Visual Studio tự tải gói NuGet `AutoCAD.NET 24.2.0` (AcCoreMgd, AcDbMgd, AcMgd). Các DLL này không được copy ra thư mục bin vì AutoCAD đã có sẵn.
3. Chọn cấu hình `Release`, build. Một lần build ra hai bản, mỗi bản gồm `DwgToDxf.dll` và file tự nạp `AutocadAddin.lsp`:
   - `src/DwgToDxf/bin/Release/net48/`: cho **AutoCAD 2023** (.NET Framework 4.8, 64-bit).
   - `src/DwgToDxf/bin/Release/net35/`: cho **AutoCAD 2010** (.NET Framework 3.5, chạy cả bản 32-bit và 64-bit).

   Chép đúng bản theo phiên bản AutoCAD. Nạp bản `net48` vào AutoCAD 2010 sẽ báo lỗi `BadImageFormatException ... built by a runtime newer than the currently loaded runtime`.

Hoặc dùng dòng lệnh: `dotnet build src/DwgToDxf/DwgToDxf.csproj -c Release`.

## Nạp vào AutoCAD

### Cách nhanh: file LISP tự nạp (không cần NETLOAD)

1. Chép `DwgToDxf.dll` và `AutocadAddin.lsp` vào cùng một thư mục, ví dụ `D:\AutocadAddin`.
2. `OPTIONS` > Files: thêm thư mục đó vào **Support File Search Path** và **Trusted Locations**.
   Nếu không muốn thêm vào Support File Search Path, mở `AutocadAddin.lsp` và sửa dòng `*ACA-DLL*` thành đường dẫn đầy đủ tới DLL, ví dụ `(setq *ACA-DLL* "D:/AutocadAddin/DwgToDxf.dll")`.
3. `APPLOAD` > **Startup Suite** > Contents... > Add, chọn `AutocadAddin.lsp`. Từ đó mỗi lần mở AutoCAD file LISP tự nạp.
4. Gõ `CAIDATIN`, `INMODEL`, `INLAYOUT`, `INNHIEUFILE` hoặc `DWG2DXF` như bình thường. Lần đầu gõ, LISP nạp DLL rồi chạy lệnh; các lần sau AutoCAD chạy thẳng lệnh trong DLL. Lệnh `TAIADDIN` nạp DLL ngay mà không chạy lệnh nào.

LISP chỉ định nghĩa vài lệnh nhỏ nên mở AutoCAD không chậm thêm; DLL chỉ được nạp khi cần.

### Cách thủ công

1. Mở AutoCAD, gõ `NETLOAD`, chọn `DwgToDxf.dll` (bản `net48` cho AutoCAD 2023, bản `net35` cho AutoCAD 2010).
2. Gõ `CAIDATIN`, `INMODEL`, `INLAYOUT`, `INNHIEUFILE` hoặc `DWG2DXF`.

Nếu AutoCAD cảnh báo bảo mật khi nạp DLL, thêm thư mục chứa DLL vào `TRUSTEDPATHS` (Options > Files > Trusted Locations).

## Ghi chú

- Bản `net48` dùng .NET Framework 4.8 vì AutoCAD 2023 chạy trên nền này. AutoCAD 2025 trở đi chuyển sang .NET 8 và cần build lại với gói AutoCAD.NET tương ứng.
- Bản `net35` biên dịch với thư viện tham chiếu ObjectARX 2010 (gói NuGet `AutoCAD-2010.Net.Base`). Khác biệt so với bản 2023: DXF xuất tối đa bản 2010; trong `CAIDATIN`, nút "Chọn khung mẫu..." và "Chọn vùng..." tạm đóng hộp thoại để chọn trên bản vẽ rồi tự mở lại.
- Phá block làm mất thông tin "đây là block gì". Nếu cần giữ block, trả lời `No` ở câu làm sạch.
- File DWG đang được người khác mở vẫn đọc được (chế độ chia sẻ đọc). File hỏng hoặc có mật khẩu sẽ được ghi vào nhật ký lỗi và bỏ qua.

# AutocadAddin

Addin .NET cho **AutoCAD 2023**, gồm hai nhóm lệnh trong cùng một file `DwgToDxf.dll`:

- [`DWG2DXF`](#dwg2dxf): chuyển hàng loạt file `.dwg` trong một thư mục sang `.dxf`.
- [`INMODEL`, `INLAYOUT`](#in-theo-khung-inmodel-inlayout): in nhiều khung bản vẽ trong Model hoặc Layout, xem và đổi thứ tự in trước khi in.

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
3. **Phiên bản DXF**: `V2018` (mặc định), `V2013`, `V2010`, `V2007`, `V2004`, `V2000`, `R12`.
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

## In theo khung: INMODEL, INLAYOUT

| Lệnh | Dùng khi |
|---|---|
| `INMODEL` | In các khung vẽ trong Model. Đang ở layout thì lệnh tự chuyển sang tab Model. |
| `INLAYOUT` | In layout hiện hành. Layout chỉ có một khung giấy thì nhấn `Enter` để in cả layout; layout có nhiều khung thì chọn khung như `INMODEL`. |

### Các bước

1. **Chọn một khung mẫu.** Khung là block (ví dụ block khung tên A3) hoặc polyline kín hình chữ nhật.
   - Khung mẫu là block: lấy mọi block cùng tên (kể cả dynamic block có kích thước khác nhau).
   - Khung mẫu là polyline: lấy mọi polyline chữ nhật kín nằm trên **cùng layer** với khung mẫu.
   - Khung nằm trọn trong khung khác (ví dụ ô chữ nhật trong khung tên) tự được bỏ qua.
   - Ở `INLAYOUT`, nhấn `Enter` (`CaLayout`) thay vì chọn khung để in thẳng cả layout theo page setup. Nếu chọn khung mà layout chỉ có một khung, lệnh cũng in cả layout.
2. **Chọn vùng chứa các khung**, hoặc nhấn `Enter` để lấy tất cả trong Model/layout hiện hành.
3. **Xem thứ tự in.** Mỗi khung được viền màu xanh lơ và hiện số thứ tự màu đỏ ở giữa. Số này chỉ là hình ảo trên màn hình, không ghi vào bản vẽ và tự mất khi lệnh kết thúc. Thứ tự ban đầu là theo hàng.
4. **Đổi thứ tự** ở dòng nhắc `Thứ tự in [Hang/Cot/Bam/Sua/In/Thoat] <In>`:
   - `Hang`: theo hàng, từ trên xuống dưới, trong mỗi hàng từ trái sang phải.
   - `Cot`: theo cột, từ trái sang phải, trong mỗi cột từ trên xuống dưới.
   - `Bam`: bấm lần lượt vào bên trong các khung theo thứ tự muốn in, số hiện ngay khi bấm. Nhấn `Enter` để dừng; các khung chưa bấm được xếp tiếp theo hàng.
   - `Sua`: sửa thủ công. Bấm vào một khung, gõ số thứ tự mới; các khung khác tự dồn số. Lặp lại cho khung khác, `Enter` để xong.
   - `In` (hoặc `Enter`): sang bước in. `Thoat`: hủy lệnh.

   Khung hơi lệch nhau hoặc khác cỡ vẫn được xếp đúng hàng/cột: khung có tâm nằm trong chiều cao (chiều rộng) của khung đầu hàng (đầu cột) được coi là cùng hàng (cột).
5. **In ra** `[Pdf/Mayin] <Pdf>`:
   - `Pdf`: in bằng `DWG To PDF.pc3` ra **một file PDF nhiều trang**, mỗi khung một trang theo đúng thứ tự. Hộp thoại hỏi chỗ lưu, tên mặc định `<tên bản vẽ>-<tên layout>.pdf`.
   - `Mayin`: gửi tới máy in/plotter đang chọn trong page setup của Model/layout (lệnh `PAGESETUP`), tất cả khung trong một lệnh in.

### Thiết lập in

- Khổ giấy, bảng nét in (CTB/STB), lineweight lấy theo page setup của Model/layout hiện hành. Nếu in PDF mà page setup đang dùng máy khác và khổ giấy đó không có trong `DWG To PDF.pc3`, lệnh chọn khổ A3. Muốn khổ khác thì đặt page setup dùng `DWG To PDF.pc3` với khổ đó trước.
- Mỗi khung in kiểu Window theo đúng hình chữ nhật của khung, tỉ lệ vừa khổ giấy (Fit), căn giữa, tự xoay giấy ngang/đứng theo chiều khung.
- In cả layout (`CaLayout`) giữ nguyên mọi thiết lập của page setup (kiểu in, tỉ lệ), chỉ đổi máy in nếu chọn `Pdf`.
- Khung xoay nghiêng được in theo hình chữ nhật bao ngoài song song trục.
- Lệnh tạm đặt `BACKGROUNDPLOT` = 0 trong lúc in rồi trả lại giá trị cũ.

## Build

Yêu cầu: Windows, Visual Studio 2019/2022 (workload ".NET desktop development"), .NET Framework 4.8 Developer Pack.

1. Mở `src/DwgToDxf/DwgToDxf.csproj` bằng Visual Studio.
2. Visual Studio tự tải gói NuGet `AutoCAD.NET 24.2.0` (AcCoreMgd, AcDbMgd, AcMgd). Các DLL này không được copy ra thư mục bin vì AutoCAD đã có sẵn.
3. Chọn cấu hình `Release`, build. Thư mục bin chỉ có `DwgToDxf.dll`. Kết quả: `src/DwgToDxf/bin/Release/net48/DwgToDxf.dll`.

Hoặc dùng dòng lệnh: `dotnet build src/DwgToDxf/DwgToDxf.csproj -c Release`.

## Nạp vào AutoCAD

1. Mở AutoCAD 2023, gõ `NETLOAD`, chọn `DwgToDxf.dll`.
2. Gõ `DWG2DXF`, `INMODEL` hoặc `INLAYOUT`.

Nếu AutoCAD cảnh báo bảo mật khi nạp DLL, thêm thư mục chứa DLL vào `TRUSTEDPATHS` (Options > Files > Trusted Locations).

Để tự nạp mỗi lần mở AutoCAD, có thể thêm dòng sau vào `acad.lsp` hoặc `acaddoc.lsp`:

```lisp
(command "_.NETLOAD" "C:/duong/dan/DwgToDxf.dll")
```

## Ghi chú

- Addin dùng .NET Framework 4.8 vì AutoCAD 2023 chạy trên nền này. AutoCAD 2025 trở đi chuyển sang .NET 8 và cần build lại với gói AutoCAD.NET tương ứng.
- Phá block làm mất thông tin "đây là block gì". Nếu cần giữ block, trả lời `No` ở câu làm sạch.
- File DWG đang được người khác mở vẫn đọc được (chế độ chia sẻ đọc). File hỏng hoặc có mật khẩu sẽ được ghi vào nhật ký lỗi và bỏ qua.

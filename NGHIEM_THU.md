# Nghiệm thu AutocadAddin trên máy có AutoCAD 2023

Phần build và kiểm thử tự động chạy được trên máy chủ. Phần dưới đây cần AutoCAD thật nên phải thử trên máy của bạn. Mỗi mục ghi Đạt / Không đạt, nếu không đạt gửi lại file `DWG2DXF_*.log` và file DWG mẫu.

## Chuẩn bị

1. Cài gói theo mục "Cài đặt" trong README (chạy `install.cmd`, mở AutoCAD 2023).
2. Tạo thư mục thử, ví dụ `D:\ThuDXF`, chép vào vài bản vẽ thật, và nếu có:
   - một bản vẽ có xref (cả xref tìm thấy và xref bị mất file),
   - một bản vẽ có block có attribute, block lồng block có attribute, dynamic block,
   - một bản vẽ có layer bị khóa chứa block,
   - một bản vẽ có block chèn trên layer khác 0 mà bên trong vẽ màu ByBlock,
   - một thư mục con có DWG, một file DWG hỏng (đổi tên một file .txt thành .dwg).

## A. DWG2DXF

| # | Thao tác | Kết quả mong đợi |
|---|---|---|
| 1 | Mở AutoCAD, gõ `DWG2DXF` | Lệnh chạy, hiện hộp chọn thư mục (không cần NETLOAD). |
| 2 | Chọn `D:\ThuDXF`, Enter hết các câu hỏi | DXF 2018 nằm cạnh từng DWG; dòng lệnh báo "Đã chuyển n/n file"; có file `DWG2DXF_*.log`. |
| 3 | Mở log | Mỗi file OK có dòng `audit: xong` (không còn "API khong co"), `xref`, `block: da pha ...`, `purge: ...`. File hỏng ghi `LOI` và các file khác vẫn chạy. |
| 4 | Chạy lại, chọn `Yes` thư mục con, `Other` thư mục đích `D:\ThuDXF_OUT` | DXF nằm trong `D:\ThuDXF_OUT` với cùng cấu trúc thư mục con. |
| 5 | Mở từng DXF bằng AutoCAD | Hình giống DWG gốc (so chồng hoặc nhìn bằng mắt): không mất nét, không trùng nét, chữ attribute đúng giá trị và vị trí. |
| 6 | Trong DXF, gõ `XREF` | Không còn xref; xref mất file có tên trong log. |
| 7 | Trong DXF, gõ `QSELECT` lọc Block Reference | Không còn block (trừ TABLE và block ghi "giu nguyen" trong log). |
| 8 | Bản vẽ có layer khóa | Block trên layer khóa đã được phá, layer vẫn khóa trong DXF, không có hình trùng. |
| 9 | Block lồng có attribute | Chữ attribute của block con có trong DXF. Nếu log ghi "khong chep duoc attribute cua block con", báo lại kèm file mẫu. |
| 10 | Block chèn trên layer khác, nét bên trong màu ByBlock | Màu nét trong DXF giống màu khi xem DWG gốc. |
| 11 | Gõ `-DWG2DXF`, nhập đường dẫn có dấu nháy và khoảng trắng | Chạy như bước 2. Đường dẫn sai báo "Không tìm thấy thư mục". |
| 12 | Chạy script `.scr` mẫu trong README | Chạy hết không hỏi gì thêm. |
| 13 | Nhấn `Esc` giữa chừng với thư mục nhiều file | Dừng, log ghi "Nguoi dung da huy". |
| 14 | Chọn `R12` và `No` ở câu làm sạch | Ra DXF R12, block giữ nguyên. |
| 15 | Mở DXF bằng phần mềm đích (LibreCAD, phần mềm dự toán...) | Đọc được, đúng hình. |

## B. In ấn

Chuẩn bị thêm: một bản vẽ có nhiều khung tên (block) trong Model, một bản vẽ có 2 layout trở lên trong đó có layout chưa từng mở, một thư mục 3 đến 5 bản vẽ.

| # | Thao tác | Kết quả mong đợi |
|---|---|---|
| 16 | `CAIDATIN`, tạo cấu hình "A3 PDF": Chọn khung mẫu, máy in `DWG To PDF.pc3`, khổ A3, `monochrome.ctb`, Lưu | Đóng AutoCAD mở lại, `CAIDATIN` vẫn còn cấu hình. |
| 17 | `INMODEL` → Enter → Enter | Hiện số thứ tự đỏ trên từng khung; `Hang`/`Cot`/`Duoi`/`Bam`/`Sua` đổi đúng thứ tự. |
| 18 | Enter để in | Một file PDF nhiều trang, mỗi khung một trang đúng thứ tự, khung ngang ra trang ngang, khung dọc ra trang dọc. |
| 19 | `INLAYOUT` trên layout một khung và layout nhiều khung | Một khung: in cả layout. Nhiều khung: chọn và in từng khung. |
| 20 | `INNHIEUFILE`, chọn thư mục, "Model và các layout", bật gộp PDF, In nhiều file | Mỗi bản vẽ một PDF, file `_TongHop.pdf` đủ trang, log `InNhieuFile_*.log`. Layout chưa từng mở in ra có hình (không trắng). Bản vẽ đang mở vẫn ở layout cũ sau khi in. |
| 21 | `VEVUNGIN` quét 2 vùng, rồi `INMODEL` | Vùng tím không ra giấy, lệnh in nhận đúng 2 vùng. |
| 22 | `DANHSOBV` với tiền tố `KC-`, 2 chữ số, có tổng số | Khung tên ghi `KC-01/05`... đúng thứ tự; `UNDO` hoàn tác được. |
| 23 | `DANHMUCBV` | Bảng danh mục đúng STT, ký hiệu, tên theo thứ tự in. |
| 24 | In ra máy in giấy thật (nếu có) | Gửi đúng số trang, đúng khổ. |

## Kết luận

Khi các mục 1 đến 7, 15 đến 18 và 20 đạt là có thể đưa vào dùng hằng ngày. Các mục 3, 8, 9, 10 và 20 kiểm tra phần vừa sửa trong đợt này (audit, layer khóa, attribute block lồng, màu ByBlock, in layout chưa mở), cần xác nhận trên bản vẽ thật.

Bản AutoCAD 2010 (thư mục `AutoCAD2010`): nếu còn dùng, thử lại các mục 1, 2, 5, 16 đến 18 sau khi nạp bằng `AutocadAddin.lsp`.

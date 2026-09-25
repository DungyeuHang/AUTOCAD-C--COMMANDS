# Dự án Lệnh tùy chỉnh cho AutoCAD (C#)

Đây là một bộ sưu tập các lệnh C# cho AutoCAD nhằm mục đích tự động hóa và tăng tốc các tác vụ vẽ kỹ thuật thường ngày.

## Danh sách Lệnh

---

### 1. Nhóm Smart Stretch

*   **File:** `SmartStretch/SmartStretchCommands.cs`
*   **Mục đích:** Thực hiện thao tác STRETCH một cách thông minh, tự động xác định hướng và tái sử dụng khoảng cách.

#### `SS` (SmartStretch)
*   **Chức năng:** Stretch đối tượng theo một khoảng cách (L) đã định trước.
*   **Cách hoạt động:**
    1.  Lệnh sử dụng giá trị `L` từ lần chạy gần nhất (được lưu lại).
    2.  Tại dấu nhắc, người dùng có thể gõ `L` (Length) để nhập `L` mới, hoặc `C` (Calculator) để lấy giá trị từ palette Quick Calculator.
    3.  Người dùng quét một hoặc nhiều vùng chọn crossing window. **Giữ Shift** khi quét để loại bỏ đối tượng.
    4.  Sau khi chọn xong đối tượng, người dùng chọn một điểm gốc.
    5.  Rê chuột từ điểm gốc để xác định hướng (ngang/dọc). Lệnh sẽ tự động chọn trục có độ lệch lớn nhất (SX+, SX-, SY+, SY-).
    6.  Click để xác nhận. Lệnh sẽ gọi `STRETCH` gốc của AutoCAD với các tham số đã được tính toán.
*   **Lưu ý:** Lệnh sẽ lặp lại cho đến khi người dùng nhấn Enter/Space hoặc Esc.

#### `SSD` (SmartStretch by Dimension)
*   **Chức năng:** Stretch đối tượng với khoảng cách `L` bằng chênh lệch giữa 2 dimension.
*   **Cách hoạt động:**
    1.  Lệnh yêu cầu chọn 2 đối tượng `Dimension`.
    2.  Tính `L = |Measurement1 - Measurement2|`.
    3.  Sau đó, quy trình stretch hoạt động tương tự như lệnh `SS`.

#### `SSD2` (SmartStretch by Half Dimension Difference)
*   **Tên lệnh đầy đủ:** `SSD2_SMART_STRETCH_BY_DIM2`
*   **Chức năng:** Stretch đối xứng từ tâm, với `L` bằng một nửa chênh lệch giữa 2 dimension.
*   **Cách hoạt động:**
    1.  Lệnh yêu cầu chọn 2 đối tượng `Dimension`.
    2.  Tính `L = |Measurement1 - Measurement2| / 2`.
    3.  Lệnh sẽ chạy 2 lần (2 passes). Người dùng sẽ cần chọn vùng và hướng cho mỗi lần. Hữu ích khi cần stretch đều cả hai phía của một đối tượng đối xứng.

---

### 2. Nhóm Copy vào tâm

*   **File:** `Commands/SmartCopyToCenterCommands.cs`
*   **Mục đích:** Sao chép hoặc chèn đối tượng vào tâm của một vùng kín.

#### `CCC` (Smart Copy To Center)
*   **Tên lệnh đầy đủ:** `CCC_SMART_COPY_TO_CENTER`
*   **Chức năng:** Sao chép một nhóm đối tượng vào tâm của một hoặc nhiều vùng kín.
*   **Cách hoạt động:**
    1.  Chọn các đối tượng nguồn. Hỗ trợ `PickFirst` (chọn trước rồi gọi lệnh).
    2.  Lệnh tính toán tâm hình học của nhóm đối tượng nguồn (có **bỏ qua Dimension và Text** để lấy tâm chính xác hơn).
    3.  Người dùng click vào bên trong các vùng kín (closed boundary).
    4.  Lệnh sẽ sao chép nhóm đối tượng nguồn vào tâm của từng vùng được click.
*   **Lưu ý:** Lệnh có 2 cơ chế tìm tâm vùng đích: một thuật toán "nhanh" bằng cách quét 4 tia, và fallback về `Editor.TraceBoundary()` nếu cách nhanh thất bại.

#### `BBB` (Block To Center)
*   **Tên lệnh đầy đủ:** `BBB_BLOCK_TO_CENTER`
*   **Chức năng:** Chèn một block (chọn từ danh sách) vào tâm của một hoặc nhiều vùng kín.
*   **Cách hoạt động:**
    1.  Lệnh hiển thị một Form danh sách các block definition có trong bản vẽ.
    2.  Người dùng chọn một block từ danh sách.
    3.  Người dùng click vào bên trong các vùng kín.
    4.  Lệnh sẽ chèn block đã chọn vào tâm của từng vùng được click.

---

### 3. Nhóm Auto Dimension

*   **File:** `Commands/AutoDimCommand.cs`
*   **Mục đích:** Tự động tạo các đối tượng Dimension.

#### `DAA` (Dim Auto)
*   **Tên lệnh đầy đủ:** `DAA_Dim_auto`
*   **Chức năng:** Tạo dimension từ một mốc tham chiếu tới 4 đường bao gần nhất.
*   **Cách hoạt động:**
    1.  Chọn mốc tham chiếu: có thể là `Object` (đối tượng) hoặc `Point` (điểm). Lựa chọn này được lưu lại.
    2.  Chọn các đường bao đích (Line/Polyline).
    3.  Lệnh tự tìm các đường bao gần nhất ở 4 phía (trái, phải, trên, dưới) và tạo dimension tương ứng.

#### `DDD` (Dim 4 Directions)
*   **Tên lệnh đầy đủ:** `DDD_Dim_4_direction`
*   **Chức năng:** Tạo dimension từ một đối tượng gốc ra 4 phía tới các đối tượng gần nhất.
*   **Cách hoạt động:**
    1.  Chọn đối tượng gốc. Hỗ trợ `PickFirst`.
    2.  Lệnh cho phép lọc đối tượng đích theo: `Loại` (Line/Polyline/Block), `Layer`, và `Closed` (với Polyline). Bộ lọc này được lưu lại cho các lần dùng sau.
    3.  Lệnh quét 4 hướng từ extents của đối tượng gốc, tìm các đối tượng đích phù hợp gần nhất và tạo dimension.
    4.  Hiển thị cảnh báo nếu các dim đối xứng (ngang/dọc) không bằng nhau.

#### `BD` (Change Dimension Placement)
*   **Chức năng:** Thay đổi điểm đặt (dimension line location) của nhiều dimension cùng lúc.
*   **Cách hoạt động:**
    1.  Chọn các đối tượng `Dimension`. Hỗ trợ `PickFirst`.
    2.  Chọn một điểm đặt mới.
    3.  Lệnh sẽ di chuyển điểm đặt của tất cả các dimension đã chọn về vị trí mới này.
*   **Lưu ý:** Sử dụng Reflection để tương thích với nhiều loại dimension khác nhau (`RotatedDimension`, `AlignedDimension`...).

#### `DPA` (Dim Auto Pline)
*   **File:** `AutoDimPline/` (module riêng, tách hẳn khỏi `Commands/AutoDimCommand.cs`)
*   **Tên lệnh đầy đủ:** `DPA_DimAutoPline`. Muốn xem bản bố trí in ra dòng lệnh thì đặt `Verbose = true` trong file cài đặt `autodimpline_settings.tsv`.
*   **Chức năng:** Tự động tạo dimension cho một Polyline **và tự bố trí sao cho sạch, không chồng chéo**.
*   **Kiến trúc:** `ANALYZE → PLAN → CREATE` — toàn bộ va chạm và xếp chồng được giải quyết trong bộ nhớ **trước khi** tạo entity đầu tiên. Lớp phân tích + bố trí (`DimPline*.cs`) không tham chiếu AutoCAD nên test được ngoài AutoCAD.
*   **Cách hoạt động:**
    1.  Bảng cài đặt hiện ra trước: `Linear scale (DIMLFAC)`, `Dim layer`, `Distance from PL`, `Dim spacing`, `Min segment`, `Auto layout`, `Create overall dimensions`, `Dim sát feature`, `Side preference`, `Arc segments`, `Skew segments`. Cài đặt được lưu ở AppData.
    2.  Chọn một `Polyline` (2D, mặt phẳng XY). Hỗ trợ cả kín và hở; polyline "kín bằng mắt" (đỉnh cuối trùng đỉnh đầu) được nhận là kín thật.
    3.  `DimPlineAnalyzer` gộp các đoạn thẳng hàng, loại đoạn dài 0, phân loại ngang/dọc/xiên theo **ngưỡng góc** (không phải ngưỡng toạ độ), tính pháp tuyến hướng ra ngoài (biên dạng kín) và bao hình chính xác kể cả cung.
    4.  `DimPlinePlanner` chấm điểm cả hai phía khả dĩ cho từng đoạn (chiều dài đường gióng + số lần cắt qua nét vẽ + pháp tuyến ngoài + cân bằng hai phía), rồi:
        *   feature sát mép ngoài → xếp vào **băng** ngoài bao hình, chia hàng bằng thuật toán xếp khoảng: dim nối tiếp nhau (dạng chuỗi) vẫn chung một hàng như dim tay, dim chồng lấn nhau bị đẩy ra hàng ngoài;
        *   feature sâu bên trong → dim **ngay cạnh feature** nếu kiểm tra thấy đường kích thước và cả hai đường gióng không cắt nét vẽ và text không chạm dim khác; không đạt thì tự động quay về băng ngoài.
    5.  Dim bao tổng thể X/Y luôn nằm ở hàng **ngoài cùng** của phía nó, cách tầng feature một khoảng hở riêng.
    6.  Dim trùng lặp (cùng phương, cùng khoảng đo) bị loại — hình chữ nhật chỉ sinh ra 2 dim chứ không phải 4.
*   **Lưu ý:**
    *   `DIMLFAC` chỉ đổi **giá trị hiển thị**; mọi phép tính vị trí vẫn dùng drawing unit thực (không nhầm với `DIMSCALE`).
    *   Khoảng cách / bước xếp hàng được nâng lên theo `DIMTXT * DIMSCALE` thực tế nếu người dùng nhập quá nhỏ, nên text không bao giờ đè nhau vì cấu hình sai.
    *   Đoạn cung mặc định **bị bỏ qua và báo lại số lượng** (không bao giờ biến hình cong thành dim thẳng sai nghĩa); có thể bật sang `Radius dimension`. Đoạn xiên mặc định tạo `Aligned dimension`.
    *   Lệnh **không sửa polyline gốc** (không đảo chiều) và **không sửa DIMSTYLE** của bản vẽ — chỉ `DIMLFAC` được ghi đè riêng trên từng dim mới tạo.
    *   Mọi dim nằm trong 1 transaction: lỗi giữa đường → không commit → bản vẽ không còn dim rác.

---

### 4. Nhóm xử lý Polyline

#### `CAA` (Change Polyline)
*   **File:** `Commands/ChangePolylineCommand.cs`
*   **Tên lệnh đầy đủ:** `CAA_change_pline`
*   **Chức năng:** Chuẩn hóa một polyline (đóng/mở, hướng, điểm bắt đầu).
*   **Cách hoạt động:**
    1.  Chọn một `Polyline`.
    2.  Lệnh cho phép vào `Settings` để chọn chế độ `Close`/`Skip` và hướng `CCW` (ngược chiều kim đồng hồ) / `CW` (cùng chiều). Cài đặt được lưu lại.
    3.  Yêu cầu người dùng chọn một điểm trên polyline để làm điểm bắt đầu mới.
    4.  Lệnh sẽ thay đổi polyline theo các tùy chọn đã xác định.
*   **Lưu ý:** Với polyline hở, chỉ cho phép chọn 1 trong 2 đầu mút làm điểm bắt đầu để không thay đổi hình dạng.

#### `UFF` (Un-Fillet Polyline)
*   **File:** `Commands/UnFilletPolylineCommand.cs`
*   **Chức năng:** Loại bỏ các cung tròn (fillet/arc) trên một polyline, biến chúng thành các góc nhọn.
*   **Cách hoạt động:**
    1.  Chọn một `Polyline`.
    2.  Lệnh duyệt qua các segment. Với mỗi segment là cung tròn (có `bulge`), nó sẽ tìm giao điểm của 2 segment thẳng kề và thay thế cung tròn bằng giao điểm đó.
    3.  Tạo ra một polyline mới đã được "un-fillet" trên layer `_mss.phantom`, không chỉnh sửa polyline gốc.

#### `APOINT` (Make Points by Polyline)
*   **File:** `Commands/APointCommand.cs`
*   **Chức năng:** Tạo các điểm (Circle + Text) tại mỗi đỉnh của một polyline và tạo một MText tổng hợp.
*   **Cách hoạt động:**
    1.  Chọn một `Polyline`.
    2.  Nhập một chuỗi tiền tố (prefix), ví dụ `bl_fr`.
    3.  Lệnh tạo Circle và MText tại mỗi đỉnh, với nội dung dạng `prefix_p1 = APoint(x, y)`.
    4.  Tạo một MText lớn tổng hợp tất cả các dòng định nghĩa điểm và một dòng `smart_pl(...)` mô tả toàn bộ polyline.
*   **Lưu ý:** Các đối tượng được tạo trên layer `_mss.phantom`.

---

### 5. Lệnh xử lý Text

#### `TT` (Text Sync)
*   **File:** `Commands/TextSyncCommands.cs`
*   **Tên lệnh đầy đủ:** `TT_TEXT_CHANGE_5`
*   **Chức năng:** Đồng bộ nội dung của nhiều đối tượng text theo một text mẫu, chỉ áp dụng cho các text có chiều cao bằng 5.
*   **Cách hoạt động:**
    1.  Chọn một text mẫu (`DBText` hoặc `MText`).
    2.  Chọn các đối tượng text đích. Hỗ trợ `PickFirst`.
    3.  Lệnh sẽ lọc ra các text đích có `Height` hoặc `TextHeight` xấp xỉ 5.0.
    4.  Thay đổi nội dung của các text đã lọc cho giống với text mẫu.
*   **Lưu ý:** Lệnh cố gắng giữ lại định dạng của `MText` khi sao chép.

#### `SLL` (Change SL theo số bộ)
*   **File:** `Commands/SllChangeSlBoCommands.cs`, `Commands/SllChangeSlBoForm.cs`
*   **Tên lệnh đầy đủ:** `SLL_CHANGE_SL_BO`
*   **Chức năng:** Đổi số lượng trong TEXT/MTEXT theo tỉ lệ số bộ gốc -> số bộ mới (`newSL = originalSL / originalBundles * newBundles`), hỗ trợ **nhiều cấu trúc SL đầu vào** cùng lúc (vì phụ kiện từ các nguồn khác nhau có thể ghi SL khác kiểu nhau trong cùng 1 vùng chọn) và **một cấu trúc SL đầu ra** duy nhất.
*   **Cách hoạt động:**
    1.  Lệnh mở bảng nhập (`SllChangeSlBoForm`, qua `Application.ShowModalDialog`), chia 3 khu vực: "SỐ LƯỢNG BỘ" (Số bộ gốc/mới, số nguyên dương), "CẤU TRÚC SL ĐẦU VÀO" (danh sách động, mỗi dòng có nút "−" để xoá và nút "+ Thêm cấu trúc" để thêm dòng mới, luôn giữ tối thiểu 1 dòng), "CẤU TRÚC SL ĐẦU RA" (1 ô duy nhất). Dùng `{X}` làm placeholder cho số lượng (vd `SL: {X}`, `SL{X}`, `(SL: {X})`). Các ô cấu trúc là ComboBox gợi ý lại các cấu trúc đã dùng gần đây (lưu qua `WorkspaceUiStateStore`, key `sll_change_sl_bo.recent_formats`, tối đa 10 mục). Toàn bộ bảng nhập (số bộ gốc/mới, danh sách cấu trúc đầu vào, cấu trúc đầu ra) tự điền lại y hệt lần chạy gần nhất của người dùng (key `sll_change_sl_bo.last_*`); nếu chưa từng chạy thì mặc định là `SL: {X}` cho cả 2 phía và 1 bộ. Bảng tự validate trước khi cho bấm "CHỌN ĐỐI TƯỢNG" (số nguyên dương, mỗi cấu trúc không rỗng và chứa đúng 1 placeholder `{X}`; cấu trúc đầu vào trùng nhau được gộp lại, không báo lỗi).
    2.  Quét chọn đối tượng. Chỉ xử lý `DBText`/`MText` trong vùng chọn, các loại khác bị bỏ qua.
    3.  Với mỗi text, thử lần lượt các cấu trúc đầu vào — **ưu tiên cấu trúc cụ thể hơn trước** (sắp theo độ dài chuỗi cấu trúc giảm dần, cấu trúc dài/nhiều ký tự cố định hơn được thử trước; bằng độ dài thì giữ thứ tự người dùng nhập) — dùng cấu trúc ĐẦU TIÊN khớp; nếu không có cấu trúc nào khớp thì bỏ qua text đó. Sau khi khớp, tính lại số theo tỉ lệ rồi sinh ra theo cấu trúc đầu ra; toàn bộ phần còn lại của text được giữ nguyên.
    4.  Ghi trực tiếp giá trị mới vào entity đó — không dùng FIND/REPLACE nên không bị cascading (mỗi text luôn tính từ giá trị SL gốc của chính nó, và text vừa sinh ra không bị xử lý lại trong cùng lần chạy).
*   **Lưu ý:** Mỗi cấu trúc (đầu vào lẫn đầu ra) phải chứa đúng 1 placeholder `{X}`, nếu không lệnh sẽ báo lỗi và không cho tiếp tục. Nếu SL gốc không chia hết cho số bộ gốc, đối tượng đó bị bỏ qua và lệnh báo lỗi thay vì sửa sai. Lệnh không tự quét toàn bản vẽ — chỉ xử lý các đối tượng đã chọn.

---

### 6. Ghép phôi tự động (nesting tôn tấm)

#### `GHOPHOI` (Ghép phôi)
*   **File:** `Nesting/` (module riêng). Lõi thuật toán `Nesting/Core/` và nhận dạng `Nesting/Recognition/` **không tham chiếu AutoCAD** (test được ngoài AutoCAD, chạy được trên luồng phụ).
*   **Chức năng:** Ghép các chi tiết tôn (đường bao kín + lỗ) lên khổ phôi đã duyệt của công ty, theo số lượng (SL) và vật liệu đọc từ TEXT/MTEXT, rồi xuất ra **một bản vẽ MỚI**. Bản vẽ gốc chỉ được đọc.
*   **Quy trình:** `chọn đối tượng → nhận dạng → bảng KIỂM TRA → bảng CÀI ĐẶT → ghép (luồng phụ, có nút Dừng) → VALIDATOR → bảng kết quả → TẠO BẢN VẼ MỚI`.
*   **Nhận dạng hình:**
    1.  Hỗ trợ LINE, ARC, CIRCLE, LWPOLYLINE (có bulge, kể cả normal −Z sau MIRROR), POLYLINE 2D/3D, ELLIPSE, SPLINE và BLOCK (explode trong bộ nhớ, block vẫn là 1 đối tượng nguồn).
    2.  Đầu mút gần nhau hơn `JoinToleranceMm` (0.05 mm) được nối thành đường bao. Vòng kín theo độ sâu lồng nhau: chẵn = chi tiết, lẻ = lỗ; chi tiết vẽ nằm trong lỗ của chi tiết khác vẫn là chi tiết riêng.
    3.  Đường hở nằm trong chi tiết (vd. đường chấn) được giữ theo chi tiết khi xuất nhưng không dùng để ghép. Hình trên layer đánh dấu (`MarkingLayers`, mặc định `_mss.dut` — layer đường chấn của `DX_FOIL`) luôn được coi là đánh dấu, không bao giờ là lỗ.
    4.  **Dọn rác tự động (chỉ những thứ chắc chắn không phải chi tiết):** hình **hở** nằm ngoài mọi chi tiết được bỏ qua và **đếm lại** (`N nhóm hình HỞ ... đã bỏ qua`). Nhưng nếu hai đầu hở **gần nhau** so với kích thước của chính nó (≤ 5% đường chéo hộp bao) thì đó là một đường bao **định vẽ kín mà bị hở khe** — giữ lại và báo lỗi kèm toạ độ, vì đó chính là thứ cần sửa. Hai đường bao **vẽ trùng khít** lên nhau được **gộp làm 1** (báo `đã gộp làm 1`) thay vì làm hỏng cả chi tiết.
    5.  **Không bao giờ đoán:** đường bao hở, tự cắt, rẽ nhánh, diện tích 0, lỗ chạm biên, 1 block chứa nhiều chi tiết → bản ghi `INVALID GEOMETRY` có lý do và tọa độ.
*   **Chữ CẮT nằm trong phôi — tự đi theo phôi:** TEXT / MTEXT nằm **bên trong đường bao** mà **không đọc ra SL / vật liệu** (mã chi tiết như `P-L-347-566-1`, `H`) được coi là **chữ cắt trên chính chi tiết đó**: nó đi theo chi tiết vào bản vẽ mới và **nhận đúng phép biến hình** (xoay / lật / tịnh tiến) của phôi, vì nó nằm trong **block** của phôi. Không cần đổi layer — trên bản vẽ thật chữ cắt vốn nằm ngay trên layer đường bao, đúng bản chất của nó. Chữ **đọc ra được SL / vật liệu** thì là **thông tin**: chỉ đọc, **không** mang đi cắt. Chữ nằm **ngoài** mọi đường bao cũng không đi theo phôi nào.
*   **Khắc chữ lên chi tiết (`_mss.khac`):** TEXT / MTEXT nằm trên layer `EngravingLayers` (mặc định **`_mss.khac`**) được coi là **hình khắc**: nó đi theo chi tiết vào bản vẽ mới và **nhận đúng phép biến hình** (tịnh tiến / xoay / lật) của chi tiết đó, vì nó được đưa thẳng vào **block** của chi tiết — không phải "cộng thêm độ dời". Hình khắc **không bao giờ** tham gia tính va chạm hay ghép, và **không** được đọc làm SL / vật liệu. Chữ trên layer đường bao vẫn chỉ là **thông tin** (`SL: 2`, `1.2MM`). Phân loại **theo layer, không đoán theo nội dung**: trên bản vẽ thật, `SL: 2` và mã chi tiết `P-L-347-566-1` nằm chung một layer và không có dấu hiệu nào trong bản thân chữ để phân biệt. Chữ khắc **không nằm trong chi tiết nào** thì được báo ra (`N chữ trên layer khắc ... không nằm trong chi tiết nào`) chứ không bị nuốt im lặng. Chi tiết **chưa xếp được** vẫn giữ hình khắc, vì nó dùng chung định nghĩa block. Lưu ý: mã chi tiết chuyển sang `_mss.khac` thì **không còn được dùng làm tên chi tiết** trong bảng kiểm tra nữa.
*   **Đọc SL / vật liệu:** `SL: 12`, `SL:12`, `SL 12`, `sl: 12`, `SL=12`; `1.2MM`, `1.2 MM`, `1,2MM`, `1,2 MM`, `1.2mm`. Độ dày ngoài 0.3–25 mm bị bỏ qua (để `150MM` không bị hiểu là vật liệu). Mẫu regex nằm trong `MetadataRules`.
    *   Thiếu SL → 1, thiếu vật liệu → `1.2MM` (hiện `WARNING` để người dùng thấy).
    *   Gán text: nằm trong đường bao → chi tiết đó; nếu không → chi tiết gần nhất trong `MaxTextDistanceMm` (300 mm); gần 2 chi tiết thì **cái gần hơn thắng**; chỉ khi hai khoảng cách chênh nhau **dưới 1%** (hoặc dưới 0,5 mm) mới thực sự là `AMBIGUOUS`; 2 SL khác nhau cho 1 chi tiết → `AMBIGUOUS`.
*   **Bảng kiểm tra:** `Chi tiết | SL | Vật liệu | Trạng thái (OK / WARNING / AMBIGUOUS / INVALID GEOMETRY)`. Chọn dòng → zoom + highlight hình trong bản vẽ. Sửa được SL / vật liệu. Dòng `AMBIGUOUS` phải xác nhận; nếu có dòng lỗi hình học phải tick xác nhận bỏ qua thì mới bấm TIẾP TỤC được.
*   **Tự chọn khổ đủ lớn:** bảng cài đặt biết kích thước chi tiết lớn nhất của từng vật liệu, nên nếu khổ đang chọn **nhỏ hơn chi tiết** thì nó **tự đổi sang khổ nhỏ nhất trong danh mục mà chứa được**. Không còn cảnh chạy xong mới thấy "chưa xếp" mà không hiểu vì sao — chi tiết dài 2930 mm thì không khổ 2500 nào xếp được, phải là khổ 6000.
*   **Nhãn tên chi tiết — MẶC ĐỊNH TẮT:** nhãn này là chữ **do GHOPHOI tự đặt ra** (`"P11 D-D-347-566-1"` = số thứ tự của chương trình ghép với chữ của người dùng), vẽ vào **tâm** chi tiết với cỡ chữ tự tính (tới 15 mm). Trên bản vẽ thật người dùng nhìn thấy nó và tưởng chương trình đã **sửa chữ của mình rồi phóng to mang ra giữa** — trong khi chữ gốc vẫn nằm yên chỗ cũ. Không ai yêu cầu cái nhãn này nên không bật sẵn; muốn có thì tick *"Ghi tên chi tiết"*. Khi bật, chi tiết **đã mang chữ khắc của chính bạn** (`_mss.khac`) vẫn **không** bị vẽ thêm nhãn.
*   **Thanh tiến trình:** chạy **0 → 100%** theo số lượt ghép thật (mỗi vật liệu × mỗi thứ tự xếp × 2 chính sách đặt), kèm phần trăm. Các lượt chạy song song nên báo về không đúng thứ tự — thanh lấy giá trị lớn nhất nên không bao giờ lùi.
*   **Cài đặt:** khổ phôi cho từng vật liệu (chỉ chọn trong danh mục), khe cắt (mặc định 5 mm), lề mép (5 mm), hướng xoay (0/90/180/270, 0/180, không xoay), lật gương (**mặc định TẮT** — lật đổi chiều chi tiết chấn), cho phép đặt chi tiết vào **lỗ kín** của chi tiết khác (`AllowPartInsideHole`, **mặc định TẮT**; hốc lõm hở L/U/C luôn được phép), thời gian tối đa, seed, xuất dạng block, ghi tên chi tiết, lưu fixture test.
    *   Danh mục khổ phôi: `%AppData%\DUNGX\AUTOCAD_COMMANDS\ghophoi_sheets.tsv` (`Tên<TAB>Rộng<TAB>Dài<TAB>Vật liệu`), sửa được ngay trên bảng cài đặt. Lần đầu được tạo sẵn 1250x2500, 1500x3000, 1500x6000 để sửa lại theo khổ thật của xưởng.
*   **Thuật toán V1:** candidate-point + va chạm đa giác thật (hộp bao chỉ để lọc nhanh) + trượt ép trái/xuống; nhiều thứ tự xếp tất định (diện tích, cao, rộng, cạnh dài, kết hợp + vài thứ tự nhiễu theo seed) × 2 chính sách đặt; so sánh: ít chi tiết chưa xếp → ít tờ → tổng chiều dài dùng ngắn → gọn hơn. Mỗi vật liệu ghép riêng. Chi tiết không vừa tờ trống ở mọi hướng → `CHƯA XẾP` kèm lý do. Không cắt tỉa ứng viên X (đã kiểm chứng: cắt tỉa cũ làm kết quả kém hơn ở 7/300 case ngẫu nhiên). Các thứ tự chạy song song trên nhiều nhân nhưng chọn kết quả theo đúng thứ tự → kết quả giống hệt chạy tuần tự. Không có NFP / GA / SA (để V2).
*   **Khe hở / lề mép (khoảng cách THỰC TẾ, độc lập):** chi tiết ↔ chi tiết ≥ `Gap`, chi tiết ↔ mép tờ ≥ `EdgeMargin` (mặc định 5 / 5 mm). Không có quy tắc cộng dồn. Chi tiết chỉ gồm đoạn thẳng là đa giác chính xác (dung sai 0); chi tiết có cung / đường cong được xấp xỉ với sai số dây cung ≤ 0.05 mm và dung sai này được cộng vào khoảng hở **của riêng chi tiết đó**. So sánh chính xác đến 0.001 mm (không có "slack").
*   **Validator độc lập:** đo lại từ hình gốc — nằm trong tờ, lề mép, không chồng, không nằm trong lỗ kín (nếu không cho phép), đủ khe, góc xoay/lật hợp lệ, đủ số lượng, không thiếu / trùng / thừa, đúng vật liệu tờ. **Không đạt → không cho tạo bản vẽ.**
*   **Xuất kết quả — hai cách:**
    *   **Vẽ thẳng vào bản vẽ đang mở** (mặc định): chọn **1 điểm đặt**, kết quả được vẽ ngay tại đó. Bỏ tick *"Mỗi chi tiết là 1 BLOCK"* thì mọi thứ được **phá khối**, mỗi đối tượng **giữ nguyên layer của nó** (đường bao ở layer đường bao, chữ khắc ở `_mss.khac`) — nhờ vậy khi xuất đi cắt CNC bạn chọn đúng layer cần cắt, chứ để nguyên block thì máy cắt hết mọi thứ bên trong kể cả chữ không định cắt. Bản vẽ gốc **chỉ bị thêm** đối tượng mới, không sửa hình cũ, và **Ctrl+Z một lần là hết**. Dựng hình dùng lại **đúng** đường đã kiểm ở 8 hướng (dựng bố cục ra bộ nhớ → chèn → phá khối), không viết lại phép biến hình lần thứ hai.
    *   **Bản vẽ mới** (bỏ tick *"Vẽ thẳng vào bản vẽ này"*): lưu cạnh file gốc (`<tên>_GHOPHOI_yyyyMMdd_HHmmss.dwg`, hoặc Documents), tự mở sau khi lệnh kết thúc. Mỗi chi tiết là 1 block chứa **đúng entity gốc** (LINE/ARC… không bị đổi thành polyline), mỗi vị trí là 1 BlockReference (xoay / lật đúng như lõi tính). Mỗi tờ có khung (`GHOPHOI_TO`), nhãn tờ / vật liệu / khổ / % sử dụng / phần dư (`GHOPHOI_TEXT`), vạch phần dư (`GHOPHOI_PHANDU`), tên chi tiết (`GHOPHOI_NHAN`, không in). Các tờ xếp theo hàng, mỗi vật liệu 1 hàng; chi tiết chưa xếp được vẽ riêng bên dưới.
*   **Thời gian tối đa (`TimeBudgetSeconds`) - hiểu cho đúng:** đây là hạn mức để **bắt đầu** một thứ tự xếp mới, **không phải** trần thời gian của cả lần ghép. Một thứ tự đã bắt đầu thì chạy đến hết; chỉ nút **Dừng** mới cắt được nó giữa chừng. Vì các thứ tự chạy song song, trên máy có số nhân ≥ số lần chạy (16 nhân / 16 lần chạy) thì **cả 16 lần đều kịp bắt đầu khi đồng hồ còn gần 0**, nên hạn mức gần như không có tác dụng: đo được trên bản vẽ sản xuất thật (402 chi tiết) là **đặt 15 s, chạy 169,7 s**. Từ nay ít nhất báo cáo sẽ **nói rõ là đã vượt** (`TimeBudgetHit`), thay vì im lặng. Muốn nó thành trần thời gian thật thì phải cho phép cắt ngang một lần chạy đang dở - việc đó làm kết quả phụ thuộc tốc độ máy, nên **chưa** làm.
*   **Kiểm thử:**
    *   `GHOPHOI_TEST` (ẩn trên ribbon/palette): phần A = lõi + nhận dạng, phần B = tầng AutoCAD trên Database trong bộ nhớ (kể cả ghi/đọc lại DWG). Chạy thêm mọi fixture `*.nest` trong `%AppData%\DUNGX\AUTOCAD_COMMANDS\ghophoi_fixtures`.
    *   Ngoài AutoCAD: project `NestingCore.Tests/` (console, không tham chiếu AutoCAD) chạy toàn bộ test lõi + `NestingCore.Tests/Fixtures/*.nest`.
    *   Tick "Lưu fixture test" trong bảng cài đặt để biến bản vẽ thật thành fixture tái lập được.
    *   **Chạy hàng loạt trên bản vẽ thật (không giao diện):** `GHOPHOI` có 4 hộp thoại nên không chạy hàng loạt được. Dùng `GHOPHOI_BATCH` (ẩn) - đi **đúng** đường ống sản xuất (đọc → nhận dạng → ghép → validator → bản vẽ mới), chỉ bỏ phần hộp thoại; cấu hình bằng biến môi trường `GHOPHOI_BATCH_OUT` (bắt buộc), `_GAP` `_MARGIN` `_ROT` `_MIRROR` `_INHOLE` `_BUDGET` `_SEED` `_SHEET` `_DWG` `_NEST`. `GHOPHOI_CADTEST` chạy cả hai phần của `GHOPHOI_TEST` rồi ghi ra file.
    *   Hai lệnh trên nạp được vào `accoreconsole.exe` qua project **`NestingCad.Tests/`** - project này **link chính những file .cs của plugin** (không chép lại dòng nào) và chỉ tham chiếu `accoremgd` + `acdbmgd`. Cần nó vì plugin thật tham chiếu `acmgd` + `AdWindows` và dựng ribbon lúc khởi động, mà `accoreconsole` không có hai assembly đó. Nếu một file trong danh sách bắt đầu cần đến giao diện thì project này hỏng build - đó chính là cảnh báo kiến trúc cần có.
    *   Fixture `real_*.nest` là **hình học sản xuất thật** lấy bằng đường trên, không phải hình tổng hợp. Lưu ý: fixture chỉ ghi lại **đa giác sau nhận dạng**, nên nó chốt được hành vi **ghép**, *không* chốt được hành vi **nhận dạng**.

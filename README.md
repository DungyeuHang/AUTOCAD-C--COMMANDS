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
*   **Tên lệnh đầy đủ:** `DPA_DimAutoPline`
*   **Chức năng:** Tự động tạo dimension cho các cạnh của một Polyline.
*   **Cách hoạt động:**
    1.  Chọn một `Polyline`.
    2.  Một Form cài đặt hiện ra cho phép tùy chỉnh: `Scale factor`, `Offset mul`, `Dim offset mul`, `Orientation` (hướng polyline), và có tạo `Angular dim` hay không. Cài đặt được lưu lại.
    3.  Lệnh tạo các dimension (Rotated, Aligned, Angular) cho các segment của polyline theo cài đặt.

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
*   **File:** `Commands/SllChangeSlBoCommands.cs`
*   **Tên lệnh đầy đủ:** `SLL_CHANGE_SL_BO`
*   **Chức năng:** Đổi số lượng trong TEXT/MTEXT theo tỉ lệ số bộ gốc -> số bộ mới (`newSL = originalSL / originalBundles * newBundles`), với cấu trúc chứa số lượng do người dùng tự định nghĩa (không hardcode `SL: X`).
*   **Cách hoạt động:**
    1.  Nhập "Số bộ gốc" và "Số bộ mới" (số nguyên dương).
    2.  Nhập "Cấu trúc SL hiện tại" và "Cấu trúc SL mong muốn", dùng `{X}` làm placeholder cho số lượng (vd `SL: {X}`, `SL{X}`, `(SL: {X})`). Lệnh gợi ý lại các cấu trúc đã dùng gần đây (lưu qua `WorkspaceUiStateStore`, key `sll_change_sl_bo.recent_formats`) — có thể gõ số thứ tự để dùng lại thay vì gõ lại toàn bộ. Mặc định là cấu trúc gần nhất đã dùng, hoặc `SL: {X}` nếu chưa có lịch sử.
    3.  Quét chọn đối tượng. Chỉ xử lý `DBText`/`MText` trong vùng chọn, các loại khác bị bỏ qua.
    4.  Với mỗi text, tìm phần khớp với cấu trúc hiện tại (ở bất kỳ vị trí nào trong nội dung), tính lại số theo tỉ lệ rồi sinh ra theo cấu trúc mong muốn; toàn bộ phần còn lại của text được giữ nguyên.
    5.  Ghi trực tiếp giá trị mới vào entity đó — không dùng FIND/REPLACE nên không bị cascading (mỗi text luôn tính từ giá trị SL gốc của chính nó).
*   **Lưu ý:** Cấu trúc phải chứa đúng 1 placeholder `{X}`, nếu không lệnh sẽ báo lỗi và yêu cầu nhập lại. Nếu SL gốc không chia hết cho số bộ gốc, đối tượng đó bị bỏ qua và lệnh báo lỗi thay vì sửa sai.

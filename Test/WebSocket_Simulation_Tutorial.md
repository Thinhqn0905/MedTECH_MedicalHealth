# Hướng dẫn chạy Giả lập (Simulator) cho PulseMonitor

Tài liệu này hướng dẫn cách giả lập luồng dữ liệu y tế từ thiết bị ESP32-S3 + MAX30102 bằng Python để phục vụ cho các hoạt động QA Testing trên ứng dụng PulseMonitor App (Windows/Android) mà không cần có phần cứng vật lý.

## 1. Cấu trúc mô phỏng

Firmware của thiết bị thật có 2 luồng dữ liệu song song qua WebSocket:
- **Luồng cao tần (100Hz):** Truyền tín hiệu quang học `red`, `ir`. Ứng dụng dùng luồng này để vẽ biểu đồ xung nhịp (LiveCharts).
- **Luồng thấp tần (1Hz):** Truyền kết quả `bpm`, `spo2`, và đặc biệt là bảng phân tích Edge AI (`sdnn`, `rmssd`, `stress`, `rhythm`).

*⚠️ Lỗi thường gặp: Nếu gửi luồng phân tích AI ở tốc độ 100Hz, ứng dụng MAUI sẽ bị tắc nghẽn UI (ANR - Application Not Responding) do quá tải Render.*

## 2. Cài đặt Python Simulator

### Cài đặt thư viện
Trong thư mục `Test`, mở terminal và chạy lệnh:
```bash
pip install -r requirements.txt
```
*(Thư viện `websockets` nằm sẵn trong tệp)*

### Chạy Server
```bash
python medical_simulator.py
```
Sau khi chạy, máy chủ nội bộ giả lập ESP32 sẽ mở đường truyền WebSocket tại `ws://0.0.0.0:8080`. Địa chỉ này cho phép tất cả các IP trong mạng LAN (kể cả máy ảo Android) kết nối đến.

## 3. Cấu hình App Client (MAUI Android)

Ứng dụng MAUI khi chạy trên Android Emulator sẽ không load `appsettings.json` (do logic code thiết kế chỉ cho Windows). Thay vào đó, app nhớ Cache Cài đặt cũ. Để sửa đổi và kết nối với máy chủ:

1. Chạy Android Emulator và mở App **PulseMonitor**.
2. Nhấn vào nút xanh **Settings** góc dưới cùng.
3. Trong giao diện Setting phần cứng:
   - Thay đổi ô **Connection Mode** (Bluetooth/Websocket/Serial) bằng chữ: `WebSocket`
   - Thay đổi ô **Wi-Fi WebSocket URI** thành: `ws://10.0.2.2:8080` (Bắt buộc dùng `10.0.2.2` để giả lập trỏ vào IP 127.0.0.1 của Windows Host).
4. Quay lại Dashboard, nếu trạng thái là "Disconnected", hãy bấm nút **Connect**.
5. Nhìn màn hình hiển thị nhịp tim (BPM) cập nhật mỗi giây và biểu đồ sóng PPG (LiveChartsCore) nhảy mượt mà 100fps!

> **[Lưu ý cho QA]** Bạn hoàn toàn có thể chỉnh sửa file `medical_simulator.py` (sửa code `math.sin` để đổi hình thù sóng, hoặc sửa biến `sdnn` để thao túng báo cáo Stress Level dùng test độ nhạy của App).

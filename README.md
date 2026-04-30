# MedTech Device — Advanced ECG & AI Diagnostics

Hệ thống thiết bị y tế cầm tay (MedTech) chuyên dụng cho việc theo dõi điện tâm đồ (ECG), phân tích biến thiên nhịp tim (HRV) và chẩn đoán **Rung tâm nhĩ (AF)** thời gian thực bằng công nghệ **Edge AI** ngay trên chip xử lý.

---

## 🚀 Tính năng cốt lõi

- **Real-time 250Hz ECG Streaming**: Thu thập và lọc tín hiệu tim tần số cao, truyền tải mượt mà qua BLE.
- **Advanced DSP Engine**: Bộ lọc số IIR Bandpass (0.5-30Hz) và Notch (50Hz) loại bỏ hoàn toàn nhiễu điện lưới và nhiễu cơ.
- **On-Device AI (Edge Inference)**: Sử dụng mô hình TensorFlow Lite Micro để phát hiện AF ngay trên ESP32-S3. Không cần Cloud, đảm bảo quyền riêng tư.
- **Smart Lead-Off Detection**: Tự động phát hiện và cảnh báo khi các điện cực bị lỏng hoặc rơi ra khỏi cơ thể.
- **Cross-Platform Dashboard**: Ứng dụng di động (.NET MAUI) hiển thị sóng tim thời gian thực, phổ tần số HRV và kết quả chẩn đoán AI.
- **Medical Reporting**: Xuất dữ liệu tim ra file Excel và gửi báo cáo qua Email cho bác sĩ.

---

---

## Tech Stack

- **Vi điều khiển**: ESP32-S3-DevKitC-1 (8MB Flash QIO, 8MB PSRAM OPI)
- **Firmware Framework**: C++ / Arduino Core (PlatformIO)
- **Edge AI**: TensorFlow Lite for Microcontrollers (TFLite Micro)
- **Mobile App**: .NET MAUI (C#)
- **Testing & Validation**: Python 3, `pyserial`, `numpy`

---

## Prerequisites

- [PlatformIO Core (CLI)](https://docs.platformio.org/en/latest/core/index.html) hoặc cài đặt PlatformIO qua VS Code.
- Cáp USB Data Type-C (cắm vào cổng COM/UART của board ESP32-S3).
- [Python 3.9+](https://www.python.org/downloads/) (Dùng cho môi trường Test).

---

## Getting Started (Hướng dẫn cài đặt & chạy lệnh)

### 1. Nạp Firmware (ESP32-S3)

Dự án có hai môi trường build chính trong `platformio.ini`:
- `esp32s3_ecg`: Bản Production (Bật BLE, chạy đầy đủ ECG + AI).
- `stream_test`: Bản Testing (Tắt BLE, chỉ chạy AI để test độ trễ và độ chính xác qua cổng Serial).

Để build và nạp firmware bản **Stream Test** vào cổng COM6:

```bash
cd firmware_ecg

# Nạp bản Production (Bật BLE + AI)
& "$env:USERPROFILE\.platformio\penv\Scripts\pio.exe" run -e esp32s3_ecg -t upload

# Nạp bản Stream Test (Chỉ Test AI qua Serial)
& "$env:USERPROFILE\.platformio\penv\Scripts\pio.exe" run -e stream_test -t upload
```

### 2. Chạy ứng dụng di động (.NET MAUI)

Đảm bảo bạn đã kết nối điện thoại Android (hoặc bật Emulator) và cài đặt .NET 8 SDK.

```powershell
cd PulseMonitor
dotnet build -t:Run -f net8.0-android
```

### 2. Xem Log Khởi động (Monitor Boot)

Rất quan trọng để xác nhận board khởi động thành công và nhận đủ 8MB PSRAM:

```bash
cd firmware_ecg
& "$env:USERPROFILE\.platformio\penv\Scripts\pio.exe" device monitor -e stream_test -p COM6 --baud 115200
```
*(Bấm `Ctrl + C` để thoát monitor).*

### 3. Chạy luồng Test AI (Python Stream Test)

Luồng test sẽ gửi từng gói dữ liệu (cửa sổ RR intervals) từ database mẫu (AFDB) xuống board qua Serial để AI trên ESP32-S3 dự đoán, sau đó gửi trả kết quả và thời gian xử lý (Latency) lên PC.

*Lưu ý: Phải tắt tất cả phần mềm Monitor (PlatformIO/Arduino) đang dùng cổng COM trước khi chạy script này để tránh lỗi `PermissionError: Access is denied`.*

```bash
# Cài đặt thư viện Python (nếu chưa có)
pip install pyserial numpy

# Chạy test
python Test/stream_test_afdb.py --port COM6 --data-dir Test/AFDB_Test/test_4_records/individual_records --max-windows 100
```

---

## 📑 Documentation (Tài liệu chi tiết)

Để hiểu sâu về hệ thống, vui lòng tham khảo các tài liệu chuyên sâu:

- **[Hệ thống Kiến trúc (Architecture)](./Architecture.md)**: Sơ đồ luồng dữ liệu, các tầng xử lý và thiết lập phần cứng.
- **[Giải thuật & AI (Algorithms)](./docs/ALGORITHMS.md)**: Chi tiết về các thuật toán DSP (Pan-Tompkins, IIR) và mô hình Edge AI.
- **[Hướng dẫn Debug & Test](./docs/tasknow.md)**: Nhật ký xử lý lỗi và các bước kiểm thử hệ thống.

---

## Môi trường bộ nhớ cấu hình ESP32-S3 (Hardware Config)

Cấu hình bộ nhớ vô cùng quan trọng đối với ESP32-S3 N8R8 để sử dụng được AI. Nếu cấu hình sai, board sẽ kẹt hoặc khởi động lỗi.
Bắt buộc phải có trong `platformio.ini`:

```ini
board_build.arduino.memory_type = qio_opi
board_build.flash_mode = qio
board_build.flash_size = 8MB
board_build.psram_type = opi
board_build.partitions = huge_app.csv

build_flags =
    -DBOARD_HAS_PSRAM
    -mfix-esp32-psram-cache-issue
```

---

## Troubleshooting

### Lỗi: Python báo `PermissionError: Access is denied`
**Cách xử lý:** 
Cổng COM đang bị một phần mềm khác chiếm dụng. Nhấn `Ctrl + C` ở terminal đang mở `pio device monitor` để giải phóng cổng trước khi chạy lệnh Python.

### Lỗi: Kẹt ở Download Mode `boot:0x0 (DOWNLOAD(USB/UART0))`
**Cách xử lý:**
Python `pyserial` có thể kích hoạt tín hiệu DTR/RTS làm sập board về chế độ nạp code thay vì chạy App. 
- Mở `stream_test_afdb.py`.
- Đảm bảo đã tắt DTR/RTS khi mở port:
  ```python
  ser.setDTR(False)
  ser.setRTS(False)
  ser.open()
  ```
- Trên board phần cứng, bấm nút **RST / EN** một lần để board khởi động lại từ đầu.

### Lỗi: `AllocateTensors failed`
**Cách xử lý:** 
- Mô hình lớn hơn mức RAM nội có thể cấp. Mở `firmware_ecg/src/af_inference.h`.
- Đảm bảo `kTensorArenaSize` được cấp đủ (ví dụ: `250 * 1024` byte).
- Xác nhận PSRAM đã được cấu hình đúng trong `platformio.ini` để `ps_malloc` hoạt động.

---
*Generated by Antigravity AI*

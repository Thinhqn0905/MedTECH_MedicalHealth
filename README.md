# MedTech Device - ECG & AF Detection System

Hệ thống thiết bị y tế đeo tay (Wearable MedTech) tích hợp đo điện tâm đồ (ECG/PPG), phân tích nhịp tim (HRV) và chẩn đoán rung nhĩ (Atrial Fibrillation - AF) theo thời gian thực bằng AI ngay trên phần cứng vi điều khiển (Edge AI). 

Dự án bao gồm firmware chạy trên ESP32-S3, ứng dụng di động theo dõi sức khỏe và các công cụ Python để test độ chính xác của AI.

## Key Features

- **Real-time ECG/PPG Processing**: Đo và lọc nhiễu tín hiệu nhịp tim theo thời gian thực ở tần số 100Hz.
- **On-device AI (Edge Computing)**: Tích hợp mô hình AI TensorFlow Lite Micro để phát hiện rung nhĩ (AF) ngay trên ESP32-S3 mà không cần internet.
- **BLE Streaming**: Truyền dữ liệu sức khỏe (HRV, kết quả AF) về ứng dụng điện thoại qua thư viện NimBLE-Arduino (siêu ổn định).
- **AI Dashboard**: Hiển thị xác suất rung nhĩ (AF Probability) và biểu đồ phổ tần số HRV ngay trên ứng dụng di động.
- **Memory Optimized**: Tối ưu hóa bộ nhớ SRAM (245KB) cho Tensor Arena để AI chạy mượt mà ngay trên board.

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

## Architecture

### Directory Structure

```text
├── firmware_ecg/            # Source code của Firmware ESP32-S3
│   ├── platformio.ini       # Cấu hình build, bộ nhớ, PSRAM
│   └── src/
│       ├── main.cpp         # Entry point, BLE Task, AI Task
│       ├── af_inference.cpp # Xử lý TFLite Micro, cấp phát Tensor Arena
│       ├── af_inference.h   # Cấu hình ngưỡng AF, dung lượng PSRAM
│       └── model_data.h     # Trọng số mô hình AI đã convert sang mảng C
├── PulseMonitor/            # Ứng dụng điện thoại .NET MAUI
├── Model/                   # Mô hình AI gốc (TensorFlow Lite)
├── Test/                    # Dữ liệu mẫu (AFDB) và script Python Test
└── docs/                    # Tài liệu giải thuật, Data flow, và Debug log
```

### Dòng dữ liệu (Data Flow)

1. Cảm biến đọc tín hiệu ECG/PPG (100Hz) qua FIFO.
2. Thuật toán Pan-Tompkins trích xuất các đỉnh R-R (Heart Rate Variability).
3. Đưa mảng R-R vào `HrvAnalyzer` (Bộ đệm vòng).
4. Cứ mỗi 30 nhịp tim, firmware tự động copy mảng này đưa vào `af_inference`.
5. TFLite Micro (`Invoke()`) chạy inference trên mảng dữ liệu.
6. Kết quả `AF Probability` được gửi qua BLE về Mobile App (PulseMonitor) hoặc in ra Serial.

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

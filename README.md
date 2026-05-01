# MedTech Device — Advanced ECG & AI Diagnostics Ecosystem

<div align="center">
  <img src="./docs/images/logos/platformio.png" height="50" alt="PlatformIO" /> &nbsp;&nbsp;
  <img src="./docs/images/logos/espressif.png" height="50" alt="ESP32-S3" /> &nbsp;&nbsp;
  <img src="./docs/images/logos/tensorflow.png" height="50" alt="TensorFlow Lite" /> &nbsp;&nbsp;
  <img src="./docs/images/logos/ble.png" height="50" alt="Bluetooth LE" /> &nbsp;&nbsp;
  <img src="./docs/images/logos/dotnet_maui.png" height="50" alt=".NET MAUI" />
</div>

**MedTech Device** là một giải pháp y tế IoT toàn diện, kết hợp khả năng thu thập tín hiệu sinh học tần số cao với trí tuệ nhân tạo biên (Edge AI). Hệ thống cho phép theo dõi điện tâm đồ (ECG) và nồng độ oxy trong máu (SpO2), đồng thời phân tích biến thiên nhịp tim (HRV) và chẩn đoán rung tâm nhĩ (AF) ngay trên thiết bị cầm tay.

---

## 📸 Demo & Interface

| **Real-time Dashboard** | **AI Diagnostics** | **Frequency Domain Analysis** |
|:---:|:---:|:---:|
| ![Dashboard](./docs/images/real_dashboard.png) | ![AI Diagnostics](./docs/images/real_ai_diagnostics.png) | ![Spectrum](./docs/images/hrv_spectrum.png) |

---

## 🚀 Tính năng cốt lõi

- **Duyệt sóng tim thời gian thực**: Stream dữ liệu ECG 250Hz và PPG 100Hz mượt mà qua BLE.
- **Edge AI Diagnostics**: Chạy mô hình phân loại nhịp tim TFLite Micro trực tiếp trên ESP32-S3 (không cần Internet).
- **Phân tích HRV chuyên sâu**: Tính toán SDNN, RMSSD và tỷ lệ LF/HF để đánh giá Stress và hệ thần kinh thực vật.
- **Báo cáo y tế**: Tự động xuất file Excel và gửi báo cáo qua Email cho bác sĩ chỉ với 1 chạm.
- **Cảnh báo thông minh**: Tự động phát hiện lỗi tiếp xúc điện cực (Lead-off detection).

---

## 🛠 Tech Stack

- **Hardware**: ESP32-S3 (Dual-core 240MHz, 8MB PSRAM), MAX30102 (PPG), AD8232 (ECG).
- **Firmware**: C++ (Arduino Core), PlatformIO, ArduinoJson, NimBLE.
- **Edge AI**: TensorFlow Lite for Microcontrollers.
- **Mobile App**: .NET MAUI 8.0 (C#), LiveChartsCore, SkiaSharp, MailKit.
- **Protocol**: Bluetooth Low Energy (GATT).

---

## 📦 Linh kiện phần cứng

Hệ thống sử dụng các linh kiện y tế và vi điều khiển hiệu năng cao để đảm bảo tính ổn định:

| **Bộ vi xử lý (ESP32-S3)** | **Cảm biến PPG (MAX30102)** | **Module ECG (AD8232)** |
|:---:|:---:|:---:|
| ![ESP32-S3](./docs/images/sensors/esp32s3_soc.png) | ![MAX30102](./docs/images/sensors/max30102.jpg) | ![AD8232](./docs/images/sensors/ad8232.jpg) |
| *Dual-core 240MHz, tích hợp AI Accelerators* | *Đo SpO2 & Nhịp tim qua quang học* | *Thu thập điện tâm đồ (ECG) chuẩn y tế* |

---

## 📂 Kiến trúc hệ thống (Architecture)

### Cấu trúc thư mục
```text
├── firmware/              # Mã nguồn ESP32-S3 (PlatformIO)
│   ├── src/               # Logic xử lý DSP & AI
│   ├── include/           # Header files & TFLite Models
│   └── platformio.ini     # Cấu hình build & thư viện
├── PulseMonitor/          # Ứng dụng di động (.NET MAUI)
│   ├── ViewModels/        # Logic xử lý dữ liệu & MVVM
│   ├── Hardware/          # Lớp giao tiếp BLE/Serial
│   └── Views/             # Giao diện người dùng (XAML)
├── docs/                  # Tài liệu & Hình ảnh
│   └── images/            # Demo, Logos, Sensors
└── Test/                  # Scripts kiểm thử Python & Database mẫu
```

### Luồng dữ liệu (Data Flow)
1. **Sensor Layer**: MAX30102 (I2C) & AD8232 (Analog) gửi tín hiệu thô.
2. **DSP Layer**: ESP32-S3 thực hiện lọc số IIR (Bandpass + Notch) để loại bỏ nhiễu 50Hz.
3. **Inference Layer**: TFLite Micro phân tích các cửa sổ RR-Intervals để dự đoán AF.
4. **Transport Layer**: Dữ liệu gộp (JSON) được gửi qua BLE GATT Characteristic.
5. **UI Layer**: App .NET MAUI nhận dữ liệu, vẽ biểu đồ SkiaSharp và hiển thị chẩn đoán.

---

## 🚦 Hướng dẫn bắt đầu (Getting Started)

### 1. Chuẩn bị phần cứng
- Kết nối MAX30102: SDA (GPIO 4), SCL (GPIO 5).
- Kết nối AD8232: OUTPUT (GPIO 1), LO+ (GPIO 2), LO- (GPIO 3).

### 2. Cấu hình Firmware (VS Code + PlatformIO)
1. Mở thư mục `firmware` bằng VS Code.
2. Đảm bảo cổng COM chính xác trong `platformio.ini`.
3. Nhấn **Upload** để nạp code.

### 3. Chạy Ứng dụng di động
1. Mở solution `MedTech_Device.sln` bằng Visual Studio 2022.
2. Chọn project `PulseMonitor` và target là **Android Emulator** hoặc thiết bị thật.
3. Bật Bluetooth trên điện thoại và nhấn **Connect** trong App.

---

## ⚙️ Biến môi trường & Cấu hình
| Biến | Mô tả | Giá trị mặc định |
|:---|:---|:---|
| `BLE_NAME` | Tên thiết bị hiển thị | `PulseMonitor` |
| `SMTP_SERVER` | Server gửi Email | `smtp.gmail.com` |
| `SMTP_PORT` | Cổng SMTP | `587` |

---

## 🧪 Kiểm thử (Testing)
- **Serial Test**: Sử dụng `pio device monitor` để xem log AI và Latency (ms).
- **Data Simulation**: Chạy `python Test/stream_test_afdb.py` để giả lập dữ liệu từ bộ cơ sở dữ liệu AFDB lên thiết bị qua Serial.

---

## 🛠 Khắc phục sự cố (Troubleshooting)
- **Lỗi BLE Disconnect**: Kiểm tra nguồn cấp cho ESP32. Sử dụng tụ lọc 10uF cho MAX30102 nếu tín hiệu nhiễu.
- **Sóng ECG bị phẳng**: Đảm bảo điện cực dán chặt và không bị khô gel. Kiểm tra chân kết nối LO+/LO-.
- **Lỗi Build App**: Đảm bảo đã cài đặt đầy đủ các workload **.NET Multi-platform App UI development**.

---
*Dự án được phát triển bởi Thinhqn0905 & Antigravity AI*

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

### Cấu trúc thư mục chi tiết
```text
├── firmware/              # Mã nguồn ESP32-S3 (PlatformIO)
│   ├── src/
│   │   ├── ble_manager.cpp    # Quản lý giao thức GATT & Advertising
│   │   ├── hrv_analyzer.cpp   # Thuật toán phân tích biến thiên nhịp tim
│   │   ├── peak_detector.cpp  # Bộ lọc & phát hiện đỉnh R-Peak
│   │   ├── spo2_calculator.cpp # Thuật toán tính SpO2 (Red/IR Ratio)
│   │   └── main.cpp           # Luồng thực thi chính (100Hz Loop)
│   └── platformio.ini         # Cấu hình thư viện (NimBLE, ArduinoJson)
├── PulseMonitor/          # Ứng dụng di động (.NET MAUI)
│   ├── ViewModels/            # Xử lý logic MVVM & Data Binding
│   ├── Hardware/              # Giao tiếp BLE tầng thấp
│   ├── Views/                 # UI/UX (XAML)
│   └── Resources/             # Assets, Fonts, Logos
├── docs/                  # Tài liệu & Hình ảnh
│   ├── images/                # Screenshots & Demo
│   └── logos/                 # Official Tech Logos
└── Test/                  # Scripts kiểm thử (Python)
```

### Luồng dữ liệu (Data Flow)
1. **Sensor Layer**: Thu thập dữ liệu thô từ cảm biến quang học (PPG) và điện cực (ECG).
2. **DSP Layer**: ESP32-S3 thực hiện lọc nhiễu 50Hz và chuẩn hóa tín hiệu.
3. **Analytics Layer**: Tính toán RR-Intervals và các chỉ số HRV (SDNN, RMSSD).
4. **Transport Layer**: Đóng gói dữ liệu JSON và truyền qua BLE Notify.
5. **UI Layer**: Ứng dụng di động giải mã dữ liệu và vẽ biểu đồ thời gian thực.

---

## 🛠 Thông số kỹ thuật chuyên sâu (Technical Deep Dive)

### 1. Giao thức Bluetooth LE (GATT)
Hệ thống sử dụng các UUID tùy chỉnh để truyền tải dữ liệu hiệu quả:
- **Service UUID**: `DE010001-0000-1000-8000-00805F9B34FB`
- **Waveform Characteristic (Notify)**: `DE010003-...` (Truyền sóng thô IR/Red)
- **Metrics Characteristic (Notify)**: `DE010004-...` (Truyền BPM, SpO2, HRV)

### 2. Thuật toán phân loại nhịp tim
Phân loại dựa trên biến thiên của các khoảng NN (NN-intervals):
- **Normal**: Nhịp tim đều đặn trong dải 50-120 BPM.
- **Tachycardia**: Nhịp nhanh (RR < 500ms).
- **Bradycardia**: Nhịp chậm (RR > 1200ms).
- **Irregular**: Nhịp không đều (Hệ số biến thiên CoV > 0.18) - Dấu hiệu cảnh báo AF.

### 3. Đánh giá mức độ Stress (SDNN-based)
- **Thấp (Low)**: SDNN ≥ 50ms
- **Trung bình (Moderate)**: 30ms ≤ SDNN < 50ms
- **Cao (High)**: 20ms ≤ SDNN < 30ms
- **Rất cao (Very High)**: SDNN < 20ms

---

## 📋 Yêu cầu hệ thống (Prerequisites)
Để làm việc với dự án này, bạn cần cài đặt:
- **Visual Studio Code** (cùng extension PlatformIO IDE).
- **.NET 8.0 SDK** (hoặc mới hơn) cùng với workload `.NET Multi-platform App UI development`.
- **Visual Studio 2022** (Khuyến nghị dùng cho phát triển Windows/Android).
- **Git** (để clone repository).

---

## 🚀 Hướng dẫn bắt đầu (Getting Started)

### 1. Clone Repository
```bash
git clone https://github.com/Thinhqn0905/MedTECH_MedicalHealth.git
cd MedTECH_MedicalHealth
```

### 2. Chuẩn bị phần cứng
- **ESP32-S3**: Vi xử lý trung tâm.
- **MAX30102**: Kết nối I2C (SDA: GPIO 8, SCL: GPIO 9).
- **AD8232**: Kết nối Analog (Output: GPIO 1).

### 3. Cấu hình Firmware (PlatformIO)
1. Mở thư mục `firmware` bằng VS Code.
2. PlatformIO sẽ tự động tải các thư viện cần thiết trong `platformio.ini`.
3. Nhấn nút **Upload** (mũi tên sang phải ở thanh trạng thái) để nạp code.
4. Mở Serial Monitor (baud rate 115200) để xem log hệ thống.

### 4. Cài đặt Ứng dụng di động
1. Mở solution `MedTech_Device.sln` bằng Visual Studio 2022.
2. Chọn project `PulseMonitor`.
3. Chọn thiết bị đích là **Android Emulator** hoặc cắm điện thoại Android/iOS thật vào.
4. Nhấn F5 để Build & Deploy.
5. Chấp nhận quyền vị trí (Location) và Bluetooth trên điện thoại để ứng dụng có thể quét thiết bị BLE.

---

## ⚙️ Biến môi trường & Cấu hình (Environment Variables)
Bạn có thể cấu hình các thông số sau trong firmware và ứng dụng:

| Biến | Vị trí | Mô tả | Giá trị mặc định |
|:---|:---|:---|:---|
| `BLE_NAME` | `firmware/src/ble_manager.cpp` | Tên thiết bị phát BLE | `PulseMonitor` |
| `SMTP_SERVER` | `PulseMonitor` config | Server gửi Email báo cáo | `smtp.gmail.com` |
| `SMTP_PORT` | `PulseMonitor` config | Cổng SMTP | `587` |

---

## 💻 Các lệnh có sẵn (Available Scripts)
Bảng lệnh dùng trong terminal cho các nhà phát triển:

| Lệnh | Môi trường | Mô tả |
|:---|:---|:---|
| `pio run` | `firmware/` | Build firmware ESP32-S3. |
| `pio run -t upload` | `firmware/` | Nạp firmware xuống board. |
| `pio device monitor` | `firmware/` | Mở Serial Monitor theo dõi log. |
| `dotnet build -t:Run -f net8.0-android` | `PulseMonitor/` | Build và chạy App trên Android Emulator/Device. |
| `dotnet clean` | `PulseMonitor/` | Xóa các file build cũ của MAUI. |

---

## 🧪 Kiểm thử (Testing)
- **Unit Test**: Kiểm tra các thuật toán DSP trong thư mục `Test/`.
- **Integration Test**: Sử dụng ứng dụng `nRF Connect` (trên điện thoại) để quét và kiểm tra các Characteristic của BLE xem chúng có push dữ liệu (Notify) đúng hay không.
- **Simulation**: Chạy script `python Test/stream_test_afdb.py` để giả lập dữ liệu từ bộ cơ sở dữ liệu MIT-BIH AFDB đẩy lên thiết bị qua cổng Serial khi không có cảm biến thật.

---

## 📦 Triển khai (Deployment)

### 1. Triển khai Firmware (Production)
Khi phần cứng đã hoàn thiện, bạn nạp firmware với cờ release để tối ưu hiệu năng:
```bash
cd firmware
pio run -e esp32s3 -t upload
```
*Lưu ý: Firmware đã nạp có thể hoạt động độc lập ngay khi được cấp nguồn bằng Pin.*

### 2. Triển khai Ứng dụng (Android APK)
Để xuất file APK phát hành cho người dùng Android:
```bash
cd PulseMonitor
dotnet publish -f net8.0-android -c Release
```
File APK sẽ được tạo tại `bin/Release/net8.0-android/publish/`.

---

## 🛠 Khắc phục sự cố (Troubleshooting)
- **Không tìm thấy thiết bị BLE**: Đảm bảo ESP32 đã được cấp nguồn và đang nháy LED xanh. Rút cáp và cắm lại nếu cần.
- **Tín hiệu nhiễu mạnh (Noise)**: Kiểm tra dây tiếp địa (GND) và tránh xa các nguồn nhiễu điện từ (Adapter sạc, Motor). Thêm tụ 10uF cho cảm biến MAX30102.
- **Lỗi Email Export**: Kiểm tra lại mật khẩu ứng dụng (App Password) trong cấu hình Gmail của ứng dụng.
- **Lỗi `XA0137` khi Deploy Android**: Đây là lỗi Fast Deployment. Thêm `-p:EmbedAssembliesIntoApk=true` vào lệnh `dotnet build`.

---

## 📜 Giấy phép & Đóng góp (License & Contributing)
- **License**: MIT License.
- **Contributing**: Mọi đóng góp về việc cải thiện thuật toán AI (HRV, AF detection) và UI đều được chào đón qua Pull Request.

---
*Product from BSMART_LAB copyright 2026*

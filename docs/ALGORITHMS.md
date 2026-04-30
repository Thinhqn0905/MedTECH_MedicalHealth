# PulseMonitor Algorithm Technical Specification

Tài liệu này mô tả chi tiết các thuật toán xử lý tín hiệu sinh học (ECG/PPG) và chẩn đoán AI được triển khai trong hệ thống PulseMonitor.

---

## 1. Digital Signal Processing (DSP) Pipeline

Trước khi đưa vào phân tích AI hoặc hiển thị, tín hiệu thô từ cảm biến (AD8232/MAX30102) trải qua chuỗi lọc số để loại bỏ nhiễu.

### 1.1 IIR Bandpass Filter (0.5Hz - 30Hz)
- **Mục tiêu**: Loại bỏ nhiễu trôi nền (Baseline Wander) do nhịp thở (<0.5Hz) và nhiễu cơ cao tần (>30Hz).
- **Triển khai**: Bộ lọc Butterworth bậc 4.
- **Công thức**: $y[n] = \frac{1}{a_0} \sum_{i=0}^{k} b_i x[n-i] - \sum_{j=1}^{k} a_j y[n-j]$

### 1.2 Notch Filter (50Hz)
- **Mục tiêu**: Loại bỏ nhiễu điện lưới (Power Line Interference) 50Hz.
- **Thông số**: Q-factor = 30.
- **Đặc điểm**: Chỉ triệt tiêu dải tần hẹp xung quanh 50Hz để tránh làm biến dạng sóng QRS.

---

## 2. Heartbeat Detection (Pan-Tompkins Algorithm)

Đây là thuật toán kinh điển được triển khai trong `PanTompkinsDetector.cs` và Firmware để xác định vị trí đỉnh R (R-peak) trong thời gian thực.

### Các giai đoạn xử lý:
1. **Differentiation (Đạo hàm)**: Làm nổi bật sự thay đổi dốc của phức bộ QRS so với sóng P và T.
   - Công thức: $y(n) = \frac{1}{8} [2x(n) + x(n-1) - x(n-3) - 2x(n-4)]$
2. **Squaring (Bình phương)**: Làm cho tất cả các điểm dữ liệu thành số dương và khuếch đại các đỉnh có biên độ lớn.
   - Công thức: $y(n) = [x(n)]^2$
3. **Moving Window Integration (Tích phân cửa sổ trượt)**: Tạo ra một xung bao quanh phức bộ QRS.
   - Độ dài cửa sổ: 12-15 mẫu (tương đương ~150ms).
4. **Adaptive Thresholding (Ngưỡng thích nghi)**:
   - Ngưỡng được tính dựa trên giá trị trung bình và độ lệch chuẩn của cửa sổ tích phân: `Threshold = Mean + (1.4 * StdDev)`.
   - Giúp thuật toán tự điều chỉnh khi biên độ sóng ECG thay đổi do tư thế nằm/ngồi.

---

## 3. HRV (Heart Rate Variability) Analysis

Sau khi xác định được các khoảng cách R-R (RRIs), hệ thống tính toán các chỉ số HRV để đánh giá hệ thần kinh tự chủ (ANS).

### 3.1 Time-Domain Metrics
- **SDNN**: Độ lệch chuẩn của các khoảng R-R. Phản ánh tổng thể khả năng thích nghi của tim.
- **RMSSD**: Căn bậc hai trung bình bình phương của các hiệu số R-R kế tiếp. Phản ánh hoạt động của hệ đối giao cảm (Vagal tone).

### 3.2 Frequency-Domain Metrics (FFT)
- Sử dụng thuật toán **Fast Fourier Transform (FFT)** để chuyển đổi chuỗi RRIs sang miền tần số.
- **LF (Low Frequency)**: 0.04 - 0.15 Hz (Liên quan đến hệ giao cảm).
- **HF (High Frequency)**: 0.15 - 0.4 Hz (Liên quan đến hệ đối giao cảm).
- **LF/HF Ratio**: Chỉ số đánh giá sự cân bằng Sympathovagal (Căng thẳng/Thả lỏng).

---

## 4. Edge AI Inference (AF Detection)

Mô hình học sâu được triển khai bằng **TensorFlow Lite Micro** để phát hiện Rung tâm nhĩ (AF).

### Quy trình xử lý:
1. **Windowing**: Cắt một cửa sổ dữ liệu dài 10 giây (2500 mẫu tại 250Hz).
2. **Normalization**: Chuẩn hóa dữ liệu về dạng Zero-mean và Unit-variance để loại bỏ sự khác biệt về biên độ ADC giữa các thiết bị.
3. **Inference**: Mô hình CNN/LSTM thực hiện trích xuất đặc trưng không gian và thời gian của sóng tim.
4. **Probability Output**: Trả về xác suất (0.0 - 1.0). Nếu xác suất > 0.5, hệ thống kết luận có dấu hiệu AF.

---

## 5. SpO2 Calculation (Dành cho PPG)

Dựa trên nguyên lý hấp thụ ánh sáng của Hemoglobin.
- **R-Ratio**: $R = \frac{AC_{Red} / DC_{Red}}{AC_{IR} / DC_{IR}}$
- **Công thức thực nghiệm**: $\%SpO2 = 110 - 25 \times R$

---

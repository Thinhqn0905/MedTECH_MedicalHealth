import asyncio
import struct
import sys
from bleak import BleakScanner, BleakClient

ECG_DEVICE_NAME = "PulseMonitor-ECG"
ECG_WAVEFORM_UUID = "A0000002-0000-1000-8000-00805F9B34FB"
ECG_LEADOFF_UUID = "A0000003-0000-1000-8000-00805F9B34FB"

last_seq = -1

def waveform_handler(sender, data):
    global last_seq
    if len(data) != 22:
        return
    
    seq = struct.unpack('<H', data[0:2])[0]
    samples = struct.unpack('<10h', data[2:22])
    
    if last_seq != -1 and seq != (last_seq + 1) % 65536:
        print(f"\n⚠️ MẤT GÓI TIN! Mong đợi: {(last_seq + 1) % 65536}, Thực tế: {seq}\n")
        
    last_seq = seq
    
    # Tính trung bình 10 mẫu để vẽ 1 thanh bar cho dễ nhìn
    avg_val = sum(samples) / len(samples)
    
    # Scale giá trị int16 [-2048, 2048] thành độ dài thanh bar [0, 80]
    # (Giá trị thực tế phụ thuộc vào nhiễu và gain của AD8232)
    bar_len = int((avg_val + 2048) / 50) 
    bar_len = max(0, min(bar_len, 80))
    bar = "█" * bar_len
    
    print(f"[{seq:05d}] {bar} ({avg_val:.1f})")

def leadoff_handler(sender, data):
    status = data[0]
    states = {
        0: "✅ BÌNH THƯỜNG", 
        1: "🚨 TUỘT ĐIỆN CỰC LO+ (Right Arm / Left Arm)", 
        2: "🚨 TUỘT ĐIỆN CỰC LO- (Left Leg)", 
        3: "🚨 TUỘT TẤT CẢ ĐIỆN CỰC"
    }
    print(f"\n=======================================================")
    print(f" TRẠNG THÁI LEAD-OFF: {states.get(status, 'KHÔNG RÕ')}")
    print(f"=======================================================\n")

async def main():
    print(f"🔍 Đang tìm kiếm thiết bị BLE có tên: '{ECG_DEVICE_NAME}'...")
    device = await BleakScanner.find_device_by_name(ECG_DEVICE_NAME, timeout=10.0)
    
    if not device:
        print("❌ Không tìm thấy thiết bị! Hãy đảm bảo Board B đã được cấp nguồn và nạp đúng code.")
        return
        
    print(f"✅ Đã tìm thấy: {device.name} [{device.address}]. Đang kết nối...")
    
    try:
        async with BleakClient(device) as client:
            print("✅ Kết nối thành công!")
            
            # Đăng ký nhận notify từ 2 characteristics
            await client.start_notify(ECG_WAVEFORM_UUID, waveform_handler)
            await client.start_notify(ECG_LEADOFF_UUID, leadoff_handler)
            
            print("📡 Đang lắng nghe dữ liệu ECG... (Bấm Ctrl+C để dừng)\n")
            
            # Chờ vô tận
            while True:
                await asyncio.sleep(1.0)
                
    except Exception as e:
        print(f"\n❌ Lỗi kết nối: {e}")

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\n⏹️ Đã dừng chương trình.")

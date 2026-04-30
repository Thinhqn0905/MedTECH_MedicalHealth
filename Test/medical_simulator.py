import asyncio
import websockets
import json
import time
import math
import random

async def stream_medical_data(websocket):
    print(f"Client connected from {websocket.remote_address}")
    
    # Base parameters
    bpm = 75.0
    t = 0.0
    dt = 0.01  # 10ms for 100Hz
    frame_count = 0
    next_tick = time.perf_counter()
    
    try:
        while True:
            t += dt
            
            # Thêm biến thiên ngẫu nhiên cho nhịp tim (HRV cơ bản)
            if random.random() < 0.05:
                bpm += random.uniform(-0.5, 0.5)
                # Giới hạn nhịp tim ở vùng an toàn
                bpm = max(60, min(100, bpm))
                
            heart_rate_hz = bpm / 60.0
            
            # Tính toán sóng Quang học (PPG) dạng chu kỳ kết hợp nhiễu
            # Sóng có hình thái gồm đỉnh nhọn (Systolic peak) và rãnh (Dicrotic notch)
            phase = 2 * math.pi * heart_rate_hz * t
            
            red_base = 100000
            red_wave = 5000 * math.sin(phase) + 1500 * math.sin(2 * phase + 0.5)
            red_noise = random.normalvariate(0, 100)
            red = int(red_base + red_wave + red_noise)
            
            ir_base = 90000
            ir_wave = 4000 * math.sin(phase - 0.1) + 1200 * math.sin(2 * phase + 0.4)
            ir_noise = random.normalvariate(0, 80)
            ir = int(ir_base + ir_wave + ir_noise)
            
            # Giả lập biến thiên nhịp tim (HRV metrics)
            # Thường HRV biến đổi theo nhịp thở (RSA) hoặc stress
            sdnn = 45.0 + 10.0 * math.sin(2 * math.pi * 0.1 * t) + random.uniform(-2, 2)
            rmssd = 38.0 + 8.0 * math.sin(2 * math.pi * 0.1 * t) + random.uniform(-1, 1)
            
            # Logic đơn giản cho Stress & Rhythm
            stress_level = "Normal"
            if sdnn < 30: stress_level = "High"
            elif sdnn > 50: stress_level = "Low"
            
            rhythm_type = "Irregular" if random.random() < 0.01 else "Normal"
            
            frame_count += 1
            
            # 1. Gửi dữ liệu Sóng (Waveform IRSample) ở 100Hz
            sample_payload = {
                "ts": int(time.time() * 1000),
                "red": red,
                "ir": ir
            }
            await websocket.send(json.dumps(sample_payload))
            
            # 2. Gửi dữ liệu phân tích AI (HRV & Sinh tồn) ở 1Hz (Mỗi 100 vòng lặp)
            if frame_count >= 100:
                frame_count = 0
                ai_payload = {
                    "ts": int(time.time() * 1000),
                    "bpm": round(bpm, 1),
                    "spo2": random.choice([98, 98, 99, 99, 100]),
                    "hrv": {
                        "sdnn": float(round(sdnn, 2)),
                        "rmssd": float(round(rmssd, 2)),
                        "stress": 1 if stress_level == "Low" else (2 if stress_level == "Normal" else 3),
                        "rhythm": rhythm_type
                    }
                }
                await websocket.send(json.dumps(ai_payload))
            
            # Stable 100Hz pacing using a monotonic scheduler.
            next_tick += dt
            sleep_for = next_tick - time.perf_counter()
            if sleep_for > 0:
                await asyncio.sleep(sleep_for)
            else:
                # If loop falls behind, avoid spiral-of-death by resyncing now.
                next_tick = time.perf_counter()
            
    except websockets.exceptions.ConnectionClosed:
        print("Client disconnected.")

async def main():
    # Mở Server tại 127.0.0.1:8080
    host = "0.0.0.0"
    port = 8080
    server = await websockets.serve(stream_medical_data, host, port)
    
    print(f"=========================================")
    print(f"🚀 Medical Hardware Simulator is RUNNING")
    print(f"📡 WebSocket Server: ws://{host}:{port}")
    print(f"⏱️  Sampling Rate: 100Hz (10ms)")
    print(f"=========================================")
    
    await asyncio.Future()  # Chạy mãi mãi

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\nSimulator stopped.")

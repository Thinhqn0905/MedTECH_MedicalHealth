import serial
import time
import struct

port = "COM6"
baud = 115200

def wait_for_line(ser, target, timeout=5):
    start = time.time()
    while time.time() - start < timeout:
        if ser.in_waiting > 0:
            line = ser.readline().decode(errors='ignore').strip()
            if line:
                print(f"[DEVICE] {line}")
                if target in line:
                    return True
        time.sleep(0.01)
    return False

try:
    print(f"Opening {port}...")
    ser = serial.Serial(port, baud, timeout=1)
    ser.setDTR(True)
    ser.setRTS(False)
    time.sleep(2)
    ser.reset_input_buffer()
    
    print("Sending STREAM_TEST...")
    ser.write(b"STREAM_TEST\n")
    if wait_for_line(ser, "STREAM_TEST_ACK"):
        print("ACK received. Sending WINDOW...")
        ser.write(b"WINDOW\n")
        time.sleep(0.5) # Wait for ESP to enter loop
        
        dummy_data = struct.pack('f' * 2500, *([0.1] * 2500))
        chunk_size = 256 # Even smaller chunks
        for i in range(0, len(dummy_data), chunk_size):
            chunk = dummy_data[i:i+chunk_size]
            ser.write(chunk)
            print(f"  Sent {len(chunk)} bytes ({i+len(chunk)}/10000)", end="\r")
            time.sleep(0.03) # 30ms delay
        print("\nAll bytes sent. Waiting for RESULT...")
        
        ser.timeout = 20
        while True:
            line = ser.readline().decode(errors='ignore').strip()
            if line:
                print(f"[DEVICE] {line}")
                if "RESULT:" in line:
                    break
            time.sleep(0.1)
    else:
        print("Timed out waiting for STREAM_TEST_ACK")
    
    ser.close()
    print("Done.")
except Exception as e:
    print(f"Error: {e}")

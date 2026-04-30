import asyncio
import struct
from bleak import BleakScanner, BleakClient

# NEW UUIDs (Mirroring Board B style)
SERVICE_UUID = "DE010001-0000-1000-8000-00805F9B34FB"
CHAR_UUID    = "DE010003-0000-1000-8000-00805F9B34FB"

def callback(sender, data):
    if len(data) == 12:
        ts, ir, red = struct.unpack("<III", data)
        print(f"[DATA] TS: {ts:10} | IR: {ir:8} | RED: {red:8}")
    else:
        print(f"[DATA] Raw: {data.hex()}")

async def run():
    print(f"Scanning for PPG Board (PulseMonitor-PPG)...")
    device = await BleakScanner.find_device_by_filter(
        lambda d, ad: d.name and "PulseMonitor" in d.name
    )
    
    if not device:
        print("PPG Board not found.")
        return

    print(f"Connecting to {device.name} ({device.address})...")
    async with BleakClient(device) as client:
        print(f"Connected! MTU: {client.mtu_size}")
        
        print(f"Starting Notifications on {CHAR_UUID}...")
        await client.start_notify(CHAR_UUID, callback)
        
        # Stream for 15 seconds
        await asyncio.sleep(15)
        await client.stop_notify(CHAR_UUID)
        print("Done.")

if __name__ == "__main__":
    asyncio.run(run())

import asyncio
from bleak import BleakClient

ADDRESS = "DC:B4:D9:09:71:B1"
CHARACTERISTIC_UUID = "6E400003-B5A3-F393-E0A9-E50E24DCCA9E"

def notification_handler(sender, data):
    # Data is 12 bytes: [ts:4][ir:4][red:4]
    import struct
    if len(data) >= 12:
        ts, ir, red = struct.unpack("<III", data[:12])
        print(f"TS: {ts} | IR: {ir} | RED: {red}")
    else:
        print(f"Raw Data: {data.hex()}")

async def run():
    print(f"Connecting to {ADDRESS}...")
    async with BleakClient(ADDRESS) as client:
        print(f"Connected: {client.is_connected}")
        print("Starting notifications...")
        await client.start_notify(CHARACTERISTIC_UUID, notification_handler)
        await asyncio.sleep(10.0)
        await client.stop_notify(CHARACTERISTIC_UUID)
        print("Done.")

asyncio.run(run())

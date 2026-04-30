import asyncio
from bleak import BleakScanner

async def run():
    print("Searching for PPG_DEBUG board...")
    def callback(device, adv):
        if device.name and "PPG" in device.name:
            print(f">>> FOUND: {device.name} [{device.address}] RSSI: {adv.rssi}")
            print(f"    Services: {adv.service_uuids}")

    async with BleakScanner(callback) as scanner:
        await asyncio.sleep(15.0)

asyncio.run(run())

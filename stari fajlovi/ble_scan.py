import asyncio
from bleak import BleakScanner

async def main():
    print("Skeniranje BLE uredjaja (10 sekundi)...\n")
    devices = await BleakScanner.discover(timeout=10.0)
    if not devices:
        print("Nije pronadjen nijedan uredjaj.")
        return
    for d in devices:
        print(f"  {d.address}  Ime: {d.name or '(nepoznato)'}")

asyncio.run(main())

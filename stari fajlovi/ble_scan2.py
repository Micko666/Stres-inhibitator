import asyncio
from bleak import BleakScanner

# Trazimo Xiaomi service UUID 0xFE95 u advertisement podacima
XIAOMI_UUID = "0000fe95-0000-1000-8000-00805f9b34fb"

found_band = None

def detection_callback(device, advertisement_data):
    global found_band
    uuids = [u.lower() for u in (advertisement_data.service_uuids or [])]
    name = device.name or ""
    is_xiaomi = XIAOMI_UUID in uuids or "xiaomi" in name.lower() or "band" in name.lower() or "mi smart" in name.lower()

    if is_xiaomi:
        print(f"[XIAOMI] {device.address}  Ime: {name}  UUIDs: {uuids}")
        found_band = device.address
    else:
        # Ispisi sve da vidimo sta ima
        print(f"  {device.address}  {name or '(nepoznato)'}")

async def main():
    print("Skeniranje (15 sekundi) — trazim Xiaomi Band...\n")
    scanner = BleakScanner(detection_callback=detection_callback)
    await scanner.start()
    await asyncio.sleep(15)
    await scanner.stop()

    if found_band:
        print(f"\n=> Band pronadjen na adresi: {found_band}")
    else:
        print("\n=> Band nije pronadjen. Provjeri da li je telefon Bluetooth iskljucen.")

asyncio.run(main())

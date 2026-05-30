import asyncio
from bleak import BleakScanner

def detection_callback(device, advertisement_data):
    name = device.name or ""
    uuids = advertisement_data.service_uuids or []
    mfr = advertisement_data.manufacturer_data or {}
    svc_data = advertisement_data.service_data or {}

    # Trazimo Xiaomi (0xFE95 = 65173, ili "FE95" u UUIDs)
    is_xiaomi = (
        any("fe95" in u.lower() for u in uuids) or
        65173 in mfr or  # 0xFE95
        any("fe95" in k.lower() for k in svc_data.keys()) or
        "xiaomi" in name.lower() or "band" in name.lower() or "mi smart" in name.lower()
    )

    # Pokazi sve uredaje s manufacture data (ukljucujuci nepoznate)
    if mfr or is_xiaomi:
        print(f"\n{'[XIAOMI!]' if is_xiaomi else '[MFR]'} {device.address}  Ime: {name or '(nepoznato)'}")
        if uuids:
            print(f"  UUIDs: {uuids}")
        for company_id, data in mfr.items():
            print(f"  Manufacturer 0x{company_id:04X}: {data.hex()}")
        for uuid, data in svc_data.items():
            print(f"  ServiceData {uuid}: {data.hex()}")

async def main():
    print("Skeniranje (15 sek) — trazim Xiaomi Band u advertisement podacima...\n")
    scanner = BleakScanner(detection_callback=detection_callback)
    await scanner.start()
    await asyncio.sleep(15)
    await scanner.stop()
    print("\nSkeniranje zavrseno.")

asyncio.run(main())

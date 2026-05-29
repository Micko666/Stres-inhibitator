import asyncio
from bleak import BleakClient, BleakScanner

BAND_MAC = "08:16:D5:A5:D8:C8"

# Standardni Heart Rate Service
HR_SERVICE_UUID     = "0000180d-0000-1000-8000-00805f9b34fb"
HR_MEASUREMENT_UUID = "00002a37-0000-1000-8000-00805f9b34fb"

def hr_notification_handler(sender, data):
    flags = data[0]
    if flags & 0x01:
        hr = int.from_bytes(data[1:3], 'little')
    else:
        hr = data[1]
    print(f"  ❤  Puls: {hr} bpm  (raw: {data.hex()})")

async def main():
    print(f"Spajam se na Band 9 ({BAND_MAC})...\n")

    async with BleakClient(BAND_MAC, timeout=15.0) as client:
        print(f"Spojeno: {client.is_connected}\n")

        print("=== Dostupni GATT servisi ===")
        for service in client.services:
            print(f"\nServis: {service.uuid}  ({service.description})")
            for char in service.characteristics:
                props = ", ".join(char.properties)
                print(f"  Char: {char.uuid}  [{props}]  ({char.description})")

        # Provjeri da li postoji standardni HR servis
        hr_service = client.services.get_service(HR_SERVICE_UUID)
        if hr_service:
            print("\n\n✅ Standardni Heart Rate Service pronadjen!")
            print("Pretplaćujem se na notifikacije pulsa (30 sekundi)...\n")
            await client.start_notify(HR_MEASUREMENT_UUID, hr_notification_handler)
            await asyncio.sleep(30)
            await client.stop_notify(HR_MEASUREMENT_UUID)
        else:
            print("\n\n⚠ Standardni Heart Rate Service (0x180D) nije dostupan bez auth.")
            print("Popis UUID-ova svih servisa gore.")

asyncio.run(main())

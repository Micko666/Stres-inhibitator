import asyncio
from bleak import BleakClient

MAC = "04:DA:28:39:4C:40"
HR_SERVICE = "0000180d-0000-1000-8000-00805f9b34fb"
HR_CHAR    = "00002a37-0000-1000-8000-00805f9b34fb"

def hr_handler(sender, data):
    hr = data[1] if not (data[0] & 0x01) else int.from_bytes(data[1:3], 'little')
    print(f"  PULS: {hr} bpm  (raw: {data.hex()})")

async def main():
    print(f"Spajam se na Redmi Watch 3 Active ({MAC})...\n")
    async with BleakClient(MAC, timeout=15.0) as c:
        print(f"Spojeno!\n")
        print("=== GATT Servisi ===")
        for svc in c.services:
            print(f"\n{svc.uuid}  {svc.description}")
            for ch in svc.characteristics:
                print(f"  {ch.uuid}  [{', '.join(ch.properties)}]  {ch.description}")

        hr_svc = c.services.get_service(HR_SERVICE)
        if hr_svc:
            print("\n\nHeart Rate Service pronadjen! Slusam 30 sekundi...\n")
            await c.start_notify(HR_CHAR, hr_handler)
            await asyncio.sleep(30)
            await c.stop_notify(HR_CHAR)
        else:
            print("\n\nHR Service (0x180D) nije direktno dostupan bez auth.")
            print("Listu servisa vidis gore.")

asyncio.run(main())

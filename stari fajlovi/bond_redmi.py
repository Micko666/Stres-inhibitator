"""
Pokušaj OS-level BLE bondinga s Redmi Watch 3 Active.
Mora biti u pairing modu (QR kod na ekranu).
"""

import asyncio
from bleak import BleakClient, BleakScanner

MAC = "04:DA:28:39:4C:40"

CHAR_AUTH  = "00000052-0000-1000-8000-00805f9b34fb"
CHAR_WRITE = "00000051-0000-1000-8000-00805f9b34fb"
CHAR_BB02  = "0000bb02-0000-1000-8000-00805f9b34fb"
CHAR_BB03  = "0000bb03-0000-1000-8000-00805f9b34fb"

received = []

def notif(label):
    def _h(s, d):
        print(f"  <<< [{label}] {d.hex()}")
        received.append((label, d))
    return _h

async def main():
    print(f"Spajam se na {MAC}...\n")

    async with BleakClient(MAC, timeout=20.0) as c:
        print("Spojeno!")

        # Pokušaj OS-level pairing
        print("Pokusavam OS pairing (Windows ce mozda pokazati dialog)...")
        try:
            result = await c.pair(protection_level=1)
            print(f"  pair() rezultat: {result}")
        except Exception as e:
            print(f"  pair() greška: {e}")

        await asyncio.sleep(2)

        # Subscribe
        for label, uuid in [("0051", CHAR_WRITE), ("0052", CHAR_AUTH), ("bb03", CHAR_BB03)]:
            try:
                await c.start_notify(uuid, notif(label))
                print(f"Subscribe: {label} OK")
            except Exception as e:
                print(f"Subscribe {label} greška: {e}")

        # Sada pokušaj Xiaomi auth
        import os
        rnd = os.urandom(16)
        auth_init = bytes([0x82, 0x00, 0x10]) + rnd
        print(f"\nSaljem auth na 0052: {auth_init.hex()}")
        try:
            await c.write_gatt_char(CHAR_AUTH, auth_init, response=False)
        except Exception as e:
            print(f"  greška: {e}")

        print("\nSlusam 10s...\n")
        await asyncio.sleep(10)

        print(f"Primljeno notifikacija: {len(received)}")

asyncio.run(main())

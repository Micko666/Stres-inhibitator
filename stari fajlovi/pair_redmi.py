"""
Pokusaj direktnog pairinga sa Redmi Watch 3 Active.
Sat mora biti u factory-reset / pairing modu (prikazuje QR kod).

Xiaomi BLE v3 flow:
  1. Read char 0x0050 — device info / capabilities
  2. Write PAIR_CMD na 0x0052 → sat odgovori s random token-om
  3. Potvrdi token → dobijemo session key
  4. Dalje komande (HR request) idu enkriptirane

Ovdje probamo korak 1-3 i pratimo sve notifikacije.
"""

import asyncio
import struct
from bleak import BleakClient

MAC = "04:DA:28:39:4C:40"

SVC_XIAOMI  = "0000fe95-0000-1000-8000-00805f9b34fb"
CHAR_INFO   = "00000050-0000-1000-8000-00805f9b34fb"   # read
CHAR_WRITE  = "00000051-0000-1000-8000-00805f9b34fb"   # write-without-response, notify
CHAR_AUTH   = "00000052-0000-1000-8000-00805f9b34fb"   # write-without-response, notify
CHAR_53     = "00000053-0000-1000-8000-00805f9b34fb"
CHAR_54     = "00000054-0000-1000-8000-00805f9b34fb"

# Xiaomi protocol — init pair request
# Opcode 0x0001 = PAIR_CMD, type 0x01 = initiate
PAIR_REQUEST = bytes([0x00, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00])

def handler(uuid_short):
    def _h(sender, data):
        print(f"  [{uuid_short}] notif: {data.hex()}  ({list(data)})")
    return _h

async def main():
    print(f"Spajam se na {MAC}...\n")
    async with BleakClient(MAC, timeout=15.0) as c:
        print("Spojeno!\n")

        # 1. Procitaj device info
        try:
            info = await c.read_gatt_char(CHAR_INFO)
            print(f"CHAR_INFO (0x0050): {info.hex()}")
            print(f"  ASCII: {info.decode('utf-8', errors='replace')}\n")
        except Exception as e:
            print(f"CHAR_INFO read error: {e}\n")

        # 2. Subscribeaj na sve notify karakteristike
        for uuid, label in [
            (CHAR_WRITE, "0051"),
            (CHAR_AUTH,  "0052"),
            (CHAR_53,    "0053"),
            (CHAR_54,    "0054"),
        ]:
            try:
                await c.start_notify(uuid, handler(label))
                print(f"Subscribeano na {label}")
            except Exception as e:
                print(f"  {label} subscribe greska: {e}")

        print("\nSlusam 5s pasivno (mozda sat sam nesto salje)...\n")
        await asyncio.sleep(5)

        # 3. Pokusaj poslati PAIR request na auth char
        print(f"\nSaljem PAIR_REQUEST na 0x0052: {PAIR_REQUEST.hex()}")
        try:
            await c.write_gatt_char(CHAR_AUTH, PAIR_REQUEST, response=False)
        except Exception as e:
            print(f"  write greska: {e}")

        print("\nCekam odgovor 10s...\n")
        await asyncio.sleep(10)

        # 4. Pokusaj i na 0x0051
        print(f"\nSaljem PAIR_REQUEST i na 0x0051...")
        try:
            await c.write_gatt_char(CHAR_WRITE, PAIR_REQUEST, response=False)
        except Exception as e:
            print(f"  write greska: {e}")

        await asyncio.sleep(5)
        print("\nGotovo.")

asyncio.run(main())

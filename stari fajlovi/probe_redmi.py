"""
Dublje ispitivanje Redmi Watch 3 Active.
Cita device info, ispituje sve notify kanale i pokusava Xiaomi auth v3.
"""

import asyncio
import os
import struct
from bleak import BleakClient

MAC = "04:DA:28:39:4C:40"

# Karakteristike za ispitivanje
CHARS = {
    "0050": "00000050-0000-1000-8000-00805f9b34fb",  # read
    "0051": "00000051-0000-1000-8000-00805f9b34fb",  # write+notify
    "0052": "00000052-0000-1000-8000-00805f9b34fb",  # write+notify (auth)
    "0053": "00000053-0000-1000-8000-00805f9b34fb",
    "0054": "00000054-0000-1000-8000-00805f9b34fb",
    "0055": "00000055-0000-1000-8000-00805f9b34fb",
    "bb02": "0000bb02-0000-1000-8000-00805f9b34fb",  # write
    "bb03": "0000bb03-0000-1000-8000-00805f9b34fb",  # notify
    "dev_name": "00002a00-0000-1000-8000-00805f9b34fb",
    "model":    "00002a24-0000-1000-8000-00805f9b34fb",
    "mfr":      "00002a29-0000-1000-8000-00805f9b34fb",
}

received = []

def make_handler(label):
    def _h(sender, data):
        print(f"  >>> [{label}] {data.hex()}  len={len(data)}")
        received.append((label, data))
    return _h

async def main():
    print(f"Spajam se na {MAC}...\n")
    async with BleakClient(MAC, timeout=15.0) as c:
        print("Spojeno!\n")

        # Citaj readable karakteristike
        for name in ["dev_name", "model", "mfr", "0050"]:
            try:
                val = await c.read_gatt_char(CHARS[name])
                print(f"  {name}: {val.hex()}  =  {val.decode('utf-8', errors='replace')}")
            except Exception as e:
                print(f"  {name}: greska — {e}")
        print()

        # Subscribe na sve notify
        for label in ["0051", "0052", "0053", "0054", "0055", "bb03"]:
            try:
                await c.start_notify(CHARS[label], make_handler(label))
                print(f"Subscribe OK: {label}")
            except Exception as e:
                print(f"Subscribe greska {label}: {e}")
        print()

        # Pasivno slušaj 3s
        await asyncio.sleep(3)

        # --- Pokušaji autentifikacije ---

        # 1. Xiaomi v3 AUTH — init sa 16 random bytes (HMAC flow)
        rnd = os.urandom(16)
        # Frame: cmd=0x82, idx=0x00, len=0x10, data=random
        auth_init = bytes([0x82, 0x00, 0x10]) + rnd
        print(f"\n[1] Šaljem Xiaomi auth init na 0052: {auth_init.hex()}")
        try:
            await c.write_gatt_char(CHARS["0052"], auth_init, response=False)
        except Exception as e:
            print(f"    greška: {e}")
        await asyncio.sleep(3)

        # 2. Pokušaj s bytes [0x01, 0x00] — generic hello
        hello = bytes([0x01, 0x00])
        print(f"\n[2] Šaljem hello na 0051: {hello.hex()}")
        try:
            await c.write_gatt_char(CHARS["0051"], hello, response=False)
        except Exception as e:
            print(f"    greška: {e}")
        await asyncio.sleep(2)

        # 3. BB02 kanal — pokušaj
        bb_cmd = bytes([0x00, 0x01])
        print(f"\n[3] Šaljem test na BB02: {bb_cmd.hex()}")
        try:
            await c.write_gatt_char(CHARS["bb02"], bb_cmd, response=False)
        except Exception as e:
            print(f"    greška: {e}")
        await asyncio.sleep(2)

        # 4. Xiaomi "new device" pairing — opcode 0x91
        pair_cmd = bytes([0x91, 0x0a, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00])
        print(f"\n[4] Šaljem PAIR_CMD (0x91) na 0052: {pair_cmd.hex()}")
        try:
            await c.write_gatt_char(CHARS["0052"], pair_cmd, response=False)
        except Exception as e:
            print(f"    greška: {e}")
        await asyncio.sleep(3)

        print(f"\n=== Ukupno primljenih notifikacija: {len(received)} ===")
        for label, data in received:
            print(f"  [{label}]: {data.hex()}")

asyncio.run(main())

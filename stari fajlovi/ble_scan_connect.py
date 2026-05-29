"""
Skenira BLE dok ne nadje sat, pa se odmah konektuje i radi auth.
Pokretanje: python ble_scan_connect.py
Rebootaj sat i odmah pokreni ovaj script.
"""

import asyncio
from bleak import BleakScanner, BleakClient
import hmac, hashlib, json, socket
from datetime import datetime

TARGET_MAC    = "04:DA:28:39:4C:40"
TARGET_PREFIX = "Redmi Watch"
AUTH_KEY_HEX  = "a77277a213dde02e338a08b1ae3fd712"

CHAR_AUTH  = "00000052-0000-1000-8000-00805f9b34fb"
CHAR_WRITE = "00000051-0000-1000-8000-00805f9b34fb"
CHAR_53    = "00000053-0000-1000-8000-00805f9b34fb"

UDP_IP, UDP_PORT = "127.0.0.1", 5005
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

auth_done = None

async def find_watch(timeout=60):
    print(f"Skeniranje do {timeout}s — rebootaj sat odmah ako nisi...")
    found = None
    deadline = asyncio.get_event_loop().time() + timeout
    while asyncio.get_event_loop().time() < deadline:
        devices = await BleakScanner.discover(timeout=5.0)
        for d in devices:
            addr = d.address.upper()
            name = d.name or ""
            if addr == TARGET_MAC.upper() or TARGET_PREFIX.lower() in name.lower():
                print(f"Nadjen: {d.address}  {d.name}")
                return d.address
        print(f"  Jos nije vidljiv ({int(deadline - asyncio.get_event_loop().time())}s ostalo)...")
    return None

async def auth_handler(auth_key, client, done_event, sender, data):
    print(f"  [AUTH] {data.hex()}")
    if len(data) < 3:
        return
    if data[0] == 0x10 and data[1] == 0x01 and data[2] == 0x01:
        random_num = data[3:]
        print(f"  [AUTH] Random: {random_num.hex()}")
        resp = hmac.new(auth_key, random_num, hashlib.sha256).digest()[:16]
        print(f"  [AUTH] HMAC: {resp.hex()}")
        payload = bytes([0x03, 0x00]) + resp
        await client.write_gatt_char(CHAR_AUTH, payload, response=False)
    elif data[0] == 0x10 and data[1] == 0x01 and data[2] == 0x04:
        print("  [AUTH] USPJESAN!")
        done_event.set()
    elif data[0] == 0x10 and data[1] == 0x01 and data[2] in (0x02, 0x05):
        print(f"  [AUTH] GRESKA kod={data[2]:02x} — pogresni kljuc?")

def hr_notify(sender, data):
    print(f"  [HR raw] {data.hex()}")
    for b in data:
        if 30 <= b <= 220:
            ts = datetime.now().strftime("%H:%M:%S")
            print(f"  >>> PULS: {b} bpm [{ts}]")
            sock.sendto(json.dumps({"hr": b, "ts": ts}).encode(), (UDP_IP, UDP_PORT))
            break

async def main():
    global auth_done
    auth_key = bytes.fromhex(AUTH_KEY_HEX)

    # Scan
    addr = await find_watch(timeout=90)
    if not addr:
        print("Sat nije nadjenn u 90 sekundi. Provjeri da je rebootovan i u blizini.")
        return

    print(f"\nSpajam se na {addr}...")
    async with BleakClient(addr, timeout=20.0) as c:
        print("Spojeno!\n")
        auth_done = asyncio.Event()

        await c.start_notify(
            CHAR_AUTH,
            lambda s, d: asyncio.ensure_future(auth_handler(auth_key, c, auth_done, s, d))
        )
        try:
            await c.start_notify(CHAR_53, hr_notify)
        except Exception:
            pass

        print("Auth flow...")
        await c.write_gatt_char(CHAR_AUTH, bytes([0x02, 0x00, 0x02]), response=False)

        try:
            await asyncio.wait_for(auth_done.wait(), timeout=15.0)
        except asyncio.TimeoutError:
            print("Auth timeout — sat ne odgovara. Kljuc mozda nije tacan.")
            return

        print("\nAuth ok! Startuj HR mjerenje...\n")
        await c.write_gatt_char(CHAR_WRITE, bytes([0x0f, 0x14, 0x01]), response=False)

        print("Slusam HR 120 sekundi (Ctrl+C za prekid)...\n")
        await asyncio.sleep(120)

        await c.write_gatt_char(CHAR_WRITE, bytes([0x0f, 0x14, 0x00]), response=False)

asyncio.run(main())

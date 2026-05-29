"""
Direktan BLE HR monitor za Redmi Watch 3 Active / Mi Band 9.
Zahtijeva auth key (32 hex chars) koji se dobija preko Notify app.

Koriscenje:
  python ble_hr_auth.py <AUTH_KEY_HEX>
  python ble_hr_auth.py 0123456789abcdef0123456789abcdef

Xiaomi BLE v3 auth flow:
  1. Subscribe na auth char (0x0052)
  2. Posalji REQUEST_RANDOM (0x0002, 0x0000, 0x0002)
  3. Dobij random_number od sata
  4. Izracunaj HMAC-SHA256(auth_key, random_number)
  5. Posalji odgovor satu
  6. Sat potvrdi → sada mozemo citati HR

HR zahtjev:
  7. Subscribe na HR notify char
  8. Posalji HR start command
  9. Parsiraj notifikacije
"""

import asyncio
import sys
import os
import hmac
import hashlib
import struct
import json
import socket
from datetime import datetime
from bleak import BleakClient

MAC = "04:DA:28:39:4C:40"

# Xiaomi BLE servisi
SVC_XIAOMI = "0000fe95-0000-1000-8000-00805f9b34fb"
CHAR_AUTH  = "00000052-0000-1000-8000-00805f9b34fb"
CHAR_WRITE = "00000051-0000-1000-8000-00805f9b34fb"
CHAR_53    = "00000053-0000-1000-8000-00805f9b34fb"

# Auth protokol opcodes
CMD_AUTH_REQUEST_RANDOM = bytes([0x02, 0x00, 0x02])
CMD_AUTH_CONFIRM_PREFIX = bytes([0x03, 0x00])

# HR komande (Xiaomi Watch)
CMD_HR_START = bytes([0x0f, 0x14, 0x01])
CMD_HR_STOP  = bytes([0x0f, 0x14, 0x00])

UDP_IP   = "127.0.0.1"
UDP_PORT = 5005

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
auth_event   = asyncio.Event()
hr_responses = []

def parse_hr(data):
    """Pokusaj parsirati HR iz notifikacije."""
    # Format 1: [status, hr_value, ...]
    if len(data) >= 2 and data[0] in (0x00, 0x01, 0x06):
        hr = data[1]
        if 30 <= hr <= 220:
            return hr
    # Format 2: trazimo razumnu vrijednost u bilo kojoj poziciji
    for b in data[1:]:
        if 30 <= b <= 220:
            return b
    return None

async def auth_handler(auth_key_bytes, client, auth_done: asyncio.Event, sender, data):
    print(f"  [AUTH] {data.hex()}")
    # Status byte: 0x10 = response, second byte = command echo
    if data[0] == 0x10 and data[1] == 0x01 and data[2] == 0x01:
        # Sat poslao random number (bytes[3:])
        random_num = data[3:]
        print(f"  [AUTH] Random broj od sata: {random_num.hex()}")
        # Izracunaj HMAC-SHA256
        response = hmac.new(auth_key_bytes, random_num, hashlib.sha256).digest()[:16]
        print(f"  [AUTH] HMAC odgovor: {response.hex()}")
        payload = CMD_AUTH_CONFIRM_PREFIX + response
        await client.write_gatt_char(CHAR_AUTH, payload, response=False)

    elif data[0] == 0x10 and data[1] == 0x01 and data[2] == 0x04:
        print("  [AUTH] Autentifikacija USPJESNA!")
        auth_done.set()

    elif data[0] == 0x10 and data[1] == 0x01 and data[2] in (0x02, 0x05):
        print(f"  [AUTH] GRESKA autentifikacije! Kod: {data[2]:02x} — Pogresni auth key?")

def hr_notify_handler(sender, data):
    print(f"  [HR notif] {data.hex()}")
    hr = parse_hr(data)
    if hr:
        ts = datetime.now().strftime("%H:%M:%S")
        print(f"  >>> PULS: {hr} bpm [{ts}]")
        payload = json.dumps({"hr": hr, "ts": ts}).encode()
        sock.sendto(payload, (UDP_IP, UDP_PORT))
        hr_responses.append(hr)

async def main(auth_key_hex: str):
    auth_key = bytes.fromhex(auth_key_hex.replace(" ", "").replace(":", ""))
    if len(auth_key) != 16:
        print(f"Auth key mora biti 16 bytes (32 hex chars). Dobijen: {len(auth_key)} bytes.")
        return

    print(f"Auth key: {auth_key.hex()}")
    print(f"Spajam se na {MAC}...\n")

    async with BleakClient(MAC, timeout=20.0) as c:
        print("Spojeno!\n")
        auth_done = asyncio.Event()

        # Subscribe na auth char
        await c.start_notify(
            CHAR_AUTH,
            lambda s, d: asyncio.ensure_future(auth_handler(auth_key, c, auth_done, s, d))
        )

        # Subscribe na write char (mozda prima HR odgovore)
        await c.start_notify(CHAR_53, hr_notify_handler)

        # Korak 1: zatrazi random broj od sata
        print("Pokrecemo auth flow...")
        await c.write_gatt_char(CHAR_AUTH, CMD_AUTH_REQUEST_RANDOM, response=False)

        # Cekaj max 10s na auth
        try:
            await asyncio.wait_for(auth_done.wait(), timeout=10.0)
        except asyncio.TimeoutError:
            print("Auth timeout — sat nije odgovorio. Provjeriti auth key.")
            return

        print("\nAuth uspjesan! Trazim HR...\n")

        # Zatrazi HR mjerenje
        await c.write_gatt_char(CHAR_WRITE, CMD_HR_START, response=False)

        # Slušaj HR 60 sekundi
        print("Slusam HR 60 sekundi... (Ctrl+C za prekid)\n")
        try:
            await asyncio.sleep(60)
        except asyncio.CancelledError:
            pass

        await c.write_gatt_char(CHAR_WRITE, CMD_HR_STOP, response=False)
        print(f"\nZavrseno. Mjerenja: {hr_responses}")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Koriscenje: python ble_hr_auth.py <AUTH_KEY_32_HEX>")
        print("Primjer:    python ble_hr_auth.py 0123456789abcdef0123456789abcdef")
        sys.exit(1)

    asyncio.run(main(sys.argv[1]))

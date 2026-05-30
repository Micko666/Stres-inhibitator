"""
Direktni BLE HR monitor za Redmi Watch 3 Active putem Windows WinRT API.
Zaobilazi bleak scan problem - koristi WinRT direktno.

Pokretanje: python -u ble_winrt_hr.py
"""

import asyncio
import hmac
import hashlib
import json
import socket
from datetime import datetime

import winrt.windows.devices.bluetooth as bt
import winrt.windows.devices.bluetooth.genericattributeprofile as gatt
import winrt.windows.storage.streams as streams

MAC          = "04:DA:28:39:4C:40"
AUTH_KEY_HEX = "a77277a213dde02e338a08b1ae3fd712"

SVC_FE95   = "0000fe95-0000-1000-8000-00805f9b34fb"
UUID_AUTH  = "00000052-0000-1000-8000-00805f9b34fb"
UUID_WRITE = "00000051-0000-1000-8000-00805f9b34fb"
UUID_53    = "00000053-0000-1000-8000-00805f9b34fb"

UDP_IP, UDP_PORT = "127.0.0.1", 5005
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

auth_done = asyncio.Event()
char_write_ref = None
hr_values = []

def buf_to_bytes(ibuf):
    reader = streams.DataReader.from_buffer(ibuf)
    n = reader.unconsumed_buffer_length
    return bytes(reader.read_bytes(n))

def bytes_to_buf(data: bytes):
    writer = streams.DataWriter()
    writer.write_bytes(data)
    return writer.detach_buffer()

async def write_char(char, data: bytes):
    buf = bytes_to_buf(data)
    result = await char.write_value_with_result_async(buf)
    return result.status

def auth_notify(sender, args):
    data = buf_to_bytes(args.characteristic_value)
    print(f"  [AUTH] {data.hex()}")
    if len(data) < 3:
        return

    if data[0] == 0x10 and data[1] == 0x01 and data[2] == 0x01:
        random_num = data[3:]
        auth_key   = bytes.fromhex(AUTH_KEY_HEX)
        resp       = hmac.new(auth_key, random_num, hashlib.sha256).digest()[:16]
        payload    = bytes([0x03, 0x00]) + resp
        print(f"  [AUTH] Saljem HMAC: {resp.hex()}")
        asyncio.ensure_future(write_char(sender, payload))

    elif data[0] == 0x10 and data[1] == 0x01 and data[2] == 0x04:
        print("  [AUTH] USPJESAN!")
        auth_done.set()

    elif data[0] == 0x10 and data[1] == 0x01 and data[2] in (0x02, 0x05):
        print(f"  [AUTH] GRESKA! kod={data[2]:02x} — pogresni kljuc?")

def hr_notify(sender, args):
    data = buf_to_bytes(args.characteristic_value)
    print(f"  [HR raw] {data.hex()}")
    for b in data:
        if 30 <= b <= 220:
            ts = datetime.now().strftime("%H:%M:%S")
            print(f"\n  >>> PULS: {b} bpm [{ts}]\n")
            sock.sendto(json.dumps({"hr": b, "ts": ts}).encode(), (UDP_IP, UDP_PORT))
            hr_values.append(b)
            break

def find_char(chars, uuid_target):
    for c in chars:
        if str(c.uuid).lower() == uuid_target.lower():
            return c
    return None

async def main():
    addr_int = int(MAC.replace(":", ""), 16)
    print(f"Spajam se na {MAC}...")

    device = await bt.BluetoothLEDevice.from_bluetooth_address_async(addr_int)
    if device is None:
        print("Sat nije nadjen.")
        return

    print(f"Uredjaj: {device.name}  Status: {device.connection_status}")

    # Dohvati Fe95 service
    import uuid as uuid_mod
    svc_result = await device.get_gatt_services_for_uuid_async(
        uuid_mod.UUID("0000fe95-0000-1000-8000-00805f9b34fb")
    )
    if svc_result.status != 0 or not list(svc_result.services):
        print("Fe95 service nije dostupan.")
        return

    svc = list(svc_result.services)[0]
    print("Fe95 service OK")

    # Dohvati karakteristike
    chars_result = await svc.get_characteristics_async()
    chars = list(chars_result.characteristics)
    print(f"Karakteristike: {[str(c.uuid)[-8:] for c in chars]}")

    char_auth  = find_char(chars, UUID_AUTH)
    char_write = find_char(chars, UUID_WRITE)
    char_53    = find_char(chars, UUID_53)

    if not char_auth:
        print("Auth karakteristika (0052) nije nadjena.")
        return

    # Subscribe na auth notifikacije
    char_auth.add_value_changed(auth_notify)
    cccd_auth = await char_auth.write_client_characteristic_configuration_descriptor_async(
        gatt.GattClientCharacteristicConfigurationDescriptorValue.NOTIFY
    )
    print(f"Auth subscribe: {cccd_auth}")

    # Subscribe na HR notifikacije (0053)
    if char_53:
        char_53.add_value_changed(hr_notify)
        await char_53.write_client_characteristic_configuration_descriptor_async(
            gatt.GattClientCharacteristicConfigurationDescriptorValue.NOTIFY
        )
        print("HR (0053) subscribe OK")

    # Pokreni auth flow
    print("\nPokrecem auth...")
    await write_char(char_auth, bytes([0x02, 0x00, 0x02]))

    try:
        await asyncio.wait_for(auth_done.wait(), timeout=15.0)
    except asyncio.TimeoutError:
        print("Auth timeout — sat ne odgovara na auth request.")
        print("Moguc razlog: sat je vec spojen na telefon (ukljuci BT na telefonu i iskljuci pa probaj opet)")
        return

    print("\nAuth ok! Startam HR mjerenje...")
    if char_write:
        await write_char(char_write, bytes([0x0f, 0x14, 0x01]))

    print("Slusam HR 120 sekundi (Ctrl+C za prekid)...\n")
    try:
        await asyncio.sleep(120)
    except asyncio.CancelledError:
        pass

    if char_write:
        await write_char(char_write, bytes([0x0f, 0x14, 0x00]))

    print(f"\nZavrseno. Mjerenja: {hr_values}")

asyncio.run(main())

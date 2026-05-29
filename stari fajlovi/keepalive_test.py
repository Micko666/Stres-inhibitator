"""
Test: šalje push notifikaciju telefonu svake INTERVAL sekundi via ADB.
Cilj: provjeriti drži li to sat budan (ekran upaljen).

Postavi autolock na satu na 10s, pa pokreni ovaj script.
Gledaj da li se sat ne gasi.
"""

import subprocess
import time

ADB_PATH = r"C:\Users\Korisnik\Desktop\Diplomski\platform-tools\adb.exe"
ADB_HOST = "192.168.1.222:5555"
INTERVAL = 6  # sekundi između notifikacija

def ping(count):
    result = subprocess.run([
        ADB_PATH, "-s", ADB_HOST,
        "shell", "cmd", "notification", "post",
        "-S", "bigtext",
        "-t", "SmartLine VR",
        "hr_ping",
        f"HR aktivan #{count}"
    ], capture_output=True, text=True, timeout=5)

    # Odmah obrisi notifikaciju sa telefona (sat je vec dobio vibraciju)
    subprocess.run([
        ADB_PATH, "-s", ADB_HOST,
        "shell", "cmd", "notification", "cancel",
        "hr_ping"
    ], capture_output=True, timeout=3)

    ok = result.returncode == 0
    print(f"  ping #{count:3d}  {'OK' if ok else 'FAIL: ' + result.stderr.strip()}")
    return ok

# --- main ---
print(f"Spajam ADB na {ADB_HOST}...")
subprocess.run([ADB_PATH, "connect", ADB_HOST], capture_output=True)
time.sleep(1)

print(f"Saljem ping svake {INTERVAL}s — Ctrl+C za stop\n")
print("  Postavi autolock na satu na 10s i gledaj da li se ekran ne gasi.\n")

count = 0
while True:
    count += 1
    try:
        ping(count)
    except Exception as e:
        print(f"  ping #{count:3d}  GRESKA: {e}")
    time.sleep(INTERVAL)

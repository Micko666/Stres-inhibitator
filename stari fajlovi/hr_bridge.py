import subprocess
import re
import socket
import time
import json
from datetime import datetime

ADB_PATH = r"C:\Users\Korisnik\Desktop\Diplomski\platform-tools\adb.exe"
UDP_IP   = "127.0.0.1"  # localhost - mijenjaj na IP laptopa ako saljes drugoj masini
UDP_PORT = 5005

# Regex za izvlacenje HR vrijednosti iz logcata
HR_PATTERNS = [
    re.compile(r"single_heart_rate=\[HrItem\([^)]*hr=(\d+)"),
    re.compile(r"latestHrRecord=HrItem\([^)]*hr=(\d+)"),
    re.compile(r"\bhr=(\d+)\b"),
]

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
last_hr = None
last_sent = 0

def extract_hr(line):
    for pat in HR_PATTERNS:
        m = pat.search(line)
        if m:
            val = int(m.group(1))
            if 30 <= val <= 220:  # realan raspon pulsa
                return val
    return None

print(f"HR Bridge pokrenut — slusam logcat, saljem UDP na {UDP_IP}:{UDP_PORT}")
print("Pritisni Ctrl+C za prekid.\n")

proc = subprocess.Popen(
    [ADB_PATH, "logcat", "-v", "time"],
    stdout=subprocess.PIPE,
    stderr=subprocess.DEVNULL,
    text=True,
    encoding="utf-8",
    errors="replace",
    bufsize=1
)

try:
    for line in proc.stdout:
        # Samo linije koje se ticu HR u Mi Fitness
        if "hr=" not in line and "HrItem" not in line and "heart_rate" not in line.lower():
            continue
        if "xiaomi" not in line.lower() and "fitness" not in line.lower() and "wearable" not in line.lower():
            continue
        hr = extract_hr(line)
        if hr and (hr != last_hr or time.time() - last_sent > 5):
            last_hr = hr
            last_sent = time.time()
            ts = datetime.now().strftime("%H:%M:%S")
            payload = json.dumps({"hr": hr, "ts": ts}).encode()
            sock.sendto(payload, (UDP_IP, UDP_PORT))
            print(f"[{ts}] Puls: {hr} bpm  -> UDP poslan")
except KeyboardInterrupt:
    print("\nZaustavljeno.")
finally:
    proc.terminate()
    sock.close()

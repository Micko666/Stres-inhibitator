"""
HR Dashboard v2 - podrska za Band 9 I Redmi Watch 3 Active
Citanje putem ADB WiFi logcat, Flask dashboard + UDP izlaz za Unity.
"""

import subprocess
import re
import threading
import time
import json
from datetime import datetime
from flask import Flask, jsonify, render_template_string
import socket

ADB_PATH        = r"C:\Users\Korisnik\Desktop\Diplomski\platform-tools\adb.exe"
ADB_HOST_SAVED  = "192.168.1.222:5555"  # fallback ako scan ne nadje nista
UDP_IP          = "127.0.0.1"
UDP_PORT        = 5005

def find_adb_host(timeout=0.3):
    """Skenira lokalnu /24 podmrezu za port 5555. Vraca 'IP:5555' ili saved fallback."""
    import ipaddress, concurrent.futures

    # Uzmi lokalnu IP adresu laptopa
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))
        local_ip = s.getsockname()[0]
        s.close()
    except Exception:
        return ADB_HOST_SAVED

    subnet = str(ipaddress.IPv4Network(local_ip + "/24", strict=False).network_address)
    base   = ".".join(local_ip.split(".")[:3])
    hosts  = [f"{base}.{i}" for i in range(1, 255)]

    print(f"[ADB] Trazim uredjaj na {base}.0/24 ...")

    def probe(ip):
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            s.settimeout(timeout)
            s.connect((ip, 5555))
            s.close()
            return ip
        except Exception:
            return None

    with concurrent.futures.ThreadPoolExecutor(max_workers=64) as ex:
        futures = {ex.submit(probe, ip): ip for ip in hosts}
        for f in concurrent.futures.as_completed(futures):
            result = f.result()
            if result:
                # Otkaži ostale
                for remaining in futures:
                    remaining.cancel()
                print(f"[ADB] Nadjen uredjaj: {result}:5555")
                return f"{result}:5555"

    print(f"[ADB] Nije nadjen uredjaj, koristim saved: {ADB_HOST_SAVED}")
    return ADB_HOST_SAVED

ADB_HOST = find_adb_host()

# Siroki set patterna - hvata i Band 9 i Watch 3
HR_PATTERNS = [
    # Xiaomi Wearable (Redmi Watch) - batch sync format
    re.compile(r"HrItem\([^)]*hr=(\d+)"),
    # Mi Fitness (Band 9) - single reading format
    re.compile(r"single_heart_rate=\[HrItem\([^)]*hr=(\d+)"),
    re.compile(r"latestHrRecord=HrItem\([^)]*hr=(\d+)"),
    # Generic
    re.compile(r"realTimeHeartRate[^=]*=\s*(\d+)"),
    re.compile(r"\bhr=(\d+)\b"),
]

HR_KEYWORDS = ["HrItem", "hr=", "heart_rate", "HeartRate", "HomeDataRepository"]
DEVICE_KEYWORDS = ["xiaomi", "fitness", "wearable", "health", "mifit", "band", "watch"]

hr_data = {"current": 0, "history": [], "last_update": "-", "source": ""}
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
_highest_watch_ts = 0  # globalni tracker - emituj samo kad dodje noviji HrItem

KEEPALIVE_SEC = 180  # šalji notifikaciju svakih 3 minute

def keepalive_pinger():
    """Šalje push notifikaciju telefonu svake 3 minute da drži sat budan."""
    time.sleep(10)  # čekaj da se ADB stabilizuje
    count = 0
    while True:
        time.sleep(KEEPALIVE_SEC)
        count += 1
        try:
            subprocess.run([
                ADB_PATH, "-s", ADB_HOST,
                "shell", "cmd", "notification", "post",
                "-S", "bigtext",
                "-t", f"Vr #{count}",
                "hr_keepalive",
                "HR monitoring aktivan"
            ], capture_output=True, timeout=5)
            print(f"[KEEPALIVE] Notifikacija #{count} poslana")
        except Exception as e:
            print(f"[KEEPALIVE] Greska: {e}")

HTML = """<!DOCTYPE html>
<html><head>
<meta charset="utf-8">
<meta http-equiv="refresh" content="2">
<title>HR Monitor SmartLine VR</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { background:#0d0d0d; color:#fff; font-family:'Segoe UI',sans-serif;
         display:flex; flex-direction:column; align-items:center; min-height:100vh; padding:40px 20px; }
  h1 { font-size:1.4rem; color:#888; margin-bottom:40px; letter-spacing:3px; text-transform:uppercase; }
  .bpm-box { background:#111; border-radius:24px; padding:50px 80px; text-align:center; margin-bottom:40px;
             border:2px solid {{ color }}; box-shadow:0 0 40px {{ glow }}; }
  .bpm-value { font-size:9rem; font-weight:700; color:{{ color }}; line-height:1; }
  .bpm-label { font-size:1.5rem; color:#666; margin-top:10px; letter-spacing:4px; }
  .zone-badge { display:inline-block; background:{{ color }}22; color:{{ color }};
                border:1px solid {{ color }}; border-radius:20px; padding:6px 20px;
                font-size:0.9rem; margin-top:16px; letter-spacing:2px; }
  .meta { color:#555; font-size:0.85rem; margin-bottom:40px; }
  .graph { background:#111; border-radius:16px; padding:20px; width:100%; max-width:700px; }
  .graph-title { color:#555; font-size:0.8rem; letter-spacing:2px; margin-bottom:16px; }
  .bars { display:flex; align-items:flex-end; gap:4px; height:100px; }
  .bar { flex:1; border-radius:4px 4px 0 0; min-height:2px; opacity:0.85; }
  .zones { display:flex; gap:12px; justify-content:center; margin-top:20px; flex-wrap:wrap; }
  .zone { font-size:0.75rem; padding:4px 12px; border-radius:10px; }
</style>
</head><body>
<h1>SmartLine VR Heart Rate Monitor</h1>
<div class="bpm-box">
  <div class="bpm-value">{{ bpm }}</div>
  <div class="bpm-label">BPM</div>
  <div class="zone-badge">{{ zone }}</div>
</div>
<div class="meta">Zadnje mjerenje: {{ last_update }}</div>
<div class="graph">
  <div class="graph-title">HISTORIJA (zadnjih 30 mjerenja)</div>
  <div class="bars">
    {% for h in history %}
    <div class="bar" style="height:{{ h.pct }}%; background:{{ h.color }};"></div>
    {% endfor %}
  </div>
</div>
<div class="zones">
  <span class="zone" style="background:#1a3a4a;color:#4af">Odmor &lt;60</span>
  <span class="zone" style="background:#1a3a1a;color:#4f4">Lagano 60-100</span>
  <span class="zone" style="background:#3a3a1a;color:#ff4">Umjereno 100-140</span>
  <span class="zone" style="background:#3a1a1a;color:#f84">Intenzivno 140-170</span>
  <span class="zone" style="background:#2a0000;color:#f44">Maksimalno &gt;170</span>
</div>
</body></html>"""

def hr_to_zone(hr):
    if hr < 60:  return "ODMOR",      "#4af", "rgba(64,170,255,0.3)"
    if hr < 100: return "LAGANO",     "#4f4", "rgba(64,255,64,0.3)"
    if hr < 140: return "UMJERENO",   "#ff4", "rgba(255,255,64,0.3)"
    if hr < 170: return "INTENZIVNO", "#f84", "rgba(255,136,64,0.3)"
    return "MAKSIMALNO", "#f44", "rgba(255,64,64,0.3)"

def bar_color(hr):
    _, c, _ = hr_to_zone(hr)
    return c

app = Flask(__name__)

@app.route("/")
def index():
    bpm = hr_data["current"] or 0
    history_raw = hr_data["history"][-30:]
    max_hr = max((h for h in history_raw), default=100)
    max_hr = max(max_hr, 80)
    history = [{"pct": max(4, int(h / max_hr * 100)), "color": bar_color(h)} for h in history_raw]
    zone, color, glow = hr_to_zone(bpm)
    return render_template_string(HTML,
        bpm=bpm if bpm else "-",
        zone=zone, color=color, glow=glow,
        history=history, last_update=hr_data["last_update"]
    )

@app.route("/api/hr")
def api_hr():
    return jsonify({"hr": hr_data["current"], "ts": hr_data["last_update"]})

HR_ITEM_TS_RE = re.compile(r"HrItem\(sid=\d+,\s*time=(\d+),\s*hr=(\d+)\)")

def extract_hr_with_ts(line):
    """Vrati (hr, watch_unix_ts) — najnoviji HrItem u liniji. watch_unix_ts=0 ako nema ts."""
    items = HR_ITEM_TS_RE.findall(line)
    if items:
        valid = [(int(t), int(hr)) for t, hr in items if 30 <= int(hr) <= 220]
        if valid:
            ts, hr = max(valid, key=lambda x: x[0])
            return hr, ts
    for pat in HR_PATTERNS:
        m = pat.search(line)
        if m:
            val = int(m.group(1))
            if 30 <= val <= 220:
                return val, 0
    return None, 0

def extract_hr(line):
    hr, _ = extract_hr_with_ts(line)
    return hr

def line_matches(line):
    line_low = line.lower()
    has_hr = any(kw in line_low for kw in HR_KEYWORDS)
    has_dev = any(kw in line_low for kw in DEVICE_KEYWORDS)
    return has_hr and has_dev

LOGCAT_TS_RE = re.compile(r"^(\d{2}-\d{2} \d{2}:\d{2}:\d{2})")
MAX_AGE_SEC  = 120  # ignoriši logcat linije starije od 2 minute

def logcat_line_age(line):
    """Vrati starost logcat linije u sekundama. -1 ako timestamp nije parsiran."""
    m = LOGCAT_TS_RE.match(line)
    if not m:
        return -1
    try:
        now = datetime.now()
        lt  = datetime.strptime(f"{now.year}-{m.group(1)}", "%Y-%m-%d %H:%M:%S")
        diff = (now - lt).total_seconds()
        # Korekcija za ponocni prelaz
        if diff < -3600:
            diff += 86400
        return diff
    except Exception:
        return -1

def send_notification(title, text):
    try:
        subprocess.run([
            ADB_PATH, "-s", ADB_HOST,
            "shell", "cmd", "notification", "post",
            "-S", "bigtext", "-t", title,
            "smartline_hr", text
        ], capture_output=True, timeout=5)
    except Exception:
        pass

def logcat_reader():
    result = subprocess.run([ADB_PATH, "connect", ADB_HOST], capture_output=True, text=True)
    connected = "connected" in result.stdout.lower() or "already" in result.stdout.lower()
    time.sleep(1)

    if connected:
        send_notification("SmartLine VR", "HR monitoring pokrenut ✓")
        print("[ADB] Notifikacija poslana - sat spojen")

    last_hr = None
    last_sent = 0
    proc = subprocess.Popen(
        [ADB_PATH, "-s", ADB_HOST, "logcat", "-v", "time"],
        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
        text=True, encoding="utf-8", errors="replace", bufsize=1
    )
    print(f"[ADB] Spojen na {ADB_HOST} - slusam logcat (ignorisem linije starije od {MAX_AGE_SEC}s)...")
    for line in proc.stdout:
        # Preskoči stare linije (logcat buffer replay)
        age = logcat_line_age(line)
        if age > MAX_AGE_SEC:
            continue

        if not line_matches(line):
            continue
        hr, watch_ts = extract_hr_with_ts(line)
        if not hr:
            continue

        global _highest_watch_ts
        # Ako ima watch timestamp — emituj samo kad je NOVIJI od zadnjeg
        if watch_ts > 0:
            if watch_ts <= _highest_watch_ts:
                continue  # duplikat ili stariji HrItem, preskoči
            _highest_watch_ts = watch_ts

        # Throttle: ne šalji isti hr više od jednom u 3s (za linije bez ts)
        if watch_ts == 0 and hr == last_hr and time.time() - last_sent < 3:
            continue

        last_hr = hr
        last_sent = time.time()
        ts = datetime.now().strftime("%H:%M:%S")
        hr_data["current"] = hr
        hr_data["last_update"] = ts
        hr_data["history"].append(hr)
        if len(hr_data["history"]) > 200:
            hr_data["history"] = hr_data["history"][-200:]
        payload = json.dumps({"hr": hr, "ts": ts}).encode()
        sock.sendto(payload, (UDP_IP, UDP_PORT))
        print(f"[{ts}]  Puls: {hr} bpm  (watch_ts={watch_ts}, age={age:.0f}s)")

if __name__ == "__main__":
    t = threading.Thread(target=logcat_reader, daemon=True)
    t.start()
    k = threading.Thread(target=keepalive_pinger, daemon=True)
    k.start()
    print("\nDashboard: http://127.0.0.1:8888")
    print(f"Keepalive: notifikacija svake {KEEPALIVE_SEC//60} minute\n")
    app.run(host="0.0.0.0", port=8888, debug=False)

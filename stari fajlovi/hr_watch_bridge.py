"""
HR Bridge za Redmi Watch 3 Active.
Forsira BLE sync svakih 30s, pola log svakih 5s, salje UDP + dashboard.

Pokretanje: python hr_watch_bridge.py
Dashboard:  http://127.0.0.1:8888
"""

import subprocess
import re
import threading
import time
import json
from datetime import datetime
from flask import Flask, jsonify, render_template_string
import socket

ADB          = r"C:\Users\Korisnik\Desktop\Diplomski\platform-tools\adb.exe"
ADB_HOST     = "192.168.1.222:5555"
LOG_PATH     = "/sdcard/Android/data/com.xiaomi.wearable/files/log/XiaomiFit.main.log"
UDP_IP       = "127.0.0.1"
UDP_PORT     = 5005
POLL_SEC     = 5   # citaj log svakih 5s
SYNC_SEC     = 30  # forsira BLE sync svakih 30s

HR_ITEM_RE = re.compile(r"HrItem\(sid=\d+,\s*time=(\d+),\s*hr=(\d+)\)")

hr_data = {"current": 0, "history": [], "last_update": "-", "last_ts": 0}
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

HTML = """<!DOCTYPE html>
<html><head>
<meta charset="utf-8">
<meta http-equiv="refresh" content="3">
<title>HR Monitor - Redmi Watch 3</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { background:#0d0d0d; color:#fff; font-family:'Segoe UI',sans-serif;
         display:flex; flex-direction:column; align-items:center; min-height:100vh; padding:40px 20px; }
  h1 { font-size:1.2rem; color:#888; margin-bottom:40px; letter-spacing:3px; }
  .bpm-box { background:#111; border-radius:24px; padding:50px 80px; text-align:center; margin-bottom:40px;
             border:2px solid {{ color }}; box-shadow:0 0 40px {{ glow }}; }
  .bpm-value { font-size:9rem; font-weight:700; color:{{ color }}; line-height:1; }
  .bpm-label { font-size:1.5rem; color:#666; margin-top:10px; letter-spacing:4px; }
  .zone-badge { display:inline-block; background:{{ color }}22; color:{{ color }};
                border:1px solid {{ color }}; border-radius:20px; padding:6px 20px;
                font-size:0.9rem; margin-top:16px; letter-spacing:2px; }
  .meta { color:#555; font-size:0.85rem; margin-bottom:30px; }
  .graph { background:#111; border-radius:16px; padding:20px; width:100%; max-width:700px; }
  .bars { display:flex; align-items:flex-end; gap:4px; height:100px; }
  .bar { flex:1; border-radius:4px 4px 0 0; min-height:2px; opacity:0.85; }
  .zones { display:flex; gap:10px; justify-content:center; margin-top:20px; flex-wrap:wrap; }
  .zone { font-size:0.75rem; padding:4px 12px; border-radius:10px; }
</style>
</head><body>
<h1>SmartLine VR - Redmi Watch 3 Active</h1>
<div class="bpm-box">
  <div class="bpm-value">{{ bpm }}</div>
  <div class="bpm-label">BPM</div>
  <div class="zone-badge">{{ zone }}</div>
</div>
<div class="meta">Zadnje: {{ last_update }} | Osvjezava se svakih 3s</div>
<div class="graph">
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
    hist = hr_data["history"][-30:]
    mx = max((h for h in hist), default=100)
    mx = max(mx, 80)
    history = [{"pct": max(4, int(h / mx * 100)), "color": bar_color(h)} for h in hist]
    zone, color, glow = hr_to_zone(bpm)
    return render_template_string(HTML,
        bpm=bpm if bpm else "-", zone=zone, color=color, glow=glow,
        history=history, last_update=hr_data["last_update"])

@app.route("/api/hr")
def api_hr():
    return jsonify({"hr": hr_data["current"], "ts": hr_data["last_update"]})

def force_sync():
    subprocess.run(
        [ADB, "-s", ADB_HOST, "shell",
         "am broadcast -a com.xiaomi.fitness.ACTION_SYNC_DEVICE"],
        capture_output=True, timeout=5
    )
    subprocess.run(
        [ADB, "-s", ADB_HOST, "shell",
         "am startservice -n com.mc.xiaomi1/.sync.SyncService"],
        capture_output=True, timeout=5
    )

def poll_and_update():
    subprocess.run([ADB, "connect", ADB_HOST], capture_output=True, timeout=5)
    time.sleep(1)
    force_sync()
    print(f"[Bridge] Start — poll svakih {POLL_SEC}s, sync svakih {SYNC_SEC}s")

    last_sync_time = time.time()

    while True:
        try:
            # Forced sync svakih SYNC_SEC sekundi
            if time.time() - last_sync_time >= SYNC_SEC:
                force_sync()
                last_sync_time = time.time()

            # Citaj zadnjih 200 linija loga
            result = subprocess.run(
                [ADB, "-s", ADB_HOST, "shell", f"tail -200 {LOG_PATH}"],
                capture_output=True, text=True,
                encoding="utf-8", errors="replace", timeout=10
            )

            # Trazi sve HrItem-ove, uzmi onaj sa najvecim timestamp-om
            items = HR_ITEM_RE.findall(result.stdout)
            valid = [(int(t), int(hr)) for t, hr in items if 30 <= int(hr) <= 220]

            if valid:
                newest_ts, newest_hr = max(valid, key=lambda x: x[0])
                if newest_ts > hr_data["last_ts"]:
                    hr_data["last_ts"] = newest_ts
                    now = datetime.now().strftime("%H:%M:%S")
                    hr_data["current"] = newest_hr
                    hr_data["last_update"] = now
                    hr_data["history"].append(newest_hr)
                    if len(hr_data["history"]) > 200:
                        hr_data["history"] = hr_data["history"][-200:]
                    payload = json.dumps({"hr": newest_hr, "ts": now}).encode()
                    sock.sendto(payload, (UDP_IP, UDP_PORT))
                    print(f"[{now}] Puls: {newest_hr} bpm  (watch_ts={newest_ts})")

        except Exception as e:
            print(f"[Bridge] Greska: {e}")

        time.sleep(POLL_SEC)

if __name__ == "__main__":
    threading.Thread(target=poll_and_update, daemon=True).start()
    print(f"\nDashboard: http://127.0.0.1:8888")
    print(f"Poll svakih {POLL_SEC}s | Forced BLE sync svakih {SYNC_SEC}s\n")
    app.run(host="0.0.0.0", port=8888, debug=False)

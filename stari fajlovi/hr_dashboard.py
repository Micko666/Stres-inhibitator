import subprocess
import re
import threading
import time
import json
from datetime import datetime
from flask import Flask, jsonify, render_template_string
import socket

ADB_PATH   = r"C:\Users\Korisnik\Desktop\Diplomski\platform-tools\adb.exe"
ADB_HOST   = "192.168.1.222:5555"  # WiFi - ne treba USB!
UDP_IP     = "127.0.0.1"
UDP_PORT   = 5005

HR_PATTERNS = [
    re.compile(r"single_heart_rate=\[HrItem\([^)]*hr=(\d+)"),
    re.compile(r"latestHrRecord=HrItem\([^)]*hr=(\d+)"),
    re.compile(r"\bhr=(\d+)\b"),
]

hr_data = {"current": 0, "history": [], "last_update": "—"}
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

HTML = """<!DOCTYPE html>
<html><head>
<meta charset="utf-8">
<meta http-equiv="refresh" content="2">
<title>HR Monitor — SmartLine VR</title>
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
<h1>❤ SmartLine VR — Heart Rate Monitor</h1>
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
  <span class="zone" style="background:#1a3a1a;color:#4f4">Lagano 60–100</span>
  <span class="zone" style="background:#3a3a1a;color:#ff4">Umjereno 100–140</span>
  <span class="zone" style="background:#3a1a1a;color:#f84">Intenzivno 140–170</span>
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
        bpm=bpm if bpm else "—",
        zone=zone,
        color=color,
        glow=glow,
        history=history,
        last_update=hr_data["last_update"]
    )

@app.route("/api/hr")
def api_hr():
    return jsonify({"hr": hr_data["current"], "ts": hr_data["last_update"]})

def extract_hr(line):
    for pat in HR_PATTERNS:
        m = pat.search(line)
        if m:
            val = int(m.group(1))
            if 30 <= val <= 220:
                return val
    return None

def logcat_reader():
    # Spoji na telefon via WiFi
    subprocess.run([ADB_PATH, "connect", ADB_HOST], capture_output=True)
    time.sleep(1)
    last_hr = None
    last_sent = 0
    proc = subprocess.Popen(
        [ADB_PATH, "-s", ADB_HOST, "logcat", "-v", "time"],
        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
        text=True, encoding="utf-8", errors="replace", bufsize=1
    )
    print(f"[ADB] Spojen na {ADB_HOST} — slusam logcat...")
    for line in proc.stdout:
        if "hr=" not in line and "HrItem" not in line and "heart_rate" not in line.lower():
            continue
        if "xiaomi" not in line.lower() and "fitness" not in line.lower() and "wearable" not in line.lower():
            continue
        hr = extract_hr(line)
        if hr and (hr != last_hr or time.time() - last_sent > 5):
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
            print(f"[{ts}]  Puls: {hr} bpm")

if __name__ == "__main__":
    t = threading.Thread(target=logcat_reader, daemon=True)
    t.start()
    print("\nDashboard pokrenut na: http://127.0.0.1:8888\n")
    app.run(host="0.0.0.0", port=8888, debug=False)

"""
HR Dashboard v3 - Band 9 / Redmi Watch 3 Active -> Unity VR bridge.

Reads HR from Mi Fitness / Xiaomi Wearable via ADB WiFi logcat, serves a local
Flask dashboard and streams UDP JSON packets to one or more Unity hosts.

Portability (no hardcoded machine paths):
- ADB path: --adb-path > config file > <repo>/platform-tools/adb.exe > "adb" on PATH
- All settings overridable via CLI or hr_bridge_config.json next to this script.

Packet format v1 (schemaVersion=1), backwards compatible ("hr" field kept):
  {"schemaVersion":1,"sequence":155,"hr":82,
   "sourceTimestampUtc":"2026-07-12T19:31:50.000Z",
   "sentAtUtc":"2026-07-12T19:31:50.120Z",
   "source":"xiaomi_mifitness_logcat"}

Unity accepts BOTH this format and the legacy {"hr":82,"ts":"19:31:50"}
(legacy packets are flagged as such on the Unity side).

CLI:
  python hr_dashboard_v2.py --adb-path C:/adb/adb.exe --device 192.168.1.222:5555 \
      --unity-host 192.168.1.50 --unity-port 5005 --dashboard-port 8888

Default Unity targets are 127.0.0.1 AND the local subnet broadcast address, so a
standalone Quest 3 on the same LAN receives packets without extra configuration
(loopback-only delivery was a proven blocker for standalone builds).

No sensitive data (auth keys, tokens, account ids) is read or logged.
"""

import argparse
import ipaddress
import json
import os
import re
import socket
import subprocess
import sys
import threading
import time
from datetime import datetime, timezone

from flask import Flask, jsonify, render_template_string

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
CONFIG_FILE = os.path.join(SCRIPT_DIR, "hr_bridge_config.json")

DEFAULTS = {
    "adb_path": "",                       # resolved below when empty
    "device": "",                         # ADB serial/host:port; empty = scan + fallback
    "device_fallback": "192.168.1.222:5555",
    "unity_hosts": [],                    # extra hosts; loopback+broadcast always included
    "unity_port": 5005,
    "dashboard_port": 8888,
    "keepalive_seconds": 180,
    "max_logcat_age_seconds": 120,
    "source_id": "xiaomi_mifitness_logcat",
}


def load_config():
    cfg = dict(DEFAULTS)
    if os.path.exists(CONFIG_FILE):
        try:
            with open(CONFIG_FILE, "r", encoding="utf-8") as f:
                user = json.load(f)
            for k in cfg:
                if k in user:
                    cfg[k] = user[k]
            print(f"[CFG] Loaded {CONFIG_FILE}")
        except Exception as e:
            print(f"[CFG] WARNING: cannot parse {CONFIG_FILE}: {e} — using defaults")
    return cfg


def parse_args(cfg):
    ap = argparse.ArgumentParser(description="Xiaomi HR -> Unity UDP bridge")
    ap.add_argument("--adb-path", default=cfg["adb_path"])
    ap.add_argument("--device", default=cfg["device"],
                    help="ADB device (host:port for WiFi ADB). Empty = subnet scan + fallback")
    ap.add_argument("--unity-host", action="append", default=None,
                    help="Additional Unity host to send UDP to (repeatable)")
    ap.add_argument("--unity-port", type=int, default=cfg["unity_port"])
    ap.add_argument("--dashboard-port", type=int, default=cfg["dashboard_port"])
    args = ap.parse_args()
    if args.unity_host is None:
        args.unity_host = list(cfg["unity_hosts"])
    return args


def resolve_adb_path(cli_value):
    """CLI > config > <repo>/platform-tools/adb.exe > adb on PATH."""
    candidates = []
    if cli_value:
        candidates.append(cli_value)
    exe = "adb.exe" if os.name == "nt" else "adb"
    candidates.append(os.path.join(SCRIPT_DIR, "platform-tools", exe))
    candidates.append(exe)  # PATH
    for c in candidates:
        if os.path.sep in c or c.endswith(".exe"):
            if os.path.exists(c):
                return c
        else:
            return c  # bare name — let the OS resolve it
    print("[ADB] ERROR: adb executable not found. Use --adb-path or place "
          "platform-tools next to this script.")
    sys.exit(1)


def local_ip_and_broadcast():
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))
        local_ip = s.getsockname()[0]
        s.close()
        net = ipaddress.IPv4Network(local_ip + "/24", strict=False)
        return local_ip, str(net.broadcast_address)
    except Exception:
        return None, None


def find_adb_host(fallback, timeout=0.3):
    """Scan the local /24 for TCP 5555. Returns 'IP:5555' or the fallback."""
    import concurrent.futures
    local_ip, _ = local_ip_and_broadcast()
    if not local_ip:
        return fallback
    base = ".".join(local_ip.split(".")[:3])
    hosts = [f"{base}.{i}" for i in range(1, 255)]
    print(f"[ADB] Scanning {base}.0/24 for :5555 ...")

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
            if f.result():
                for r in futures:
                    r.cancel()
                found = f"{f.result()}:5555"
                print(f"[ADB] Found device: {found}")
                return found
    print(f"[ADB] No device found, using fallback: {fallback}")
    return fallback


# ── HR parsing (unchanged, proven patterns) ─────────────────────────────────

HR_PATTERNS = [
    re.compile(r"HrItem\([^)]*hr=(\d+)"),
    re.compile(r"single_heart_rate=\[HrItem\([^)]*hr=(\d+)"),
    re.compile(r"latestHrRecord=HrItem\([^)]*hr=(\d+)"),
    re.compile(r"realTimeHeartRate[^=]*=\s*(\d+)"),
    re.compile(r"\bhr=(\d+)\b"),
]
HR_KEYWORDS = ["HrItem", "hr=", "heart_rate", "HeartRate", "HomeDataRepository"]
DEVICE_KEYWORDS = ["xiaomi", "fitness", "wearable", "health", "mifit", "band", "watch"]
HR_ITEM_TS_RE = re.compile(r"HrItem\(sid=\d+,\s*time=(\d+),\s*hr=(\d+)\)")
LOGCAT_TS_RE = re.compile(r"^(\d{2}-\d{2} \d{2}:\d{2}:\d{2})")


def extract_hr_with_ts(line):
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


def line_matches(line):
    low = line.lower()
    return any(k in low for k in HR_KEYWORDS) and any(k in low for k in DEVICE_KEYWORDS)


def logcat_line_age(line, max_age):
    m = LOGCAT_TS_RE.match(line)
    if not m:
        return -1
    try:
        now = datetime.now()
        lt = datetime.strptime(f"{now.year}-{m.group(1)}", "%Y-%m-%d %H:%M:%S")
        diff = (now - lt).total_seconds()
        if diff < -3600:
            diff += 86400
        return diff
    except Exception:
        return -1


# ── Bridge ──────────────────────────────────────────────────────────────────

class Bridge:
    def __init__(self, cfg, args):
        self.cfg = cfg
        self.adb = resolve_adb_path(args.adb_path)
        self.device = args.device or find_adb_host(cfg["device_fallback"])
        self.unity_port = args.unity_port
        self.dashboard_port = args.dashboard_port
        self.source_id = cfg["source_id"]
        self.max_age = cfg["max_logcat_age_seconds"]

        # UDP targets: loopback + subnet broadcast + any explicit hosts.
        # Default must not be loopback-only (standalone Quest requirement).
        _, broadcast = local_ip_and_broadcast()
        targets = ["127.0.0.1"]
        if broadcast:
            targets.append(broadcast)
        for h in args.unity_host:
            if h not in targets:
                targets.append(h)
        self.targets = targets

        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)

        self.sequence = 0
        self.hr_data = {"current": 0, "history": [], "last_update": "-"}
        self._highest_watch_ts = 0
        self._last_hr = None
        self._last_sent = 0.0

    # ── send ──
    def emit(self, hr, watch_ts):
        now_utc = datetime.now(timezone.utc)
        src_iso = (datetime.fromtimestamp(watch_ts, tz=timezone.utc).isoformat()
                   .replace("+00:00", "Z")) if watch_ts > 0 else \
                  now_utc.isoformat().replace("+00:00", "Z")
        packet = {
            "schemaVersion": 1,
            "sequence": self.sequence,
            "hr": hr,                                   # backwards-compatible field
            "sourceTimestampUtc": src_iso,
            "sentAtUtc": now_utc.isoformat().replace("+00:00", "Z"),
            "source": self.source_id,
        }
        self.sequence += 1
        payload = json.dumps(packet).encode()
        for host in self.targets:
            try:
                self.sock.sendto(payload, (host, self.unity_port))
            except OSError:
                pass  # unreachable target must not stop the stream

        ts_local = datetime.now().strftime("%H:%M:%S")
        self.hr_data["current"] = hr
        self.hr_data["last_update"] = ts_local
        self.hr_data["history"].append(hr)
        self.hr_data["history"] = self.hr_data["history"][-200:]
        print(f"[{ts_local}] HR {hr} bpm  seq={packet['sequence']} watch_ts={watch_ts}")

    def notify_phone(self, title, text):
        try:
            subprocess.run(
                [self.adb, "-s", self.device, "shell", "cmd", "notification", "post",
                 "-S", "bigtext", "-t", title, "stress_vr_hr", text],
                capture_output=True, timeout=5)
        except Exception:
            pass

    # ── logcat with reconnect loop ──
    def logcat_loop(self):
        backoff = 2
        while True:
            try:
                r = subprocess.run([self.adb, "connect", self.device],
                                   capture_output=True, text=True, timeout=15)
                ok = "connected" in (r.stdout or "").lower() or "already" in (r.stdout or "").lower()
                print(f"[ADB] connect {self.device}: {'OK' if ok else (r.stdout or '').strip()}")
                if ok:
                    self.notify_phone("Stress VR", "HR monitoring aktivan")
                    backoff = 2

                proc = subprocess.Popen(
                    [self.adb, "-s", self.device, "logcat", "-v", "time"],
                    stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                    text=True, encoding="utf-8", errors="replace", bufsize=1)
                print(f"[ADB] logcat streaming (ignoring lines older than {self.max_age}s)")

                for line in proc.stdout:
                    age = logcat_line_age(line, self.max_age)
                    if age > self.max_age or not line_matches(line):
                        continue
                    hr, watch_ts = extract_hr_with_ts(line)
                    if not hr:
                        continue
                    if watch_ts > 0:
                        if watch_ts <= self._highest_watch_ts:
                            continue
                        self._highest_watch_ts = watch_ts
                    elif hr == self._last_hr and time.time() - self._last_sent < 3:
                        continue
                    self._last_hr = hr
                    self._last_sent = time.time()
                    self.emit(hr, watch_ts)

                print("[ADB] logcat stream ended — reconnecting...")
            except Exception as e:
                print(f"[ADB] logcat error: {e} — reconnecting in {backoff}s")
            time.sleep(backoff)
            backoff = min(backoff * 2, 30)

    def keepalive_loop(self):
        time.sleep(10)
        count = 0
        while True:
            time.sleep(self.cfg["keepalive_seconds"])
            count += 1
            self.notify_phone(f"Stress VR #{count}", "HR monitoring aktivan")
            print(f"[KEEPALIVE] #{count}")


# ── Dashboard (unchanged visuals) ───────────────────────────────────────────

HTML = """<!DOCTYPE html>
<html><head><meta charset="utf-8"><meta http-equiv="refresh" content="2">
<title>HR Monitor Stress VR</title>
<style>
 *{margin:0;padding:0;box-sizing:border-box}
 body{background:#0d0d0d;color:#fff;font-family:'Segoe UI',sans-serif;display:flex;
      flex-direction:column;align-items:center;min-height:100vh;padding:40px 20px}
 h1{font-size:1.4rem;color:#888;margin-bottom:40px;letter-spacing:3px;text-transform:uppercase}
 .bpm-box{background:#111;border-radius:24px;padding:50px 80px;text-align:center;margin-bottom:40px;
          border:2px solid {{ color }};box-shadow:0 0 40px {{ glow }}}
 .bpm-value{font-size:9rem;font-weight:700;color:{{ color }};line-height:1}
 .bpm-label{font-size:1.5rem;color:#666;margin-top:10px;letter-spacing:4px}
 .zone-badge{display:inline-block;background:{{ color }}22;color:{{ color }};border:1px solid {{ color }};
             border-radius:20px;padding:6px 20px;font-size:0.9rem;margin-top:16px;letter-spacing:2px}
 .meta{color:#555;font-size:0.85rem;margin-bottom:40px}
 .graph{background:#111;border-radius:16px;padding:20px;width:100%;max-width:700px}
 .graph-title{color:#555;font-size:0.8rem;letter-spacing:2px;margin-bottom:16px}
 .bars{display:flex;align-items:flex-end;gap:4px;height:100px}
 .bar{flex:1;border-radius:4px 4px 0 0;min-height:2px;opacity:0.85}
</style></head><body>
<h1>Stress VR Heart Rate Monitor</h1>
<div class="bpm-box"><div class="bpm-value">{{ bpm }}</div><div class="bpm-label">BPM</div>
<div class="zone-badge">{{ zone }}</div></div>
<div class="meta">Zadnje mjerenje: {{ last_update }} | UDP -> {{ targets }} :{{ port }}</div>
<div class="graph"><div class="graph-title">HISTORIJA (zadnjih 30)</div><div class="bars">
{% for h in history %}<div class="bar" style="height:{{ h.pct }}%;background:{{ h.color }};"></div>{% endfor %}
</div></div></body></html>"""


def hr_to_zone(hr):
    if hr < 60:
        return "ODMOR", "#4af", "rgba(64,170,255,0.3)"
    if hr < 100:
        return "LAGANO", "#4f4", "rgba(64,255,64,0.3)"
    if hr < 140:
        return "UMJERENO", "#ff4", "rgba(255,255,64,0.3)"
    if hr < 170:
        return "INTENZIVNO", "#f84", "rgba(255,136,64,0.3)"
    return "MAKSIMALNO", "#f44", "rgba(255,64,64,0.3)"


def main():
    cfg = load_config()
    args = parse_args(cfg)
    bridge = Bridge(cfg, args)

    app = Flask(__name__)

    @app.route("/")
    def index():
        d = bridge.hr_data
        bpm = d["current"] or 0
        hist = d["history"][-30:]
        max_hr = max(max(hist, default=100), 80)
        history = [{"pct": max(4, int(h / max_hr * 100)), "color": hr_to_zone(h)[1]} for h in hist]
        zone, color, glow = hr_to_zone(bpm)
        return render_template_string(
            HTML, bpm=bpm if bpm else "-", zone=zone, color=color, glow=glow,
            history=history, last_update=d["last_update"],
            targets=", ".join(bridge.targets), port=bridge.unity_port)

    @app.route("/api/hr")
    def api_hr():
        return jsonify({"hr": bridge.hr_data["current"], "ts": bridge.hr_data["last_update"],
                        "sequence": bridge.sequence})

    threading.Thread(target=bridge.logcat_loop, daemon=True).start()
    threading.Thread(target=bridge.keepalive_loop, daemon=True).start()

    print(f"\n[UDP] Targets: {bridge.targets} port {bridge.unity_port}")
    print(f"Dashboard: http://127.0.0.1:{bridge.dashboard_port}\n")
    app.run(host="0.0.0.0", port=bridge.dashboard_port, debug=False)


if __name__ == "__main__":
    main()

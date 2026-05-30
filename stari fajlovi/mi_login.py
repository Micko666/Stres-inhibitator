"""
Xiaomi Mi Cloud login sa email verifikacijom.
Izvlaci auth key za sat iz cloud API-ja.
"""

import requests
import hashlib
import json
import sys
import re

EMAIL     = "djurovicmihailo666@gmail.com"
PASSWORD  = "satotkucavasrce@"
DEVICE_DID = "791141009"

session = requests.Session()
session.headers.update({
    "User-Agent": "XiaoMi/MiuiBrowser/12.1.2-g",
    "Content-Type": "application/x-www-form-urlencoded",
})

def md5up(s):
    return hashlib.md5(s.encode()).hexdigest().upper()

def login():
    # Korak 1: sign
    r = session.get(
        "https://account.xiaomi.com/pass/serviceLogin",
        params={"sid": "xiaomiio", "_json": "true"},
        allow_redirects=False, timeout=10
    )
    data = json.loads(r.text.lstrip("&&&START&&&"))
    sign = data["_sign"]

    # Korak 2: auth
    r2 = session.post(
        "https://account.xiaomi.com/pass/serviceLoginAuth2",
        data={"sid": "xiaomiio", "_sign": sign, "_json": "true",
              "user": EMAIL, "hash": md5up(PASSWORD)},
        allow_redirects=False, timeout=10
    )
    auth = json.loads(r2.text.lstrip("&&&START&&&"))
    return auth

def verify_email(notification_url):
    """Otvori verifikacijski URL i pokusaj poslati email kod."""
    print("Otvaranje verifikacije...")

    # Posjeti notificationUrl da inicijaliziramo verifikaciju
    r = session.get(notification_url, allow_redirects=True, timeout=10)

    # Pokusaj naci email verifikacijski endpoint
    # Xiaomi salje email automatski kad se posjeti authStart
    print("Xiaomi je trebao poslati verifikacijski kod na:", EMAIL)
    code = input("\nUnesi kod iz emaila (6 cifara): ").strip()

    # Pokusaj razlicite endpoint forme za verifikaciju
    endpoints = [
        "https://account.xiaomi.com/pass/bindConfirm",
        "https://account.xiaomi.com/pass/emailVerify",
        "https://account.xiaomi.com/fe/service/identity/verify",
    ]

    # Izvuci context iz URL-a
    context = re.search(r"context=([^&]+)", notification_url)
    context_val = context.group(1) if context else ""

    for ep in endpoints:
        try:
            resp = session.post(ep,
                data={"code": code, "context": context_val, "_json": "true"},
                timeout=10)
            print(f"  {ep}: {resp.status_code} — {resp.text[:100]}")
            if resp.status_code == 200:
                break
        except Exception as e:
            print(f"  {ep}: {e}")

    return code

def get_devices(user_id, service_token):
    r = session.get(
        "https://api.io.mi.com/app/home/device_list",
        params={"getVirtualModel": "false", "getHuamiDevices": "1"},
        headers={
            "x-xiaomi-protocal-flag-cli": "PROTOCAL-HTTP2",
            "Cookie": f"userId={user_id}; serviceToken={service_token}; "
                      f"yetAnotherServiceToken={service_token}",
        },
        timeout=10
    )
    return r.json()

# ---- MAIN ----
print("=== Mi Cloud Auth Key Extractor ===\n")
print("Logovanje...")

auth = login()
code = auth.get("code", -1)
user_id = str(auth.get("userId", ""))
location = auth.get("location", "")
notification_url = auth.get("notificationUrl", "")

print(f"Login code: {code} | userId: {user_id or 'prazan'} | location: {'ok' if location else 'nema'}")

if location:
    # Direktan login, nema verifikacije
    r3 = session.get(location, allow_redirects=False, timeout=10)
    service_token = session.cookies.get("serviceToken", "")
else:
    # Treba verifikacija
    if not notification_url:
        print("Nema notificationUrl. Login nije uspio.")
        sys.exit(1)

    print(f"\nSecurityStatus {auth.get('securityStatus')} — treba verifikacija.")
    verify_email(notification_url)

    # Pokusaj login ponovo
    print("\nPonavlja login...")
    auth2 = login()
    user_id = str(auth2.get("userId", ""))
    location2 = auth2.get("location", "")
    if not location2:
        print("Login ponovo nije prosao. Treba rucna verifikacija.")
        print("notificationUrl:", auth2.get("notificationUrl", "")[:120])
        sys.exit(1)

    r3 = session.get(location2, allow_redirects=False, timeout=10)
    service_token = session.cookies.get("serviceToken", "")

print(f"userId: {user_id}")
print(f"serviceToken: {'ok (' + service_token[:12] + '...)' if service_token else 'NEMA'}\n")

if not service_token:
    print("Nije dobijen serviceToken.")
    sys.exit(1)

print("Trazim uredaje...")
data = get_devices(user_id, service_token)
devices = data.get("result", {}).get("list", [])
print(f"Pronadjeno {len(devices)} uredjaja:\n")

found_key = None
for d in devices:
    did   = d.get("did", "")
    name  = d.get("name", "?").encode("ascii", "replace").decode()
    token = d.get("token", "")
    model = d.get("model", "")
    print(f"  did={did}  model={model}  name={name}")
    print(f"  token={token}\n")
    if did == DEVICE_DID:
        found_key = token
        print(f">>> TVOJ SAT AUTH KEY: {token}\n")

if found_key:
    with open("auth_key.txt", "w") as f:
        f.write(found_key)
    print("Kljuc sacuvan u auth_key.txt")
    print("\nSljedeci korak:")
    print(f"  python ble_hr_auth.py {found_key}")
else:
    print(f"Sat (DID={DEVICE_DID}) nije nadjen u listi. Provjeri gore sve uredjaje.")

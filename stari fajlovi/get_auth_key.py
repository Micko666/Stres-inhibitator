"""
Izvlaci Xiaomi wearable auth key iz Mi Cloud.
Loguje se na Xiaomi nalog i dohvata security key za spareni uredaj.

Koriscenje:
  python get_auth_key.py
"""

import requests
import hashlib
import json
import sys
import re
from getpass import getpass

DEVICE_DID = "791141009"  # tvoj sat

USER_AGENT = "Android-7.1.1-1.0.0-ONEPLUS A3010-136-" + "".join(
    ["%02X" % b for b in hashlib.md5(b"xiaomi_auth").digest()]
)

session = requests.Session()
session.headers.update({
    "User-Agent": USER_AGENT,
    "Content-Type": "application/x-www-form-urlencoded",
})


def sign_password(password):
    return hashlib.md5(password.encode()).hexdigest().upper()


def service_login(email, password):
    """Korak 1: dohvati sign token."""
    r = session.get(
        "https://account.xiaomi.com/pass/serviceLogin",
        params={"sid": "xiaomiio", "_json": "true"},
        allow_redirects=False,
        timeout=10,
    )
    text = r.text.lstrip("&&&START&&&")
    data = json.loads(text)
    sign = data.get("_sign", "")

    """Korak 2: authenticate."""
    r2 = session.post(
        "https://account.xiaomi.com/pass/serviceLoginAuth2",
        data={
            "sid": "xiaomiio",
            "_sign": sign,
            "_json": "true",
            "user": email,
            "hash": sign_password(password),
        },
        allow_redirects=False,
        timeout=10,
    )
    text2 = r2.text.lstrip("&&&START&&&")
    auth = json.loads(text2)

    code = auth.get("code", -1)
    if code != 0:
        desc = auth.get("desc", "")
        print(f"Login greska (code={code}): {desc}")
        if code == 70016:
            print("Pogresna lozinka.")
        elif code == 87001:
            print("Potrebna je verifikacija — provjeri email/SMS za 2FA kod.")
        return None, None

    ssecurity = auth.get("ssecurity", "")
    user_id   = auth.get("userId", "")
    location  = auth.get("location", "")

    """Korak 3: dohvati service token."""
    r3 = session.get(location, allow_redirects=False, timeout=10)
    token = session.cookies.get("serviceToken", "")

    print(f"Login uspjesan! userId={user_id}")
    return user_id, token, ssecurity


def get_device_key(user_id, service_token, ssecurity):
    """Dohvati listu uredaja i security key."""
    headers = {
        "x-xiaomi-protocal-flag-cli": "PROTOCAL-HTTP2",
        "mishop-did": "NA",
    }

    # Endpoint za fitness/wearable uredaje
    for base in [
        "https://api.mifit.xiaomi.com",
        "https://api-mifit.huami.com",
    ]:
        try:
            r = session.get(
                f"{base}/v1/device/profile",
                params={"userId": user_id, "deviceId": DEVICE_DID},
                headers=headers,
                timeout=10,
            )
            if r.status_code == 200:
                data = r.json()
                print(f"Profile response: {json.dumps(data, indent=2)[:500]}")
        except Exception as e:
            print(f"  {base}: {e}")

    # Standardni Mi Cloud device listing
    try:
        r = session.get(
            "https://api.io.mi.com/app/home/device_list",
            params={"getVirtualModel": "false", "getHuamiDevices": "1"},
            headers={
                "x-xiaomi-protocal-flag-cli": "PROTOCAL-HTTP2",
                "Cookie": f"userId={user_id}; serviceToken={service_token}; "
                           f"yetAnotherServiceToken={service_token}",
            },
            timeout=10,
        )
        data = r.json()
        devices = data.get("result", {}).get("list", [])
        print(f"\nNadeno {len(devices)} uredaja:")
        for d in devices:
            did   = d.get("did", "")
            name  = d.get("name", "")
            token = d.get("token", "")
            print(f"  did={did}  name={name}  token={token[:8]}...")
            if did == DEVICE_DID:
                print(f"\n>>> TVOJ SAT: token={token}")
                return token
    except Exception as e:
        print(f"device_list greska: {e}")

    return None


def try_mifit_login(email, password):
    """Direktan Mi Fit / Amazfit API login koji vraca wearable kljuc."""
    try:
        r = requests.post(
            "https://user-fit.huami.com/registrations/social",
            data={
                "token_type": "access_mi_login",
                "app_name": "com.xiaomi.hm.health",
                "app_version": "6.3.0",
                "source": "com.xiaomi.hm.health",
                "country_code": "EU",
                "device_id": "d92cf86c-2f4d-4c8c-8f48-7f24c6e6ade0",
                "platform": "android",
                "third_name": "mi-watch",
            },
            timeout=10,
        )
        print(f"Huami login: {r.status_code} — {r.text[:200]}")
    except Exception as e:
        print(f"Huami greska: {e}")


if __name__ == "__main__":
    print("=== Xiaomi Watch Auth Key Extractor ===")
    print(f"Device DID: {DEVICE_DID}\n")

    email = input("Mi nalog email: ").strip()
    password = getpass("Lozinka: ")

    print("\nLogovanje na Mi Cloud...")
    result = service_login(email, password)
    if result and result[0]:
        user_id, token, ssecurity = result
        print("\nTrazim auth key...")
        key = get_device_key(user_id, token, ssecurity)
        if key:
            print(f"\n=== AUTH KEY ===\n{key}\n")
            with open("auth_key.txt", "w") as f:
                f.write(key)
            print("Sacuvan u auth_key.txt")
        else:
            print("\nAuth key nije nadjen direktno. Provjeri output iznad za 'token' vrijednosti.")
    else:
        print("Login nije uspio.")

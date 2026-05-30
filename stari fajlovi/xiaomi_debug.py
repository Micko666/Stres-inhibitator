import requests
import hashlib
import json
import re

USERNAME = "mickojekralj4@gmail.com"
PASSWORD = "satotkucavasrce@"

HEADERS = {
    "User-Agent": "APP/com.xiaomi.mihome APPV/6.0.103 iosPassportSDK/3.9.0 iOS/14.4 miHSTS",
    "Content-Type": "application/x-www-form-urlencoded",
}

def parse_json(text):
    text = re.sub(r"^&&&START&&&", "", text.strip())
    return json.loads(text)

session = requests.Session()
session.headers.update(HEADERS)

r = session.get(
    "https://account.xiaomi.com/pass/serviceLogin",
    params={"sid": "xiaomiio", "_json": "true"},
    timeout=15
)
data = parse_json(r.text)
sign = data["_sign"]

pwd_hash = hashlib.md5(PASSWORD.encode()).hexdigest().upper()
r2 = session.post(
    "https://account.xiaomi.com/pass/serviceLoginAuth2",
    data={
        "sid": "xiaomiio",
        "_sign": sign,
        "qs": "%3Fsid%3Dxiaomiio%26_json%3Dtrue",
        "callback": "https://sts.api.io.mi.com/sts",
        "_json": "true",
        "user": USERNAME,
        "hash": pwd_hash,
    },
    timeout=15
)

data2 = parse_json(r2.text)
print("=== LOGIN RESPONSE ===")
print(json.dumps(data2, indent=2))

print("\n=== COOKIES ===")
for c in session.cookies:
    print(f"  {c.name} = {c.value[:40] if c.value else 'None'}")

location = data2.get("location", "")
if location:
    print(f"\n=== STS redirect: {location[:80]} ===")
    r3 = session.get(location, timeout=15, allow_redirects=True)
    print(f"STS status: {r3.status_code}")
    print(f"STS cookies:")
    for c in session.cookies:
        print(f"  {c.name} = {str(c.value)[:60]}")

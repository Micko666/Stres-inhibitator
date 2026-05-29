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

def login():
    session = requests.Session()
    session.headers.update(HEADERS)

    print("Korak 1: Dohvatam sign token...")
    r = session.get(
        "https://account.xiaomi.com/pass/serviceLogin",
        params={"sid": "xiaomiio", "_json": "true"},
        timeout=15
    )
    data = parse_json(r.text)
    sign = data.get("_sign")
    if not sign:
        print("Greska: nije dobiven sign token.")
        print(r.text[:500])
        return None

    print(f"Sign: {sign[:20]}...")

    pwd_hash = hashlib.md5(PASSWORD.encode()).hexdigest().upper()
    print("Korak 2: Loginujem se...")
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
    code = data2.get("code", -1)

    if code != 0:
        print(f"Login neuspio. Code: {code}, Poruka: {data2.get('desc', '')}")
        print(json.dumps(data2, indent=2)[:500])
        return None

    service_token = data2.get("ssecurity") or data2.get("serviceToken")
    user_id = data2.get("userId")
    print(f"Login OK! UserId: {user_id}")

    # Dohvati STS token
    location = data2.get("location")
    token = data2.get("serviceToken")
    if location:
        print(f"Korak 3: Dohvatam STS token od: {location[:60]}...")
        r3 = session.get(location, timeout=15)
        for cookie in session.cookies:
            if cookie.name == "serviceToken":
                token = cookie.value
                break

    return session, user_id, token, data2.get("ssecurity", "")

def get_devices(session, user_id, service_token, ssecurity):
    print("\nKorak 4: Dohvatam listu uredjaja...")
    import time, base64, hmac, hashlib

    nonce_bytes = b'\x00' * 8 + int(time.time() / 60).to_bytes(4, 'big')
    nonce = base64.b64encode(nonce_bytes).decode()

    signed_nonce = base64.b64encode(
        hashlib.sha256(base64.b64decode(ssecurity) + base64.b64decode(nonce)).digest()
    ).decode()

    r = session.get(
        "https://api.io.mi.com/api/v2/home/device_list",
        params={"data": '{"getVirtualModel":false,"getHuamiDevices":1}'},
        headers={
            "x-xiaomi-protocal-flag-cli": "PROTOCAL-HTTP2",
            "MIOT-ENCRYPT-ALGORITHM": "ENCRYPT-RC4",
            "content-encoding": "Undefined",
        },
        cookies={"userId": str(user_id), "serviceToken": service_token},
        timeout=15
    )
    print(f"Status: {r.status_code}")
    print(r.text[:2000])

def main():
    result = login()
    if not result:
        return
    session, user_id, service_token, ssecurity = result
    get_devices(session, user_id, service_token, ssecurity)

if __name__ == "__main__":
    main()

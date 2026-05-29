"""
Dekodira QR kod sa slike i pokusava izvuci auth key / MAC / pairing token.
"""

import sys
import re
from PIL import Image
from pyzbar import pyzbar

def decode(path):
    img = Image.open(path)
    results = pyzbar.decode(img)

    if not results:
        # Pokusaj povecati kontrast i pokusaj opet
        import PIL.ImageEnhance as IE
        img2 = IE.Contrast(img).enhance(3.0)
        img2 = IE.Sharpness(img2).enhance(2.0)
        results = pyzbar.decode(img2)

    if not results:
        print("QR kod nije prepoznat. Pokusaj rucno izrezati samo QR dio slike.")
        return

    for r in results:
        data = r.data.decode("utf-8", errors="replace")
        print(f"\n=== QR SADRZAJ ===")
        print(data)
        print(f"\nTip: {r.type}")
        print(f"Pozicija: {r.rect}")

        # Trazi MAC adresu
        mac = re.findall(r"([0-9A-Fa-f]{2}[:\-]){5}[0-9A-Fa-f]{2}", data)
        if mac:
            print(f"\n>>> MAC adresa pronadjena: {mac}")

        # Trazi hex stringove koji mogu biti auth key (16+ hex chars)
        keys = re.findall(r"[0-9a-fA-F]{16,}", data)
        if keys:
            print(f"\n>>> Potencijalni kljucevi/tokeni:")
            for k in keys:
                print(f"    {k}  ({len(k)} hex chars = {len(k)//2} bytes)")

        # Trazi URL parametre
        params = re.findall(r"[?&]([^=]+)=([^&\s]+)", data)
        if params:
            print(f"\n>>> URL parametri:")
            for k, v in params:
                print(f"    {k} = {v}")

path = sys.argv[1] if len(sys.argv) > 1 else r"C:\Users\Korisnik\Desktop\qr_watch.png"
print(f"Citam: {path}")
decode(path)

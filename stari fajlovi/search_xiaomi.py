import re

with open("C:/Users/Korisnik/Desktop/Diplomski/btsnoop2.log", 'rb') as f:
    data = f.read()

print(f"Velicina fajla: {len(data)} bajta\n")

# Traži Xiaomi service UUID 0xFE95 (little-endian: 95 FE)
pattern_fe95 = bytes([0x95, 0xFE])
positions = [i for i in range(len(data)) if data[i:i+2] == pattern_fe95]
print(f"Pronadjeno 'FE95' na {len(positions)} mjesta:")
for pos in positions[:20]:
    start = max(0, pos - 16)
    end = min(len(data), pos + 32)
    print(f"  Offset {pos}: ...{data[start:pos].hex(' ')} [95 fe] {data[pos+2:end].hex(' ')}")

# Traži auth pattern koji Xiaomi koristi (0x01 0x08 = auth init command)
print(f"\nTrazi auth init (01 00 08 ili 01 08):")
for seq in [b'\x01\x08', b'\x01\x00\x08']:
    positions2 = [i for i in range(len(data)) if data[i:i+len(seq)] == seq]
    print(f"  Pattern {seq.hex()}: {len(positions2)} mjesta")

# Traži Xiaomi auth service UUID 0009A000 (za Mi Band 7/8/9)
patterns_to_search = [
    (b'\x00\x09\xA0\x00', "auth char 0x9A00"),
    (b'\xA0\x09', "0x09A0 short"),
    (b'\x00\x02\x96\x57', "HR notification UUID"),
]
for pat, name in patterns_to_search:
    found = data.count(pat)
    print(f"\n  Pattern '{name}' ({pat.hex()}): {found} pojavljivanja")

print("\n=== Trazim 16-bajtne sekvence koje se pojavljuju samo jednom (key kandidati) ===")
# Skupi sve 16-bajtne blokove koji se pojavljuju samo jednom
from collections import Counter
blocks = Counter()
step = 1
for i in range(16, len(data) - 16, step):
    block = data[i:i+16]
    # Preskoci blokove koji su svi nule ili isti bajt
    if len(set(block)) > 6:
        blocks[block] += 1

unique = [(b, c) for b, c in blocks.items() if c == 1]
print(f"Ima {len(unique)} jedinstvenih 16-bajtnih blokova (prvih 20):")
for b, _ in unique[:20]:
    print(f"  {b.hex()}")

import struct

def parse_btsnoop(filename):
    with open(filename, 'rb') as f:
        magic = f.read(8)
        version = struct.unpack('>I', f.read(4))[0]
        datalink = struct.unpack('>I', f.read(4))[0]

        packets = []
        while True:
            header = f.read(24)
            if len(header) < 24:
                break
            orig_len, incl_len, flags, drops = struct.unpack('>IIII', header[:16])
            data = f.read(incl_len)
            if len(data) < incl_len:
                break
            packets.append((flags, data))
    return packets

packets = parse_btsnoop("C:/Users/Korisnik/Desktop/Diplomski/btsnoop2.log")
print(f"Paketa: {len(packets)}")

# Prvih 10 ACL paketa — raw hex
print("\n=== Prvih 10 ACL paketa (raw) ===")
count = 0
for i, (flags, data) in enumerate(packets):
    if len(data) < 2 or data[0] != 0x02:
        continue
    direction = "OUT" if flags == 0 else "IN"
    print(f"\nPaket {i} | {direction} | len={len(data)}")
    print(f"  Hex: {data[:32].hex(' ')}")

    # Decode ACL header
    if len(data) >= 5:
        handle_raw = struct.unpack('<H', data[1:3])[0]
        handle = handle_raw & 0x0FFF
        pb = (handle_raw >> 12) & 0x3
        bc = (handle_raw >> 14) & 0x3
        total_len = struct.unpack('<H', data[3:5])[0]
        print(f"  ACL: handle=0x{handle:03X} PB={pb} BC={bc} data_len={total_len}")

        if pb in [0x00, 0x02] and total_len >= 4 and len(data) >= 5 + 4:  # first fragment
            l2cap = data[5:]
            l2cap_len = struct.unpack('<H', l2cap[0:2])[0]
            cid = struct.unpack('<H', l2cap[2:4])[0]
            print(f"  L2CAP: len={l2cap_len} CID=0x{cid:04X}")
            if len(l2cap) > 4:
                print(f"  Payload: {l2cap[4:36].hex(' ')}")

    count += 1
    if count >= 10:
        break

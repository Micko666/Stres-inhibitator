import struct
from collections import Counter

def parse_btsnoop(filename):
    with open(filename, 'rb') as f:
        magic = f.read(8)
        if magic != b'btsnoop\x00':
            print("Nije validan btsnoop fajl!")
            return

        version = struct.unpack('>I', f.read(4))[0]
        datalink = struct.unpack('>I', f.read(4))[0]
        print(f"btsnoop verzija: {version}, datalink: {datalink}\n")

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

    print(f"Ukupno paketa: {len(packets)}\n")

    # Statistika HCI tipova
    hci_types = Counter()
    cids_found = Counter()
    for flags, data in packets:
        if data:
            hci_types[data[0]] += 1

    print("=== HCI tipovi paketa ===")
    for t, cnt in sorted(hci_types.items()):
        names = {0x01: "HCI Command", 0x02: "HCI ACL", 0x03: "HCI SCO", 0x04: "HCI Event"}
        print(f"  0x{t:02X} ({names.get(t, 'Unknown')}): {cnt} paketa")

    print("\n=== L2CAP CID-ovi u ACL paketima ===")
    for flags, data in packets:
        if len(data) < 5 or data[0] != 0x02:
            continue
        total_len = struct.unpack('<H', data[3:5])[0]
        if len(data) < 5 + total_len or total_len < 4:
            continue
        acl = data[5:5 + total_len]
        cid = struct.unpack('<H', acl[2:4])[0]
        cids_found[cid] += 1

    cid_names = {
        0x0001: "L2CAP Signaling", 0x0002: "Connectionless",
        0x0004: "ATT", 0x0005: "LE Signaling", 0x0006: "SMP"
    }
    for cid, cnt in sorted(cids_found.items()):
        print(f"  CID 0x{cid:04X} ({cid_names.get(cid, 'Unknown')}): {cnt} paketa")

    print("\n=== ATT operacije (sve) ===")
    att_opcodes = {
        0x01: "Error", 0x02: "ExchangeMTU Req", 0x03: "ExchangeMTU Rsp",
        0x04: "FindInfo Req", 0x05: "FindInfo Rsp", 0x08: "ReadByType Req",
        0x09: "ReadByType Rsp", 0x0A: "Read Req", 0x0B: "Read Rsp",
        0x10: "ReadByGroup Req", 0x11: "ReadByGroup Rsp",
        0x12: "Write Req", 0x13: "Write Rsp", 0x52: "Write Cmd",
        0x1B: "Notification", 0x1D: "Indication"
    }
    for i, (flags, data) in enumerate(packets):
        if len(data) < 5 or data[0] != 0x02:
            continue
        total_len = struct.unpack('<H', data[3:5])[0]
        if len(data) < 5 + total_len or total_len < 5:
            continue
        acl = data[5:5 + total_len]
        cid = struct.unpack('<H', acl[2:4])[0]
        if cid != 0x0004:
            continue
        att = acl[4:]
        if not att:
            continue
        opcode = att[0]
        name = att_opcodes.get(opcode, f"0x{opcode:02X}")
        direction = "Phone→Band" if flags == 0 else "Band→Phone"
        handle_str = ""
        value_str = ""
        if len(att) >= 3:
            handle_str = f"| Handle 0x{struct.unpack('<H', att[1:3])[0]:04X}"
        if len(att) > 3:
            value = att[3:]
            value_str = f"| {value.hex()} (len={len(value)})"
        print(f"  [{i:04d}] {direction} | {name} {handle_str} {value_str}")

    print("\n=== Trazim 16-bajtne sekvence (potencijalni auth key) ===")
    for i, (flags, data) in enumerate(packets):
        if len(data) < 5 or data[0] != 0x02:
            continue
        total_len = struct.unpack('<H', data[3:5])[0]
        if len(data) < 5 + total_len or total_len < 5:
            continue
        acl = data[5:5 + total_len]
        cid = struct.unpack('<H', acl[2:4])[0]
        if cid != 0x0004:
            continue
        att = acl[4:]
        if len(att) < 3:
            continue
        opcode = att[0]
        if opcode in [0x12, 0x52]:
            value = att[3:]
            if len(value) == 16:
                direction = "Phone→Band" if flags == 0 else "Band→Phone"
                handle = struct.unpack('<H', att[1:3])[0]
                print(f"*** KEY KANDIDAT | Paket {i:04d} | {direction} | Handle 0x{handle:04X} | {value.hex()}")

if __name__ == "__main__":
    parse_btsnoop("C:/Users/Korisnik/Desktop/Diplomski/btsnoop2.log")

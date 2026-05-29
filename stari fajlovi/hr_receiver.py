import socket
import json

UDP_IP   = "0.0.0.0"
UDP_PORT = 5005

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.bind((UDP_IP, UDP_PORT))

print(f"HR Receiver slusam na UDP port {UDP_PORT}...")
print("Cekam podatke o pulsu...\n")

while True:
    data, addr = sock.recvfrom(1024)
    msg = json.loads(data.decode())
    hr = msg["hr"]
    ts = msg["ts"]
    bar = "#" * (hr // 5)
    print(f"[{ts}]  Puls: {hr:3d} bpm  {bar}")

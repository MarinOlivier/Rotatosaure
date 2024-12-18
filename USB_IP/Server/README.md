# USB/IP Server

Based on Linux

## Installation 

```bash
sudo apt install -y usbip

sudo tee /etc/modules-load.d/usbip.conf <<EOF
usbip
usbip-core
usbip_common_mod
usbip-host
EOF

sudo modprobe usbip usbip-core usbip_common_mod usbip-host
```

## Services

```bash
sudo tee /etc/systemd/system/usbip-server.service <<EOF
[Unit]
Description=USBIP Server
After=network.target

[Service]
ExecStart=/usr/sbin/usbipd -D --pid /run/usbipd.pid
PIDFile=/run/usbipd.pid
Restart=always
User=root

[Install]
WantedBy=multi-user.target
EOF

sudo tee /etc/systemd/system/usbip-manager.service <<EOF
[Unit]
Description=USBIP Manager
After=usbip-server.service

[Service]
Type=oneshot
ExecStart=/usr/local/bin/usbip-manager.sh check
User=root
EOF

sudo tee /etc/systemd/system/usbip-manager.timer <<EOF
[Unit]
Description=Run USBIP Manager every 10 seconds

[Timer]
OnBootSec=10sec
OnUnitActiveSec=10sec
Unit=usbip-manager.service

[Install]
WantedBy=timers.target
EOF
```

Copy usbip-manager to /usr/local/bin/usbip-manager.sh

```bash
systemctl enable usbip-server.service
systemctl start usbip-server.service
systemctl enable usbip-manager.timer
systemctl start usbip-manager.timer
```
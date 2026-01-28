# Netcheck

Netcheck is a small WPF app to check Internet access, public IP, ping, and optional email notifications.
<img width="529" height="388" alt="Netcheck" src="https://github.com/user-attachments/assets/f8756850-b745-4ea0-976a-b839e29e524b" />

## Features
- Internet connectivity check
- Public IP lookup
- Ping to 1.1.1.1 with status colors
- Geolocation (country/city)
- Optional email notifications when IP changes

## Install
Use the Windows setup from GitHub Releases:
```
https://github.com/Anthanaab/Netcheck/releases
```

## Usage
1) Launch the app
2) Click "Verifier" to run a check
3) Enable "Auto" to run checks on a timer

## Email notifications
Open the "Notifications email" section and fill in:
- SMTP host, port, SSL
- Login + password
- From / To

Click "Enregistrer" to save settings. The app will notify when the public IP changes.

## License
MIT. See LICENSE.

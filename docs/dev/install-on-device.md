# Install the debug app on an emulator or an Android phone

A debug build talks to the local Development API over plain http (`app/android/app/src/debug/AndroidManifest.xml`
allows cleartext in debug builds only) and signs in with the server's fake identity provider (`VELA_FAKE_SSO=true`),
so no Google account, Play Integrity or email delivery is needed. Release builds refuse an `http://` API.

All commands run from the repository root. Toolchains: `source scripts/dev-env.sh && source scripts/android-env.sh`
in every shell (or prefix a single command with `scripts/with-env.sh`).

## 1. Start the local API and seed demo data

```bash
scripts/run-local-api.sh        # Postgres + Azurite in Docker, API on 0.0.0.0:5080 (attestation off), idempotent
python3 tools/seed_local.py     # 2 artists (maren, theo), 5 published stories, replies, a follow (needs Pillow)
curl -fs http://127.0.0.1:5080/health && echo API up
```

Details and settings: [run-local.md](run-local.md). Stop everything with `scripts/stop-local-api.sh` (discards the data;
`--keep` keeps the containers).

## 2a. Android emulator

```bash
emulator -avd vela_test -no-window -no-audio -no-boot-anim -gpu swiftshader_indirect -no-snapshot -memory 3072 &
adb wait-for-device
until [ "$(adb shell getprop sys.boot_completed | tr -d '\r')" = 1 ]; do sleep 3; done

cd app
flutter build apk --debug --dart-define=API_BASE_URL=http://10.0.2.2:5080 --dart-define=VELA_FAKE_SSO=true
adb -s emulator-5554 install -r build/app/outputs/flutter-apk/app-debug.apk
adb -s emulator-5554 reverse tcp:10000 tcp:10000      # photo and upload URLs point at 127.0.0.1:10000 (Azurite)
adb -s emulator-5554 shell monkey -p dev.vela.app -c android.intent.category.LAUNCHER 1
```

`10.0.2.2` is the emulator's alias for the host. Drop `-no-window` to see the screen. `adb reverse` is lost when the
emulator restarts; run it again.

## 2b. USB-connected Android phone

Enable Developer options and USB debugging on the phone, connect it and accept the RSA prompt. `adb devices` must list it
as `device`; use its serial with `-s` (or `export ANDROID_SERIAL=<serial>`) when the emulator also runs.

### Option 1: adb reverse (recommended, no network set-up)

The phone's own `127.0.0.1:5080` and `127.0.0.1:10000` are tunnelled over USB to the host.

```bash
cd app
flutter build apk --debug --dart-define=API_BASE_URL=http://127.0.0.1:5080 --dart-define=VELA_FAKE_SSO=true
adb -s <serial> install -r build/app/outputs/flutter-apk/app-debug.apk
adb -s <serial> reverse tcp:5080 tcp:5080        # the API
adb -s <serial> reverse tcp:10000 tcp:10000      # Azurite (photos, uploads)
adb -s <serial> shell monkey -p dev.vela.app -c android.intent.category.LAUNCHER 1
```

The tunnels end when the cable is unplugged or adb restarts; run the two `adb reverse` lines again.

### Option 2: LAN address (phone on the same Wi-Fi, no cable after installing)

Start the API so that photo and upload URLs carry the host's LAN address instead of 127.0.0.1, and point the app at it:

```bash
HOST_IP=192.168.1.20            # the host's address on the Wi-Fi (ip -4 addr / ipconfig)
scripts/stop-local-api.sh --keep   # if it already runs with 127.0.0.1 URLs
VELA_PUBLIC_HOST=$HOST_IP scripts/run-local-api.sh
python3 tools/seed_local.py
cd app
flutter build apk --debug --dart-define=API_BASE_URL=http://$HOST_IP:5080 --dart-define=VELA_FAKE_SSO=true
adb -s <serial> install -r build/app/outputs/flutter-apk/app-debug.apk
```

The host firewall must allow inbound TCP 5080 and 10000. Under WSL2 the Linux address is not reachable from the LAN: use
option 1, or forward the ports from Windows (`netsh interface portproxy add v4tov4 listenport=5080 connectport=5080
connectaddress=<WSL address>`, same for 10000) and use the Windows LAN address. Photo URLs are built per response from
`Storage:PublicBaseUrl`, so data seeded before the switch shows too.

## 3. Using the app

- Sign in: any "Follow" pill or the Profile tab opens the sign-in sheet; "Continue with Google" signs in with the fake
  provider as a new account per install (clear the app's data for another one; `--dart-define=VELA_FAKE_SSO_SUBJECT=<name>`
  pins one identity). Onboarding then asks for a name and handle.
- Email sign-in: the code is printed in `.local/api.log` (`[dev email] sign-in code: 123456`).
- Demo content: the seeded artists `maren` and `theo` and their stories show in Discover.

## 4. Uninstall and clean up

```bash
adb -s <serial> uninstall dev.vela.app
adb -s <serial> reverse --remove-all
scripts/stop-local-api.sh
adb -s emulator-5554 emu kill      # the emulator
```

Note: `flutter test integration_test/...` (the e2e suite, [run-e2e.md](run-e2e.md)) uninstalls the app when it finishes;
install the APK again afterwards.

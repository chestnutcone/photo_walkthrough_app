# Run the end-to-end tests on an Android emulator

```bash
scripts/run-e2e.sh                       # everything: API, helper, emulator, seed, all integration tests
scripts/run-e2e.sh app/integration_test/a_guest_test.dart   # one file
scripts/e2e/run-test.sh app/integration_test/a_guest_test.dart   # one file, API/emulator already up
```

`run-e2e.sh` starts the local API ([run-local.md](run-local.md)), the host helper (`tools/e2e_host_helper.py`, port 5090),
boots the headless emulator if `emulator-5554` is missing
(`emulator -avd vela_test -no-window -no-audio -gpu swiftshader_indirect -no-snapshot`; needs KVM), seeds data and runs
every `app/integration_test/*_test.dart` with `flutter test -d emulator-5554`. Logs: `.local/e2e-logs/`. Accessibility
guideline failures are collected into `docs/review/a11y-violations.txt`.

Screenshots: tests print `E2E_SHOT <flow>/<name>`; `scripts/e2e/run-test.sh` grabs the frame with `adb screencap`
(`scripts/e2e/shot.py`, downscaled to 540 px) into `docs/review/screenshots/<flow>/<name>.png`.

Test layout (`app/integration_test/`): `support/e2e.dart` (harness: fake photo picker, injected session, plain-HTTP
second accounts, host helper client), `a_guest`, `b_signin_onboarding` (B and C), `d_social` (D, H, J, K),
`e_create`, `f_profile_search_settings` (F, G, I, session restart), `g_a11y`.

Manual checks used in the report: `scripts/e2e/adb-link.sh <uri> <out.png>` (deep links),
`scripts/e2e/airplane.sh on|off`, `adb shell settings put system font_scale 2.0`,
`adb shell wm size 320x568; adb shell wm density 160` (reset with `wm size reset; wm density reset`),
`adb shell settings put system user_rotation 1` (with `accelerometer_rotation 0`), `adb shell uiautomator dump`.

Notes and sandbox limits:
- No system `unzip`: Flutter's Android tooling needs one on first run; a python shim in `~/.local/shim/unzip` is picked
  up by `scripts/with-env.sh`. A normal machine does not need it.
- `flutter test` uninstalls the app afterwards. Install a normal build with
  `flutter build apk --debug --dart-define=API_BASE_URL=http://10.0.2.2:5080 --dart-define=VELA_FAKE_SSO=true`
  then `adb install -r app/build/app/outputs/flutter-apk/app-debug.apk`.
- The headless swiftshader emulator occasionally dies under load; restart it and re-run. `adb reverse` must be
  re-applied after a restart (the scripts do it).
- Real Google/Apple sign-in, Play Integrity, TalkBack speech and verified app links cannot run here; see the report.

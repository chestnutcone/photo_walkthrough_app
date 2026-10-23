# Vela app

Flutter client. Run `source ../scripts/dev-env.sh` first, then `flutter test` / `flutter analyze`.

## Build-time configuration (`--dart-define`)

| Define | Purpose |
|---|---|
| `API_BASE_URL` | API origin. Required in release builds. |
| `GOOGLE_CLIENT_ID` | iOS OAuth client id for Google sign-in (owner-provided). |
| `GOOGLE_SERVER_CLIENT_ID` | Web OAuth client id; the token audience on Android (owner-provided). |
| `VELA_FAKE_SSO=true` | Development only (ignored in release): skip the Apple and Google SDKs and sign in through the API's fake identity provider. |

Both Google client ids must also be listed in the API setting `Auth:Google:ClientIds`;
the Apple bundle id must be the API's `Auth:Apple:ClientId`.

## Sign-in notes

- Each attempt creates a fresh random nonce and sends the raw value to the API.
  Google receives it unchanged; Apple receives its lowercase hex SHA-256, which is
  what the API checks.
- Google: `google_sign_in` 7.2.0 only accepts a nonce in `initialize()`, which its docs say to call once. The app calls it per attempt so each attempt has a fresh nonce; the Android and iOS plugin code just stores it. Device-test two consecutive Google sign-ins on both platforms and re-check on any version bump (the version is pinned).
- Sign in with Apple needs the capability in Xcode; Google on iOS needs the reversed
  client id URL scheme (see the commented block in `ios/Runner/Info.plist`).
- Terms and privacy links open the API-hosted legal pages in the external browser
  (`url_launcher`; https only, or http to localhost in debug).

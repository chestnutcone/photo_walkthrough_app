/// Single source for user-facing names, bundle ids and domains.
/// "Vela" is a code name.
abstract final class Branding {
  static const String appName = 'Vela';
  static const String bundleId = 'dev.vela.app';
  static const String domain = 'vela.example';

  /// Custom URL scheme for "Open in app" from the web viewer
  /// (`vela://open/s/{workId}`). Registered in AndroidManifest.xml and Info.plist.
  static const String urlScheme = 'vela';
}

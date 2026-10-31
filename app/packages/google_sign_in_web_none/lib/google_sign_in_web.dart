import 'package:flutter_web_plugins/flutter_web_plugins.dart';

/// Registers nothing on purpose (see pubspec.yaml): no Google script is
/// loaded on web, and any GoogleSignIn call there throws from the platform
/// interface's placeholder.
class GoogleSignInNoWeb {
  GoogleSignInNoWeb._();

  static void registerWith(Registrar registrar) {}
}

import 'package:flutter_web_plugins/url_strategy.dart';

/// Makes the browser path (`/s/{workId}`) the router location. Without it
/// Flutter web reads the URL hash and every shared link opens as `/`.
/// [install] is a test seam; the default is a no-op outside the browser.
void configureWebUrlStrategy({void Function() install = usePathUrlStrategy}) =>
    install();

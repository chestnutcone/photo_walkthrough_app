import 'dart:io';

import 'package:flutter/services.dart';

/// Loads the app's bundled fonts (and Material icons / Roboto from the
/// Flutter SDK) so screenshots show real glyphs instead of the test font.
Future<void> loadGalleryFonts() async {
  Future<void> load(String family, List<String> paths) async {
    final loader = FontLoader(family);
    var any = false;
    for (final p in paths) {
      final f = File(p);
      if (!f.existsSync()) continue;
      any = true;
      loader.addFont(f.readAsBytes().then((b) => ByteData.sublistView(b)));
    }
    if (any) await loader.load();
  }

  await load('InstrumentSerif', ['assets/fonts/InstrumentSerif-Regular.ttf']);
  await load('Geist', ['assets/fonts/Geist-Variable.ttf']);
  await load('GeistMono', ['assets/fonts/GeistMono-Variable.ttf']);

  final fonts = _materialFontsDir();
  if (fonts != null) {
    await load('MaterialIcons', ['$fonts/MaterialIcons-Regular.otf']);
    await load('Roboto', [
      '$fonts/Roboto-Regular.ttf',
      '$fonts/Roboto-Medium.ttf',
      '$fonts/Roboto-Bold.ttf',
    ]);
  }
}

/// `<flutter>/bin/cache/artifacts/material_fonts`, found from FLUTTER_ROOT or
/// from the flutter_tester binary (`.../bin/cache/artifacts/engine/...`).
String? _materialFontsDir() {
  final root = Platform.environment['FLUTTER_ROOT'];
  if (root != null) {
    final d = '$root/bin/cache/artifacts/material_fonts';
    if (Directory(d).existsSync()) return d;
  }
  var dir = File(Platform.resolvedExecutable).parent;
  for (var i = 0; i < 6; i++) {
    final d = Directory('${dir.path}/material_fonts');
    if (d.existsSync()) return d.path;
    dir = dir.parent;
  }
  return null;
}

import 'package:flutter/foundation.dart';

import '../config.dart';
import 'app_attest_client.dart';
import 'attestation_client.dart';
import 'play_integrity_client.dart';

/// The attestation client for this device. Imported by `main.dart` only, so
/// the web entry point never pulls in the native plugin.
AttestationClient createPlatformAttestationClient() =>
    createAttestationClientFor(defaultTargetPlatform, isWeb: kIsWeb);

AttestationClient createAttestationClientFor(
  TargetPlatform platform, {
  required bool isWeb,
}) {
  if (isWeb) return const NoAttestationClient();
  return switch (platform) {
    TargetPlatform.android => PlayIntegrityAttestationClient(
      cloudProjectNumber: AppConfig.playCloudProjectNumber,
    ),
    TargetPlatform.iOS => AppAttestClient(enabled: AppConfig.appAttestEnabled),
    _ => const NoAttestationClient(),
  };
}

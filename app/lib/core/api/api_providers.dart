import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../attestation/attestation_client.dart';
import '../attestation/attestation_interceptor.dart';
import '../auth/auth_controller.dart';
import '../config.dart';
import 'auth_interceptor.dart';

export '../auth/auth_controller.dart'
    show clockProvider, sessionEpochProvider, sessionProvider;

/// Test seam for the HTTP transport. Null uses Dio's default.
final httpClientAdapterProvider = Provider<HttpClientAdapter?>((_) => null);

Dio _newDio(Ref ref) {
  final dio = Dio(
    BaseOptions(
      baseUrl: AppConfig.apiBaseUrl,
      connectTimeout: const Duration(seconds: 10),
      receiveTimeout: const Duration(seconds: 20),
      headers: {'X-Platform': AppConfig.platformHeader},
    ),
  );
  final adapter = ref.read(httpClientAdapterProvider);
  if (adapter != null) dio.httpClientAdapter = adapter;
  return dio;
}

/// Plain Dio for the token refresh call: no bearer interceptor, so it can
/// never recurse into itself.
final refreshDioProvider = Provider<Dio>(_newDio);

final dioProvider = Provider<Dio>((ref) {
  final dio = _newDio(ref);
  dio.interceptors.add(
    AttestationInterceptor(ref.watch(attestationClientProvider)),
  );
  dio.interceptors.add(
    AuthInterceptor(
      dio: dio,
      notifier: () => ref.read(sessionProvider.notifier),
    ),
  );
  return dio;
});

/// Generated client wired to the authenticated Dio.
final apiProvider = Provider<VelaApiClient>(
  (ref) => VelaApiClient(dio: ref.watch(dioProvider), interceptors: const []),
);

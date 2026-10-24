import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/api/auth_interceptor.dart';
import 'package:vela_app/features/web/web_viewer_app.dart';
import 'package:vela_app/features/web/web_viewer_config.dart';

void main() {
  test('store_urls_must_be_https', () {
    const c = WebViewerConfig(
      appStoreUrl: 'https://apps.apple.com/x',
      playStoreUrl: 'ftp://x',
    );
    expect(c.appStoreUri?.host, 'apps.apple.com');
    expect(c.playStoreUri, isNull);
    expect(const WebViewerConfig().appStoreUri, isNull);
  });

  test('deep_link_uses_the_branded_scheme', () {
    expect(
      const WebViewerConfig().deepLinkFor('w1').toString(),
      'vela://open/s/w1',
    );
  });

  test('open_in_app_prefers_https_on_phones_and_scheme_elsewhere', () {
    const c = WebViewerConfig(linksDomain: 'links.example');
    expect(
      c.openInAppUri('w1', mobile: true).toString(),
      'https://links.example/s/w1',
    );
    expect(c.openInAppUri('w1', mobile: false).toString(), 'vela://open/s/w1');
  });

  test('work_ids_are_encoded_in_the_deep_link', () {
    expect(
      const WebViewerConfig().deepLinkFor('a/b c').toString(),
      'vela://open/s/a%2Fb%20c',
    );
  });

  test(
    'web_overrides_use_no_session_storage_and_no_auth_interceptor',
    () async {
      final c = ProviderContainer(overrides: webViewerOverrides());
      addTearDown(c.dispose);
      expect(c.read(sessionStorageProvider), isA<NoSessionStorage>());
      expect(await c.read(sessionProvider.future), isNull);
      expect(
        c.read(dioProvider).interceptors.whereType<AuthInterceptor>(),
        isEmpty,
      );
    },
  );
}

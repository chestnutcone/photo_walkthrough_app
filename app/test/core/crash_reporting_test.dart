import 'package:flutter_test/flutter_test.dart';
import 'package:sentry_flutter/sentry_flutter.dart';
import 'package:vela_app/core/crash/crash_scrubber.dart';
import 'package:vela_app/core/crash_reporting.dart';

SentryEvent _dirtyEvent() => SentryEvent(
  message: SentryMessage('failed for jane.doe@example.com'),
  user: SentryUser(
    id: 'u1',
    email: 'jane.doe@example.com',
    ipAddress: '1.2.3.4',
    username: 'jane',
  ),
  request: SentryRequest(
    url: 'https://api.example.com/v1/search?q=jane%40example.com&sig=abc',
    queryString: 'q=secret+query&sig=abc',
    cookies: 'sid=abc',
    data: {'body': 'reply text'},
    headers: {'Authorization': 'Bearer abc.def.ghi', 'X-Attestation': 'tok'},
  ),
  exceptions: [
    SentryException(
      type: 'StateError',
      value: 'Bad state: Bearer eyJhbGciOi.eyJzdWIiOi.c2lnbmF0dXJl for jane@example.com',
    ),
  ],
  // ignore: deprecated_member_use
  extra: {'replyText': 'my private reply'},
  tags: {'screen': 'search?q=jane@example.com'},
  breadcrumbs: [
    Breadcrumb(
      message: 'typed hello',
      category: 'ui.input',
      type: 'user',
      data: {'text': 'my bio text'},
    ),
    Breadcrumb(
      message: 'tap Send',
      category: 'ui.click',
      type: 'user',
      data: {'label': 'Send reply'},
    ),
    Breadcrumb(
      category: 'navigation',
      type: 'navigation',
      data: {
        'from': '/search?q=jane@example.com',
        'to': '/story/w1?chapter=2&replies=1',
      },
    ),
    Breadcrumb(
      category: 'http',
      type: 'http',
      data: {
        'url':
            'https://blob.example.net/c/p.jpg?sv=2024&sig=SASSECRET%3D&se=2026',
        'method': 'GET',
        'status_code': 200,
        'request_body': 'x',
      },
    ),
  ],
);

void main() {
  test('crash_event_before_send_strips_email_and_text_inputs', () {
    final out = scrubEvent(_dirtyEvent())!;
    final dump = out.toJson().toString();

    for (final secret in [
      'jane.doe@example.com',
      'jane@example.com',
      'jane',
      'Bearer abc',
      'eyJhbGciOi',
      'SASSECRET',
      'secret+query',
      'my private reply',
      'my bio text',
      'Send reply',
      'reply text',
      'sig=abc',
      '1.2.3.4',
    ]) {
      expect(dump, isNot(contains(secret)), reason: secret);
    }
    expect(out.user, isNull);
    expect(out.request!.data, isNull);
    expect(out.request!.cookies, isNull);
    expect(out.request!.queryString, isNull);
    expect(out.request!.headers, isEmpty);
    expect(out.message!.formatted, contains('[email]'));
    expect(out.breadcrumbs!.map((b) => b.category), ['navigation', 'http']);
    final nav = out.breadcrumbs!.first.data!;
    expect(nav['from'], '/search');
    expect(nav['to'], '/story/w1');
    expect(out.breadcrumbs!.last.data!['method'], 'GET');
    expect(out.breadcrumbs!.last.data!['status_code'], 200);
    expect(out.breadcrumbs!.last.data!.containsKey('request_body'), isFalse);
  });

  test('scrubber_redacts_codes_jwts_and_keyed_secrets', () {
    expect(
      scrubText('code=123456 and otp: "654321"'),
      isNot(contains('123456')),
    );
    expect(scrubText('call 1234567890 now'), isNot(contains('1234567890')));
    expect(scrubText('eyJabc.def.ghi'), '[redacted]');
    expect(scrubText('access_token=zzz&x=1'), 'access_token=[redacted]&x=1');
    expect(scrubText('plain text 42'), 'plain text 42');
  });

  test('an_event_the_scrubber_cannot_process_is_dropped', () {
    final event = SentryEvent(message: SentryMessage('x'));
    expect(
      failClosed<SentryEvent>(event, (_) => throw StateError('boom')),
      isNull,
    );
    expect(failClosed<SentryEvent>(event, (e) => e), same(event));
  });

  test('breadcrumb_hook_drops_ui_text_and_strips_query_strings', () {
    expect(
      scrubBreadcrumb(Breadcrumb(category: 'ui.input', message: 'x')),
      isNull,
    );
    final nav = scrubBreadcrumb(
      Breadcrumb(
        category: 'navigation',
        data: {'to': '/story/w1?chapter=2&replies=1'},
      ),
    )!;
    expect(nav.data!['to'], '/story/w1');
    expect(scrubBreadcrumb(null), isNull);
  });

  test('transactions_are_scrubbed_too', () {
    final hub = Hub(
      SentryOptions(dsn: 'https://k@o0.ingest.sentry.io/1')
        ..tracesSampleRate = 1.0
        ..traceLifecycle = SentryTraceLifecycle.static,
    );
    // ignore: avoid_dynamic_calls
    final dynamic tracer = hub.startTransaction(
      '/search?q=jane@example.com',
      'navigation',
    );
    final child = tracer.startChild(
      'http.client',
      description: 'GET https://x/y?sig=SAS',
    );
    child.setData('q', 'jane@example.com');
    final tx = SentryTransaction(
      tracer,
      user: SentryUser(email: 'jane@example.com'),
    );

    final out = scrubTransaction(tx)!;

    expect(out.transaction, '/search');
    expect(out.user, isNull);
    expect(out.toJson().toString(), isNot(contains('jane@example.com')));
    expect(out.toJson().toString(), isNot(contains('SAS')));
  });

  test('crash_reporting_disabled_without_dsn', () async {
    var initCalls = 0;
    final started = await initCrashReporting(
      dsn: '',
      initializer: (configure) async => initCalls++,
    );
    expect(started, isFalse);
    expect(initCalls, 0);
    expect(
      await initCrashReporting(dsn: '   ', initializer: (_) async {}),
      isFalse,
    );
  });

  test('crash_reporting_options_are_privacy_safe', () async {
    final options = SentryFlutterOptions();
    SentryFlutterOptions? seen;
    final started = await initCrashReporting(
      dsn: 'https://k@o0.ingest.sentry.io/1',
      initializer: (configure) async {
        configure(options);
        seen = options;
      },
    );
    expect(started, isTrue);
    final o = seen!;
    expect(o.dsn, 'https://k@o0.ingest.sentry.io/1');
    expect(o.sendDefaultPii, isFalse);
    expect(o.attachScreenshot, isFalse);
    // ignore: experimental_member_use
    expect(o.attachViewHierarchy, isFalse);
    expect(o.enableUserInteractionBreadcrumbs, isFalse);
    expect(o.captureFailedRequests, isFalse);
    expect(o.traceLifecycle, SentryTraceLifecycle.static);
    expect(o.enablePrintBreadcrumbs, isFalse);
    expect(o.enableUserInteractionTracing, isFalse);
    expect(o.maxRequestBodySize, MaxRequestBodySize.never);
    expect(o.tracesSampleRate, lessThanOrEqualTo(0.1));
    expect(o.beforeSend, isNotNull);
    expect(o.beforeSendTransaction, isNotNull);
    expect(o.beforeBreadcrumb, isNotNull);
    expect(o.replay.sessionSampleRate, anyOf(isNull, 0));
    expect(o.replay.onErrorSampleRate, anyOf(isNull, 0));
    final scrubbed = await o.beforeSend!(_dirtyEvent(), Hint());
    expect(scrubbed!.user, isNull);
  });

  test('a_failing_sdk_init_never_stops_the_app', () async {
    final started = await initCrashReporting(
      dsn: 'https://k@o0.ingest.sentry.io/1',
      initializer: (_) async => throw StateError('native init failed'),
    );
    expect(started, isFalse);
  });

  group('fix round 1', () {
    SentryException ex(String value, {Mechanism? mechanism}) => SentryException(
      type: 'HttpException',
      value: value,
      mechanism: mechanism,
    );

    test('query_strings_in_exception_values_and_messages_are_stripped', () {
      final out = scrubEvent(
        SentryEvent(
          message: SentryMessage(
            'open yourapp://story/w1?replies=1&chapter=2 failed',
          ),
          exceptions: [
            ex(
              'Connection closed, uri = https://api.example.com/v1/search?q=bob smith',
            ),
            ex(
              'GET https://api.example.com/v1/search?q=bob%20smith#frag-secret failed',
            ),
          ],
        ),
      )!;
      final dump = out.toJson().toString();
      for (final leaked in [
        'bob',
        'smith',
        'replies=1',
        'chapter=2',
        'frag-secret',
      ]) {
        expect(dump, isNot(contains(leaked)), reason: leaked);
      }
      expect(dump, contains('https://api.example.com/v1/search'));
      expect(dump, contains('yourapp://story/w1'));
    });

    test('prose_question_marks_survive', () {
      expect(scrubText('What happened? Nothing.'), 'What happened? Nothing.');
    });

    test('error_event_transaction_mechanism_data_and_type_are_scrubbed', () {
      final out = scrubEvent(
        SentryEvent(
          transaction: '/search?q=jane@example.com',
          exceptions: [
            SentryException(
              type: 'Err jane@example.com',
              value: 'x',
              mechanism: Mechanism(
                type: 'generic',
                data: {'note': 'my private bio'},
              ),
            ),
          ],
        ),
      )!;
      final dump = out.toJson().toString();
      expect(out.transaction, '/search');
      expect(dump, isNot(contains('my private bio')));
      expect(dump, isNot(contains('jane@example.com')));
    });

    test('format_exception_source_excerpts_are_redacted', () {
      final out = scrubEvent(
        SentryEvent(
          exceptions: [
            ex(
              'Unexpected character (at character 3)\n{"bio":"my private bio"}\n   ^',
            ),
            ex('x' * 500),
          ],
        ),
      )!;
      expect(out.toJson().toString(), isNot(contains('my private bio')));
      expect(
        out.exceptions!.first.value,
        'Unexpected character (at character 3)',
      );
      expect(out.exceptions!.last.value!.length, lessThanOrEqualTo(200));
    });
  });
}

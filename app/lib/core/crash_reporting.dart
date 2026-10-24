import 'dart:async';

import 'package:sentry_flutter/sentry_flutter.dart';

import 'config.dart';
import 'crash/crash_scrubber.dart';

/// Low on purpose: performance traces are a convenience, not a feature.
const _tracesSampleRate = 0.05;

typedef SentryInitializer = Future<void> Function(
  FutureOr<void> Function(SentryFlutterOptions) configure,
);

Future<void> _defaultInit(
  FutureOr<void> Function(SentryFlutterOptions) configure,
) => SentryFlutter.init(configure);

/// Starts Sentry (mobile entry point only). Returns false, and does nothing,
/// when [dsn] (the `SENTRY_DSN` dart-define) is empty. The SDK installs the
/// Flutter and platform error hooks itself.
///
/// Reports carry no user, email, request data, input text, screenshots or
/// query strings; see `crash/crash_scrubber.dart`.
Future<bool> initCrashReporting({
  String dsn = AppConfig.sentryDsn,
  SentryInitializer initializer = _defaultInit,
}) async {
  final trimmed = dsn.trim();
  if (trimmed.isEmpty) return false;
  try {
    await initializer((options) => configureSentry(options, trimmed));
    return true;
  } on Object {
    // Crash reporting must never stop the app from starting.
    return false;
  }
}

void configureSentry(SentryFlutterOptions o, String dsn) {
  o
    ..dsn = dsn
    ..sendDefaultPii = false
    ..attachScreenshot = false
    // ignore: experimental_member_use
    ..attachViewHierarchy = false
    ..enableUserInteractionBreadcrumbs = false
    ..captureFailedRequests = false
    ..maxRequestBodySize = MaxRequestBodySize.never
    ..tracesSampleRate = _tracesSampleRate
    ..beforeSend = ((event, hint) => scrubEvent(event))
    ..beforeSendTransaction = ((tx, hint) => scrubTransaction(tx))
    ..beforeBreadcrumb = ((crumb, hint) => scrubBreadcrumb(crumb));
  o.replay.sessionSampleRate = 0;
  o.replay.onErrorSampleRate = 0;
}

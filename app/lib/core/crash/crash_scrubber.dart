// ignore_for_file: deprecated_member_use
import 'package:sentry_flutter/sentry_flutter.dart';

import 'pii_text.dart';

export 'pii_text.dart' show scrubText, scrubUrl;

const _removed = '[removed]';

/// Breadcrumb data keys that are safe to keep (after scrubbing).
const _keptData = {
  'from',
  'to',
  'route',
  'url',
  'method',
  'status_code',
  'state',
};

/// Runs [scrub] and drops the result when it throws: a report that cannot be
/// scrubbed is never sent.
T? failClosed<T>(T? input, T? Function(T) scrub) {
  if (input == null) return null;
  try {
    return scrub(input);
  } on Object {
    return null;
  }
}

SentryEvent? scrubEvent(SentryEvent event) => failClosed(event, (e) {
  _scrubCommon(e);
  final message = e.message;
  if (message != null) {
    e.message = SentryMessage(
      scrubText(message.formatted),
      template: message.template == null ? null : scrubText(message.template!),
    );
  }
  e.exceptions?.forEach(_scrubException);
  e.breadcrumbs = _scrubCrumbs(e.breadcrumbs);
  return e;
});

SentryTransaction? scrubTransaction(SentryTransaction tx) =>
    failClosed(tx, (t) {
      _scrubCommon(t);
      final name = t.transaction;
      if (name != null) t.transaction = scrubUrl(name);
      for (final span in t.spans) {
        final d = span.context.description;
        if (d != null) span.context.description = scrubUrl(d);
        for (final key in span.data.keys.toList()) {
          span.removeData(key);
        }
      }
      t.breadcrumbs = _scrubCrumbs(t.breadcrumbs);
      return t;
    });

/// `beforeBreadcrumb`: null drops it.
Breadcrumb? scrubBreadcrumb(Breadcrumb? crumb) =>
    failClosed(crumb, _scrubCrumb);

/// Parse errors (FormatException) append the offending source text on the
/// lines after the message, so only the first line survives, capped.
const _maxExceptionValue = 200;

void _scrubException(SentryException x) {
  final v = x.value;
  if (v != null) {
    final firstLine = v.split('\n').first;
    final scrubbed = scrubText(firstLine);
    x.value = scrubbed.length > _maxExceptionValue
        ? scrubbed.substring(0, _maxExceptionValue)
        : scrubbed;
  }
  final type = x.type;
  if (type != null) x.type = scrubText(type);
  final mechanism = x.mechanism;
  if (mechanism != null && mechanism.data.isNotEmpty) {
    mechanism.data = {for (final k in mechanism.data.keys) k: _removed};
  }
}

void _scrubCommon(SentryEvent e) {
  e.user = null;
  final transaction = e.transaction;
  if (transaction != null) e.transaction = scrubUrl(transaction);
  e.serverName = null;
  e.request = _scrubRequest(e.request);
  final tags = e.tags;
  if (tags != null) {
    e.tags = {for (final t in tags.entries) t.key: scrubUrl(t.value)};
  }
  final extra = e.extra;
  if (extra != null) {
    // Free-form: drop the values rather than guess which are personal.
    e.extra = {for (final k in extra.keys) k: _removed};
  }
}

SentryRequest? _scrubRequest(SentryRequest? r) {
  if (r == null) return null;
  return SentryRequest(
    url: r.url == null ? null : scrubUrl(r.url!),
    method: r.method,
  );
}

List<Breadcrumb>? _scrubCrumbs(List<Breadcrumb>? crumbs) {
  if (crumbs == null) return null;
  return [for (final c in crumbs) ?_scrubCrumb(c)];
}

/// Typing, taps (widget labels hold user text) and any other `ui.*` event are
/// dropped. Everything else keeps only whitelisted, scrubbed data.
Breadcrumb? _scrubCrumb(Breadcrumb c) {
  final category = c.category ?? '';
  if (category.startsWith('ui.') || c.type == 'user') return null;
  final data = c.data;
  return Breadcrumb(
    message: c.message == null ? null : scrubUrl(c.message!),
    category: c.category,
    type: c.type,
    level: c.level,
    timestamp: c.timestamp,
    data: data == null ? null : _scrubData(data),
  );
}

Map<String, dynamic> _scrubData(Map<String, dynamic> data) => {
  for (final e in data.entries)
    if (_keptData.contains(e.key))
      if (e.value case final String s)
        e.key: scrubUrl(s)
      else if (e.value is num || e.value is bool)
        e.key: e.value,
};

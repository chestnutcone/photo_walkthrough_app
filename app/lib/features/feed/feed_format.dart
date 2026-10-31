import 'package:intl/intl.dart';
import 'package:vela_api_client/vela_api_client.dart';

/// "Oct 2", or "Oct 2, 2025" when [date] is not in [now]'s year, in
/// [locale] (default: `Intl.defaultLocale`, en_US until the app adds
/// localizations; other locales need `initializeDateFormatting` first).
String formatFeedDate(DateTime date, DateTime now, {String? locale}) {
  final local = date.toLocal();
  return local.year == now.toLocal().year
      ? DateFormat.MMMd(locale).format(local)
      : DateFormat.yMMMd(locale).format(local);
}

/// "{n} CHAPTERS · {n} REPLIES" with singular forms for 1.
String feedMeta(Work work) {
  final chapters = work.chapters.length;
  final replies = work.chapters.fold<int>(
    0,
    (sum, c) => sum + (c.replyCount ?? 0),
  );
  return '$chapters ${chapters == 1 ? 'CHAPTER' : 'CHAPTERS'} · '
      '$replies ${replies == 1 ? 'REPLY' : 'REPLIES'}';
}

/// "99+" above 99.
String unreadLabel(int count) => count > 99 ? '99+' : '$count';

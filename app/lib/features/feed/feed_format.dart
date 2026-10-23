import 'package:vela_api_client/vela_api_client.dart';

const _months = [
  'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
  'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec', //
];

/// "Oct 2", or "Oct 2, 2025" when [date] is not in [now]'s year.
String formatFeedDate(DateTime date, DateTime now) {
  final local = date.toLocal();
  final base = '${_months[local.month - 1]} ${local.day}';
  return local.year == now.toLocal().year ? base : '$base, ${local.year}';
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

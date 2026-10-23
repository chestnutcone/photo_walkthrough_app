import 'package:vela_api_client/vela_api_client.dart';

/// "2.4k" above 999, otherwise the plain number.
String compactCount(int n) {
  if (n < 1000) return '$n';
  final k = n / 1000;
  final text = k < 10 ? k.toStringAsFixed(1) : k.round().toString();
  return '${text.endsWith('.0') ? text.substring(0, text.length - 2) : text}k';
}

/// First word of a display name; empty names give "the artist".
String firstName(String displayName) {
  final trimmed = displayName.trim();
  return trimmed.isEmpty ? 'the artist' : trimmed.split(RegExp(r'\s+')).first;
}

/// "Followed by Teo, Mara and 2.4k others". [names] are display names of the
/// first followers; [total] is the follower count.
String followedByLine(List<String> names, int total) {
  final shown = [for (final n in names.take(2)) firstName(n)];
  if (shown.isEmpty) return '';
  final others = total - shown.length;
  final lead = shown.length == 2 ? '${shown[0]}, ${shown[1]}' : shown[0];
  if (others <= 0) {
    return shown.length == 2
        ? 'Followed by ${shown[0]} and ${shown[1]}'
        : 'Followed by ${shown[0]}';
  }
  return 'Followed by $lead and ${compactCount(others)} '
      '${others == 1 ? 'other' : 'others'}';
}

/// "{DISCIPLINE} · {CITY}", skipping whatever is missing.
String identityLine(Profile p) => [
  if (p.discipline?.trim().isNotEmpty ?? false) p.discipline!.trim(),
  if (p.city?.trim().isNotEmpty ?? false) p.city!.trim(),
].join(' · ').toUpperCase();

/// "30 seconds", "1 minute", "2 hours": how long a rate limit asks to wait.
String waitLabel(Duration d) {
  if (d.inSeconds < 60) {
    final s = d.inSeconds < 1 ? 1 : d.inSeconds;
    return '$s ${s == 1 ? 'second' : 'seconds'}';
  }
  if (d.inMinutes < 60) {
    final m = (d.inSeconds / 60).ceil();
    return '$m ${m == 1 ? 'minute' : 'minutes'}';
  }
  final h = (d.inMinutes / 60).ceil();
  return '$h ${h == 1 ? 'hour' : 'hours'}';
}

/// Count line: "{n} followers", singular for 1.
String followersTitle(int n) => '$n ${n == 1 ? 'follower' : 'followers'}';

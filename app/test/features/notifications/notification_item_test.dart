import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/notifications/notification_item.dart';

import '../search/support/fake_search_api.dart' show searchProfile;
import 'support/fake_notifications_api.dart';

void main() {
  final now = DateTime.utc(2026, 10, 22, 12);

  group('text', () {
    NotificationItem item(Map<String, Object?> j) =>
        NotificationItem.tryFromJson(j)!;

    test('follow', () {
      expect(
        item(notifJson('n', type: 'follow', workId: null)).text,
        'Ines a1 started following you',
      );
    });

    test('single_reply_names_the_story', () {
      expect(item(notifJson('n')).text, 'Ines a1 replied on Salt and Rope');
    });

    test('grouped_replies_show_the_count', () {
      expect(
        item(notifJson('n', count: 3)).text,
        '3 new replies on Salt and Rope',
      );
    });

    test('new_story_inquiry_and_moderation', () {
      expect(
        item(notifJson('n', type: 'new_story')).text,
        'Ines a1 published Salt and Rope',
      );
      expect(
        item(notifJson('n', type: 'inquiry', workId: null)).text,
        'Ines a1 sent you an inquiry',
      );
      expect(
        item(notifJson('n', type: 'moderation')).text,
        'A moderator reviewed your story',
      );
    });

    test('a_missing_title_reads_as_a_story', () {
      expect(
        item(notifJson('n', title: null)).text,
        'Ines a1 replied on a story',
      );
    });

    test('an_unknown_type_gets_generic_text_and_never_throws', () {
      final i = item(notifJson('n', type: 'mystery'));
      expect(i.kind, NotificationKind.unknown);
      expect(i.text, 'Ines a1 has an update for you');
    });
  });

  group('lenient parsing', () {
    test('skips_rows_that_are_not_usable_and_keeps_the_rest', () {
      final parsed = parseNotificationRows([
        notifJson('ok'),
        notifJson('weird', type: 'mystery'),
        {
          'id': 'no-actor',
          'type': 'follow',
          'createdAt': '2026-10-22T10:00:00Z',
        },
        'junk',
        null,
        {
          'id': 'bad-date',
          'type': 'follow',
          'actor': searchProfile('a1'),
          'createdAt': 'x',
        },
      ]);
      expect([for (final i in parsed.items) i.id], ['ok', 'weird']);
      expect(parsed.skipped, 4);
    });

    test('hostile_text_is_kept_verbatim', () {
      final i = NotificationItem.tryFromJson(
        notifJson('n', title: '<script>alert(1)</script> مرحبا 😀'),
      )!;
      expect(i.text, contains('<script>alert(1)</script> مرحبا 😀'));
    });
  });

  group('destination', () {
    NotificationItem item(Map<String, Object?> j) =>
        NotificationItem.tryFromJson(j)!;

    test('reply_opens_the_chapter_with_replies', () {
      expect(
        item(notifJson('n', chapter: 2)).location,
        '/story/w1?chapter=2&replies=1',
      );
    });

    test('reply_without_a_chapter_opens_the_story', () {
      expect(item(notifJson('n', chapter: null)).location, '/story/w1');
    });

    test('follow_and_inquiry_open_the_actor', () {
      expect(
        item(notifJson('n', type: 'follow', workId: null)).location,
        '/artist/ha1',
      );
      expect(
        item(notifJson('n', type: 'inquiry', workId: null)).location,
        '/artist/ha1',
      );
    });

    test('new_story_and_moderation_open_the_story', () {
      expect(item(notifJson('n', type: 'new_story')).location, '/story/w1');
      expect(item(notifJson('n', type: 'moderation')).location, '/story/w1');
    });

    test('unknown_opens_the_actor_and_a_story_less_reply_goes_nowhere', () {
      expect(item(notifJson('n', type: 'mystery')).location, '/artist/ha1');
      expect(item(notifJson('n', workId: null)).location, isNull);
    });
  });

  group('relativeTime', () {
    test('buckets', () {
      String at(Duration ago) => relativeTime(now.subtract(ago), now);
      expect(at(const Duration(seconds: 20)), 'now');
      expect(at(const Duration(minutes: 5)), '5m');
      expect(at(const Duration(hours: 3)), '3h');
      expect(at(const Duration(days: 2)), '2d');
      expect(at(const Duration(days: 15)), '2w');
      expect(at(const Duration(days: 400)), '1y');
    });

    test('a_future_timestamp_reads_as_now', () {
      expect(relativeTime(now.add(const Duration(hours: 1)), now), 'now');
    });
  });
}

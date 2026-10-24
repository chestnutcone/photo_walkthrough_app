import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/report/report_sheet_hook.dart';

import 'support/story_harness.dart';

final _menu = find.byKey(const ValueKey('story-menu'));

void main() {
  testWidgets('story_menu_reports_the_work_and_leaves_when_filed', (t) async {
    final calls = <String>[];
    final h = await pumpStory(
      t,
      signedIn: true,
      overrides: [
        reportSheetHook.overrideWithValue((
          ctx, {
          required targetType,
          required targetId,
        }) async {
          calls.add('$targetType/$targetId');
          return true;
        }),
      ],
    );
    expect(_menu, findsOneWidget);

    await t.tap(_menu);
    await t.pumpAndSettle();
    await t.tap(find.text('Report story'));
    await t.pumpAndSettle();

    expect(calls, ['work/w1']);
    expect(find.text('route:feed'), findsOneWidget);
    expect(h.router.state.uri.path, '/');
  });

  testWidgets('story_stays_open_when_the_report_is_cancelled', (t) async {
    await pumpStory(
      t,
      signedIn: true,
      overrides: [
        reportSheetHook.overrideWithValue(
          (ctx, {required targetType, required targetId}) async => false,
        ),
      ],
    );
    await t.tap(_menu);
    await t.pumpAndSettle();
    await t.tap(find.text('Report story'));
    await t.pumpAndSettle();

    expect(find.text('route:feed'), findsNothing);
    expect(find.text('Salt and Rope'), findsWidgets);
  });

  testWidgets('own_stories_have_no_report_menu', (t) async {
    await pumpStory(t, signedIn: true, work: workJson(isOwner: true));
    expect(_menu, findsNothing);
  });

  testWidgets('guests_see_the_report_menu', (t) async {
    await pumpStory(t);
    expect(_menu, findsOneWidget);
  });

  testWidgets('more_and_close_buttons_have_44pt_tap_targets', (t) async {
    await pumpStory(t, signedIn: true);
    for (final k in [_menu, find.byKey(const ValueKey('close'))]) {
      final size = t.getSize(k);
      expect(size.width, greaterThanOrEqualTo(44));
      expect(size.height, greaterThanOrEqualTo(44));
    }
  });
}

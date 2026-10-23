import 'package:flutter_test/flutter_test.dart';

import '../story/support/story_harness.dart';
import 'support/sheet_harness.dart' show composerInput;

Future<void> _openReplies(WidgetTester t) async {
  await t.tap(find.text('Begin →'));
  await t.pumpAndSettle();
  await t.tap(find.text('Reply'));
  await t.pumpAndSettle();
}

void main() {
  testWidgets('story_default_hook_opens_real_sheet_with_chapter_title', (
    t,
  ) async {
    await pumpStory(t);
    await _openReplies(t);
    expect(find.text('REPLIES ON'), findsOneWidget);
    expect(find.text('Detail 1'), findsWidgets);
    expect(composerInput, findsOneWidget);
    await t.tap(find.text('Done'));
    await t.pumpAndSettle();
    expect(find.text('REPLIES ON'), findsNothing);
    expect(find.text('Reply'), findsOneWidget); // thread closed again
  });

  testWidgets('read_only_story_opens_sheet_without_composer', (t) async {
    await pumpStory(t, readOnly: true);
    await _openReplies(t);
    expect(find.text('REPLIES ON'), findsOneWidget);
    expect(composerInput, findsNothing);
  });
}

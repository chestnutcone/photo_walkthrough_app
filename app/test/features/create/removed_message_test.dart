import 'package:flutter_test/flutter_test.dart';

import '../story/support/story_harness.dart';

void main() {
  testWidgets('removed_work_shows_removed_by_moderators_message', (t) async {
    await pumpStory(
      t,
      signedIn: true,
      work: {
        ...workJson(isOwner: true),
        'status': 'removed',
        'imageUrl': null,
        'thumbUrl': null,
      },
    );
    expect(find.text('Removed by moderators'), findsOneWidget);
  });

  testWidgets('blocked_owner_view_shows_policy_message', (t) async {
    await pumpStory(
      t,
      signedIn: true,
      work: {...workJson(isOwner: true), 'status': 'blocked'},
    );
    expect(
      find.text(
        "This photo can't be published (content policy). It has been sent for review.",
      ),
      findsOneWidget,
    );
  });
}

// Flow E: create and publish a story (landscape photo and an EXIF-rotated one).
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/e2e.dart';

Future<void> addPin(
  WidgetTester tester,
  double fx,
  double fy,
  String title, {
  required bool zoomIn,
}) async {
  final box = tester.getRect(find.byKey(const ValueKey('photo-box')));
  await tester.tapAt(box.topLeft + Offset(box.width * fx, box.height * fy));
  await pumpUntil(tester, find.byKey(const ValueKey('chapter-title')));
  await enterKey(tester, 'chapter-title', title);
  await enterKey(tester, 'chapter-body', 'Body of $title');
  await tapKey(tester, zoomIn ? 'zoom-in' : 'stay-put');
  await tapKey(tester, 'add-chapter');
  await settle(tester, 800);
}

void main() {
  initBinding();

  Future<String> createStory(
    WidgetTester tester,
    String flow,
    Account me,
    FakePhotoPicker picker,
    String title,
  ) async {
    await launchApp(tester, picker: picker, signedInAs: me);
    await pumpUntil(tester, find.byKey(const ValueKey('tab-following')));
    goTo(tester, '/create');
    await pumpUntil(tester, find.byKey(const ValueKey('pick-library')));
    await shot(tester, flow, '01-pick-photo');
    await tapKey(tester, 'pick-library');
    await pumpUntil(tester, find.byKey(const ValueKey('photo-box')));
    await settle(tester, 1500);
    await shot(tester, flow, '02-photo-chosen');
    await addPin(tester, 0.3, 0.3, 'First detail', zoomIn: true);
    await shot(tester, flow, '03-first-pin');
    await addPin(tester, 0.7, 0.6, 'Second detail', zoomIn: false);
    await shot(tester, flow, '04-second-pin');
    await enterKey(tester, 'story-title', title);
    await shot(tester, flow, '05-ready');
    await tapKey(tester, 'publish');
    await pumpUntilGone(tester, find.byKey(const ValueKey('create-screen')),
        timeout: const Duration(seconds: 90));
    await settle(tester, 2000);
    await shot(tester, flow, '06-published');
    final mine = await http('GET', '$apiBase/v1/artists/${me.handle}/works', token: me.token);
    final work = (mine['data'] as List).firstWhere((w) => w['title'] == title);
    expect(work['status'] ?? 'published', 'published');
    expect(work['chapters'], hasLength(2));
    expect(work['chapters'][0]['zoom'], isNotNull, reason: 'zoom-in chapter');
    expect(work['chapters'][1]['zoom'], isNull, reason: 'stay-put chapter');
    return work['id'] as String;
  }

  testWidgets('E: create a landscape story, publish, open it', (tester) async {
    final me = await makeAccount('ec$uniq');
    final id = await createStory(
      tester, 'e-create', me,
      FakePhotoPicker(() => testPhoto(w: 1600, h: 1000)),
      'Landscape $uniq',
    );
    goTo(tester, '/story/$id');
    await pumpUntil(tester, find.text('Begin →'));
    await shot(tester, 'e-create', '07-open-published');
    final w = await http('GET', '$apiBase/v1/works/$id', token: me.token);
    expect(w['data']['imageWidth'], greaterThan(w['data']['imageHeight'] as int));
  });

  testWidgets('E: an EXIF-rotated JPEG is shown and stored upright', (tester) async {
    final me = await makeAccount('er$uniq');
    // 1600x1000 stored sideways with orientation 6 = displays as 1000x1600.
    final id = await createStory(
      tester, 'e-create-rotated', me,
      FakePhotoPicker(() => testPhoto(w: 1600, h: 1000, exifOrientation: 6)),
      'Rotated $uniq',
    );
    final w = await http('GET', '$apiBase/v1/works/$id', token: me.token);
    log('rotated image ${w['data']['imageWidth']}x${w['data']['imageHeight']}');
    expect(w['data']['imageHeight'], greaterThan(w['data']['imageWidth'] as int),
        reason: 'EXIF orientation must be baked in');
    goTo(tester, '/story/$id');
    await pumpUntil(tester, find.text('Begin →'));
    await shot(tester, 'e-create-rotated', '07-open-published');
  });

  testWidgets('E: a new account reaches create, portfolio and settings from the feed only', (tester) async {
    final me = await makeAccount('nv$uniq');
    await launchApp(tester, signedInAs: me);
    await pumpUntil(tester, find.byKey(const ValueKey('tab-following')));
    await shot(tester, 'e-nav', '01-feed-with-tab-bar');
    await tapKey(tester, 'tab-bar-create');
    await pumpUntil(tester, find.byKey(const ValueKey('create-screen')));
    await shot(tester, 'e-nav', '02-create');
    await tapKey(tester, 'cancel');
    await pumpUntil(tester, find.byKey(const ValueKey('tab-bar-portfolio')));
    await tapKey(tester, 'tab-bar-portfolio');
    await pumpUntil(tester, find.byKey(const ValueKey('portfolio-settings')));
    await shot(tester, 'e-nav', '03-own-portfolio');
    await tapKey(tester, 'portfolio-settings');
    await pumpUntil(tester, find.byKey(const ValueKey('settings-sign-out')));
  });
}

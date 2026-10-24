// Flows F, G, I: edit profile, search, settings (export, sign out, delete account).
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/e2e.dart';

void main() {
  initBinding();

  testWidgets('F: edit profile bio and see it on the portfolio', (tester) async {
    final me = await makeAccount('pf$uniq', name: 'Profile $uniq');
    await publishStory(me, 'Portfolio story $uniq');
    await launchApp(tester, signedInAs: me);
    await pumpUntil(tester, find.byKey(const ValueKey('tab-following')));
    goTo(tester, '/artist/${me.handle}');
    await pumpUntil(tester, find.byKey(const ValueKey('portfolio-edit')));
    await shot(tester, 'f-profile', '01-own-portfolio');
    await tapKey(tester, 'portfolio-edit');
    await pumpUntil(tester, find.byKey(const ValueKey('edit-bio')));
    await shot(tester, 'f-profile', '02-edit-form');
    await enterKey(tester, 'edit-bio', 'Bio written on device $uniq');
    await enterKey(tester, 'edit-city', 'Oslo');
    await shot(tester, 'f-profile', '03-edited');
    await tapKey(tester, 'edit-save');
    await pumpUntilGone(tester, find.byKey(const ValueKey('edit-save')));
    await pumpUntil(tester, find.byKey(const ValueKey('tab-about')));
    await tapKey(tester, 'tab-about');
    await pumpUntil(tester, find.textContaining('Bio written on device $uniq'));
    await shot(tester, 'f-profile', '04-portfolio-about');
  });

  testWidgets('G: search finds an artist and a story', (tester) async {
    final artist = await makeAccount('sa$uniq', name: 'Searchable Person $uniq');
    await publishStory(artist, 'Searchable story $uniq');
    await launchApp(tester);
    await pumpUntil(tester, find.byKey(const ValueKey('search-button')));
    await tapKey(tester, 'search-button');
    await pumpUntil(tester, find.byKey(const ValueKey('search-field')));
    await shot(tester, 'g-search', '01-empty');
    await enterKey(tester, 'search-field', 'Searchable');
    await pumpUntil(tester, find.byKey(ValueKey('search-artist-${artist.id}')));
    await shot(tester, 'g-search', '02-artist-result');
    await tester.tap(find.byKey(ValueKey('search-artist-${artist.id}')));
    await pumpUntil(tester, find.byKey(const ValueKey('portfolio-follow')));
    await shot(tester, 'g-search', '03-artist-portfolio');
  });

  testWidgets('I: settings export request, sign out', (tester) async {
    final me = await makeAccount('st$uniq');
    await launchApp(tester, signedInAs: me);
    await pumpUntil(tester, find.byKey(const ValueKey('tab-following')));
    goTo(tester, '/settings');
    await pumpUntil(tester, find.byKey(const ValueKey('export-request')));
    await shot(tester, 'i-settings', '01-settings');
    await tapKey(tester, 'export-request');
    await settle(tester, 3000);
    await shot(tester, 'i-settings', '02-export-requested');
    expect(find.byKey(const ValueKey('export-request')), findsNothing,
        reason: 'request button is replaced by the pending status');
    await tapKey(tester, 'settings-sign-out');
    await pumpUntil(tester, find.text('Discover'));
    expect(find.byKey(const ValueKey('tab-following')), findsNothing);
    await shot(tester, 'i-settings', '03-signed-out');
  });

  testWidgets('I: delete account wipes the session', (tester) async {
    final me = await makeAccount('dl$uniq');
    await launchApp(tester, signedInAs: me);
    await pumpUntil(tester, find.byKey(const ValueKey('tab-following')));
    goTo(tester, '/settings');
    await tapKey(tester, 'settings-delete');
    await pumpUntil(tester, find.byKey(const ValueKey('delete-confirm-field')));
    await shot(tester, 'i-delete', '01-confirm-dialog');
    await enterKey(tester, 'delete-confirm-field', 'DELETE');
    await tapKey(tester, 'delete-confirm');
    await pumpUntil(tester, find.text('Discover'));
    expect(find.byKey(const ValueKey('tab-following')), findsNothing);
    await shot(tester, 'i-delete', '02-after-delete');
    // The account is gone server side: its token no longer works.
    Object? err;
    try {
      await http('GET', '$apiBase/v1/me', token: me.token);
    } on StateError catch (e) {
      err = e;
    }
    expect(err, isNotNull, reason: 'deleted account token must be rejected');
    // A cold restart stays a guest.
    await launchApp(tester, clean: false);
    await pumpUntil(tester, find.text('Discover'));
    expect(find.byKey(const ValueKey('tab-following')), findsNothing);
  });
}

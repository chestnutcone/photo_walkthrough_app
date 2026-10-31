import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'gallery_harness.dart';

/// One screen or state of the app, reached through the real router against
/// a [GalleryWorld]. Shared by the screenshot gallery and the a11y audit.
class Scene {
  const Scene(
    this.name,
    this.group,
    this.caption,
    this.world,
    this.act, {
    this.textScales = const [1.0],
    this.settleAnimations = true,
  });

  /// File name stem, e.g. `story-chapter-01`.
  final String name;

  /// Gallery section heading.
  final String group;
  final String caption;
  final GalleryWorld Function() world;
  final Future<void> Function(Gallery g) act;

  /// Key screens also render at 2.0.
  final List<double> textScales;

  /// False for screens with endless animations (loading skeletons).
  final bool settleAnimations;
}

Finder key(String k) => find.byKey(ValueKey(k));
Finder text(String t) => find.text(t);

const both = [1.0, 2.0];

GalleryWorld guest() => GalleryWorld();
GalleryWorld member() => GalleryWorld(signedIn: true);

Future<void> none(Gallery g) async {}

Future<void> openStory(Gallery g, {bool signedIn = false}) async {
  await g.push('/story/w1');
}

Future<void> toChapter(Gallery g, int n) async {
  await openStory(g);
  await g.tap(text('Begin →'));
  for (var i = 1; i < n; i++) {
    await g.tap(find.bySemanticsLabel('Next chapter'));
  }
}

Future<void> toEnd(Gallery g) async {
  await toChapter(g, 4);
  await g.tap(find.bySemanticsLabel('Next chapter'));
}

Future<void> openCreateAnnotate(Gallery g) async {
  await g.push('/create');
  await g.tap(key('pick-library'));
}

Future<void> addChapter(Gallery g, Offset at, String title, String body) async {
  final area = key('photo-area');
  final box = g.tester.getRect(area);
  await g.tester.tapAt(box.topLeft + at);
  await g.settle();
  await g.tester.enterText(key('chapter-title'), title);
  await g.tester.enterText(key('chapter-body'), body);
  await g.settle();
  await g.tap(key('add-chapter'));
}

Future<void> profileStep(Gallery g) async {
  await g.tap(key('welcome-continue'));
  await g.tester.enterText(key('name-field'), 'Sam Reyes');
  await g.tester.enterText(key('handle-field'), 'samreyes');
  await g.tester.pump(const Duration(milliseconds: 600));
  await g.settle();
  await g.tester.testTextInput.receiveAction(TextInputAction.done);
  await g.settle();
}

final scenes = <Scene>[
  // Feed
  Scene(
    'feed-guest',
    'Feed',
    'Feed as a guest: Discover only, Follow opens sign-in, tab bar.',
    guest,
    none,
    textScales: both,
  ),
  Scene(
    'feed-following',
    'Feed',
    'Signed in, Following tab, unread badge on the bell.',
    member,
    (g) => g.tap(text('Following').first),
    textScales: both,
  ),
  Scene(
    'feed-discover-signed-in',
    'Feed',
    'Signed in, Discover tab scrolled to the second card.',
    member,
    (g) async {
      await g.tester.drag(find.byType(Scrollable).first, const Offset(0, -560));
      await g.settle();
    },
  ),
  Scene(
    'feed-empty',
    'Feed',
    'Empty feed (no stories yet).',
    () => GalleryWorld(signedIn: true, feed: Load.empty),
    none,
  ),
  Scene(
    'feed-error',
    'Feed',
    'Feed failed to load: message and Retry.',
    () => GalleryWorld(feed: Load.error),
    none,
  ),
  Scene(
    'feed-loading',
    'Feed',
    'First load: skeleton cards.',
    () => GalleryWorld(feed: Load.loading),
    none,
    settleAnimations: false,
  ),
  // Story viewer
  Scene(
    'story-intro',
    'Story viewer',
    'Intro card with numbered pins; tap the photo or Begin.',
    guest,
    openStory,
    textScales: both,
  ),
  Scene(
    'story-chapter-01',
    'Story viewer',
    'First chapter: camera zoomed to the detail, spotlight ring, progress.',
    guest,
    (g) => toChapter(g, 1),
    textScales: both,
  ),
  Scene(
    'story-chapter-02',
    'Story viewer',
    'Middle chapter with a meta chip and "2 replies".',
    guest,
    (g) => toChapter(g, 2),
  ),
  Scene(
    'story-chapter-04',
    'Story viewer',
    'Last chapter.',
    guest,
    (g) => toChapter(g, 4),
  ),
  Scene(
    'story-end',
    'Story viewer',
    'End card: Explore freely, Replay, Follow, Next story, print inquiry.',
    guest,
    toEnd,
    textScales: both,
  ),
  Scene(
    'story-explore',
    'Story viewer',
    'Explore mode, idle: pins on the whole photo.',
    guest,
    (g) async {
      await openStory(g);
      await g.tap(text('Explore'));
    },
  ),
  Scene(
    'story-explore-pin',
    'Story viewer',
    'Explore mode after tapping pin 3, with Zoom out.',
    guest,
    (g) async {
      await openStory(g);
      await g.tap(text('Explore'));
      await g.tap(key('pin-2'));
    },
  ),
  Scene(
    'story-replies',
    'Story viewer',
    'Reply sheet on chapter 2, signed in, with the composer.',
    member,
    (g) async {
      await toChapter(g, 2);
      await g.tap(text('2 replies'));
    },
    textScales: both,
  ),
  Scene(
    'story-replies-guest',
    'Guest states',
    'Reply sheet as a guest: the composer asks to sign in.',
    guest,
    (g) async {
      await toChapter(g, 2);
      await g.tap(text('2 replies'));
    },
  ),
  Scene(
    'story-replies-empty',
    'Story viewer',
    'Reply sheet with no replies yet.',
    () => GalleryWorld(signedIn: true, replies: Load.empty),
    (g) async {
      await toChapter(g, 1);
      await g.tap(text('Reply'));
    },
  ),
  Scene(
    'story-report',
    'Story viewer',
    'Report a story (from the top bar menu).',
    member,
    (g) async {
      await openStory(g);
      await g.tap(key('story-menu'));
      await g.tap(key('story-menu-report'));
    },
  ),
  Scene(
    'story-not-found',
    'Errors',
    'A story that does not exist or was removed.',
    guest,
    (g) => g.push('/story/w404'),
  ),
  // Create
  Scene(
    'create-pick',
    'Create story',
    'Step 1: choose a photograph (library or camera).',
    member,
    (g) => g.push('/create'),
    textScales: both,
  ),
  Scene(
    'create-annotate',
    'Create story',
    'Step 2: photo picked, hint to tap a detail.',
    member,
    openCreateAnnotate,
  ),
  Scene(
    'create-ghost',
    'Create story',
    'Ghost pin and the chapter draft form.',
    member,
    (g) async {
      await openCreateAnnotate(g);
      final box = g.tester.getRect(key('photo-area'));
      await g.tester.tapAt(box.topLeft + const Offset(120, 160));
      await g.settle();
      await g.tester.enterText(key('chapter-title'), 'The ferry');
      await g.settle();
    },
  ),
  Scene(
    'create-chapters',
    'Create story',
    'Two chapters added: numbered pins and the chapter list; Publish enabled.',
    member,
    (g) async {
      await openCreateAnnotate(g);
      await addChapter(g, const Offset(120, 160), 'The ferry', 'Leaving.');
      await addChapter(g, const Offset(260, 300), 'Tide line', 'Wet sand.');
      await g.tester.drag(find.byType(Scrollable).first, const Offset(0, -300));
      await g.settle();
    },
  ),
  // Portfolio
  Scene(
    'portfolio-works',
    'Portfolio',
    'Artist portfolio, Works tab, as a guest.',
    guest,
    (g) => g.push('/artist/ines'),
    textScales: both,
  ),
  Scene(
    'portfolio-works-grid',
    'Portfolio',
    'Works tab scrolled to the featured story and the grid.',
    guest,
    (g) async {
      await g.push('/artist/ines');
      await g.tester.drag(find.byType(Scrollable).first, const Offset(0, -500));
      await g.settle();
    },
  ),
  Scene(
    'portfolio-about',
    'Portfolio',
    'About & contact tab: services, contact rows, exhibitions.',
    member,
    (g) async {
      await g.push('/artist/ines');
      await g.tap(key('tab-about'));
      await g.tester.drag(find.byType(Scrollable).first, const Offset(0, -420));
      await g.settle();
    },
  ),
  Scene(
    'portfolio-own-empty',
    'Portfolio',
    'Own portfolio with no stories: Edit profile, New story, empty box.',
    member,
    (g) => g.push('/artist/you'),
  ),
  Scene('portfolio-followers', 'Portfolio', 'Followers sheet.', member, (
    g,
  ) async {
    await g.push('/artist/ines');
    await g.tap(key('stat-followers'));
  }),
  Scene(
    'portfolio-contact',
    'Portfolio',
    'Contact sheet: inquiry type chips, email, message.',
    member,
    (g) async {
      await g.push('/artist/ines');
      await g.tap(key('portfolio-contact'));
    },
  ),
  Scene(
    'portfolio-not-found',
    'Errors',
    'A handle that does not exist.',
    guest,
    (g) => g.push('/artist/nobody'),
  ),
  Scene(
    'portfolio-error',
    'Errors',
    'Portfolio failed to load: Retry.',
    () => GalleryWorld(artist: Load.error),
    (g) => g.push('/artist/ines'),
  ),
  // Search
  Scene(
    'search-start',
    'Search',
    'Search with recent searches and suggested artists.',
    guest,
    (g) => g.push('/search'),
    textScales: both,
  ),
  Scene('search-artists', 'Search', 'Artist results for "a".', guest, (
    g,
  ) async {
    await g.push('/search');
    await g.tester.enterText(key('search-field'), 'ma');
    await g.tester.pump(const Duration(milliseconds: 600));
    await g.settle();
  }),
  Scene('search-stories', 'Search', 'Story results.', guest, (g) async {
    await g.push('/search');
    await g.tester.enterText(key('search-field'), 'ro');
    await g.tester.pump(const Duration(milliseconds: 600));
    await g.settle();
    await g.tap(text('Stories'));
  }),
  Scene('search-no-results', 'Search', 'No results.', guest, (g) async {
    await g.push('/search');
    await g.tester.enterText(key('search-field'), 'zzz');
    await g.tester.pump(const Duration(milliseconds: 600));
    await g.settle();
  }),
  // Notifications
  Scene(
    'notifications',
    'Notifications',
    'Replies (folded), follow, new story, inquiry; unread first.',
    member,
    (g) => g.push('/notifications'),
    textScales: both,
  ),
  Scene(
    'notifications-empty',
    'Notifications',
    'No notifications yet.',
    () => GalleryWorld(signedIn: true, notifications: Load.empty),
    (g) => g.push('/notifications'),
  ),
  Scene(
    'notifications-error',
    'Errors',
    'Notifications failed to load.',
    () => GalleryWorld(signedIn: true, notifications: Load.error),
    (g) => g.push('/notifications'),
  ),
  // Settings and profile
  Scene(
    'settings',
    'Settings',
    'Settings: account, export, legal, sign out, delete.',
    member,
    (g) => g.push('/settings'),
    textScales: both,
  ),
  Scene(
    'settings-delete',
    'Settings',
    'Delete account confirmation (type DELETE).',
    member,
    (g) async {
      await g.push('/settings');
      await g.tap(key('settings-delete'));
    },
  ),
  Scene(
    'edit-profile',
    'Settings',
    'Edit profile.',
    member,
    (g) => g.push('/profile/edit'),
  ),
  // Onboarding
  Scene(
    'onboarding-welcome',
    'Onboarding',
    'Welcome step for a new account.',
    () => GalleryWorld(signedIn: true, onboarded: false),
    none,
    textScales: both,
  ),
  Scene(
    'onboarding-profile',
    'Onboarding',
    'Profile step: name, handle (available), 18+ and terms.',
    () => GalleryWorld(signedIn: true, onboarded: false),
    profileStep,
    textScales: both,
  ),
  Scene(
    'onboarding-demo',
    'Onboarding',
    'The bundled sample story.',
    () => GalleryWorld(signedIn: true, onboarded: false),
    (g) => g.tap(key('welcome-demo')),
  ),
  // Sign-in
  Scene(
    'sign-in-sheet',
    'Sign-in',
    'Sign-in sheet opened by a guest action (Follow).',
    guest,
    (g) => g.tap(find.text('Follow').first),
    textScales: both,
  ),
  Scene('sign-in-email', 'Sign-in', 'Email step.', guest, (g) async {
    await g.tap(find.text('Follow').first);
    await g.tap(key('sso-email'));
    await g.tester.enterText(key('email-field'), 'sam@example.com');
    await g.settle();
  }),
  Scene('sign-in-code', 'Sign-in', 'Six-digit code step.', guest, (g) async {
    await g.tap(find.text('Follow').first);
    await g.tap(key('sso-email'));
    await g.tester.enterText(key('email-field'), 'sam@example.com');
    await g.settle();
    await g.tap(key('email-send'));
  }),
  Scene(
    'guest-create',
    'Guest states',
    'Guest taps + : sign-in sheet instead of the create flow.',
    guest,
    (g) => g.tap(key('tab-bar-create')),
  ),
  Scene(
    'suspended',
    'Errors',
    'Suspended account screen.',
    () => GalleryWorld(signedIn: true, suspended: true),
    none,
  ),
  Scene(
    'not-found-route',
    'Errors',
    'Unknown route.',
    guest,
    (g) => g.go('/nowhere'),
  ),
];

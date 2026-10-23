import 'package:flutter_test/flutter_test.dart';
import 'package:vela_api_client/vela_api_client.dart';
import 'package:vela_app/features/profile_edit/profile_form.dart';

void main() {
  const base = ProfileForm(
    displayName: 'Ana',
    handle: 'ana',
    bio: 'hi',
    publicEmail: 'a@b.co',
  );

  test('changed_fields_sends_only_what_changed', () {
    final body = changedFields(base, base.copyWith(city: ' Porto '));
    expect(body, {'city': 'Porto'});
  });

  test('nothing_changed_sends_nothing', () {
    expect(changedFields(base, base), isEmpty);
  });

  test('emptied_public_email_is_sent_as_explicit_null', () {
    final body = changedFields(base, base.copyWith(publicEmail: ''));
    expect(body.containsKey('publicEmail'), isTrue);
    expect(body['publicEmail'], isNull);
  });

  test('avatar_flag_is_added_when_a_photo_was_uploaded', () {
    expect(changedFields(base, base, avatarUploaded: true), {
      'avatarUploaded': true,
    });
  });

  test('bio_counts_code_points_like_the_server', () {
    // 280 emoji = 560 UTF-16 units but 280 code points: allowed.
    expect(validateForm(base.copyWith(bio: '😀' * 280)), isEmpty);
    expect(validateForm(base.copyWith(bio: '😀' * 281)).keys, ['bio']);
  });

  test('bio_rejects_control_characters_but_allows_newlines', () {
    expect(validateForm(base.copyWith(bio: 'a\nb')), isEmpty);
    expect(validateForm(base.copyWith(bio: 'a\tb')).keys, ['bio']);
  });

  test('website_must_be_https', () {
    expect(validateForm(base.copyWith(website: 'https://a.example')), isEmpty);
    expect(validateForm(base.copyWith(website: 'http://a.example')).keys, [
      'website',
    ]);
    expect(validateForm(base.copyWith(website: 'javascript:alert(1)')).keys, [
      'website',
    ]);
    expect(validateForm(base.copyWith(website: 'https://')).keys, ['website']);
  });

  test('public_email_is_validated_and_optional', () {
    expect(validateForm(base.copyWith(publicEmail: '')), isEmpty);
    expect(validateForm(base.copyWith(publicEmail: 'nope')).keys, [
      'publicEmail',
    ]);
    expect(validateForm(base.copyWith(publicEmail: 'a b@c.de')).keys, [
      'publicEmail',
    ]);
  });

  test('service_and_show_limits', () {
    expect(serviceProblem('', '', ''), isNotNull);
    expect(serviceProblem('Portrait', 'x' * 301, ''), isNotNull);
    expect(serviceProblem('Portrait', 'ok', 'From \$400'), isNull);
    expect(showProblem('20', 'x'), isNotNull);
    expect(showProblem('2024', 'Group show'), isNull);
    final many = [
      for (var i = 0; i < 11; i++)
        ServiceItem(title: 't', description: '', price: ''),
    ];
    expect(validateForm(base.copyWith(services: many)).keys, ['services']);
  });

  test('handle_is_normalised_before_comparison', () {
    expect(changedFields(base, base.copyWith(handle: ' @Ana ')), isEmpty);
  });

  test('unchanged_legacy_http_website_is_tolerated', () {
    final legacy = base.copyWith(website: 'http://old.example');
    expect(validateForm(legacy, original: legacy), isEmpty);
    expect(validateForm(legacy), isNotEmpty);
    expect(
      validateForm(
        legacy.copyWith(website: 'http://new.example'),
        original: legacy,
      ).keys,
      ['website'],
    );
  });
}

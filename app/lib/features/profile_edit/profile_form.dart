import 'package:flutter/foundation.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../onboarding/handle_rules.dart';

const int bioMax = 280;
const int nameMax = 60;
const int cityMax = 80;
const int websiteMax = 200;
const int emailMax = 254;
const int maxServices = 10;
const int maxShows = 20;
const int serviceTitleMax = 80;
const int serviceDescriptionMax = 300;
const int servicePriceMax = 40;
const int showTextMax = 200;

/// Length the way the server counts it: Unicode code points (an emoji is 1).
/// The server's `EnumerateRunes().Count()` is the reference.
int codePointCount(String s) => s.runes.length;

bool _hasControl(String s) =>
    s.runes.any((r) => (r < 0x20 || (r >= 0x7F && r < 0xA0)) && r != 0x0A);

final _emailPattern = RegExp(r'^[^\s@]+@[^\s@]+\.[^\s@.]+$');
final _yearPattern = RegExp(r'^[0-9]{4}$');

/// The editable fields as one immutable value.
@immutable
class ProfileForm {
  const ProfileForm({
    this.displayName = '',
    this.handle = '',
    this.bio = '',
    this.city = '',
    this.publicEmail = '',
    this.website = '',
    this.services = const [],
    this.shows = const [],
  });

  factory ProfileForm.fromProfile(Profile p) => ProfileForm(
    displayName: p.displayName,
    handle: p.handle,
    bio: p.bio,
    city: p.city ?? '',
    publicEmail: p.publicEmail ?? '',
    website: p.website ?? '',
    services: List.unmodifiable(p.services ?? const <ServiceItem>[]),
    shows: List.unmodifiable(p.shows ?? const <ShowItem>[]),
  );

  final String displayName;
  final String handle;
  final String bio;
  final String city;
  final String publicEmail;
  final String website;
  final List<ServiceItem> services;
  final List<ShowItem> shows;

  ProfileForm copyWith({
    String? displayName,
    String? handle,
    String? bio,
    String? city,
    String? publicEmail,
    String? website,
    List<ServiceItem>? services,
    List<ShowItem>? shows,
  }) => ProfileForm(
    displayName: displayName ?? this.displayName,
    handle: handle ?? this.handle,
    bio: bio ?? this.bio,
    city: city ?? this.city,
    publicEmail: publicEmail ?? this.publicEmail,
    website: website ?? this.website,
    services: services ?? this.services,
    shows: shows ?? this.shows,
  );

  /// Values as they would be sent (trimmed, handle normalised).
  ProfileForm get normalized => ProfileForm(
    displayName: displayName.trim(),
    handle: HandleRules.normalize(handle),
    bio: bio,
    city: city.trim(),
    publicEmail: publicEmail.trim(),
    website: website.trim(),
    services: services,
    shows: shows,
  );

  @override
  bool operator ==(Object other) =>
      other is ProfileForm &&
      other.displayName == displayName &&
      other.handle == handle &&
      other.bio == bio &&
      other.city == city &&
      other.publicEmail == publicEmail &&
      other.website == website &&
      listEquals(other.services, services) &&
      listEquals(other.shows, shows);

  @override
  int get hashCode => Object.hash(
    displayName,
    handle,
    bio,
    city,
    publicEmail,
    website,
    Object.hashAll(services),
    Object.hashAll(shows),
  );
}

/// Field problems keyed by field name. Empty means the form can be sent.
/// Messages never echo what the user typed.
///
/// A stored website that was not touched is not re-validated, so an older
/// http address cannot block saving other fields; new input must be https.
Map<String, String> validateForm(ProfileForm raw, {ProfileForm? original}) {
  final f = raw.normalized;
  final errors = <String, String>{};
  final nameLen = codePointCount(f.displayName);
  if (nameLen < 1) {
    errors['displayName'] = 'Enter a name.';
  }
  if (nameLen > nameMax) {
    errors['displayName'] = 'Use at most $nameMax characters.';
  }

  final handleProblem = HandleRules.problem(f.handle);
  if (f.handle.isEmpty) {
    errors['handle'] = 'Enter a handle.';
  }
  if (handleProblem != null) {
    errors['handle'] = HandleRules.message(handleProblem);
  }

  final bioLen = codePointCount(f.bio);
  if (bioLen > bioMax) {
    errors['bio'] = 'Bio is ${bioLen - bioMax} over the $bioMax limit.';
  } else if (_hasControl(f.bio)) {
    errors['bio'] = 'Bio has characters that are not allowed.';
  }
  if (codePointCount(f.city) > cityMax) {
    errors['city'] = 'Use at most $cityMax characters.';
  }
  if (f.publicEmail.isNotEmpty &&
      (f.publicEmail.length > emailMax ||
          !_emailPattern.hasMatch(f.publicEmail))) {
    errors['publicEmail'] = 'Enter a valid email address.';
  }
  final websiteTouched =
      original == null || original.normalized.website != f.website;
  if (websiteTouched && f.website.isNotEmpty && !_validWebsite(f.website)) {
    errors['website'] = 'Use a web address that starts with https://';
  }
  if (f.services.length > maxServices) {
    errors['services'] = 'At most $maxServices services.';
  }
  if (f.shows.length > maxShows) {
    errors['shows'] = 'At most $maxShows shows.';
  }
  return errors;
}

bool _validWebsite(String v) {
  if (v.length > websiteMax || v.contains(RegExp(r'\s'))) {
    return false;
  }
  final uri = Uri.tryParse(v);
  return uri != null &&
      uri.scheme.toLowerCase() == 'https' &&
      uri.host.isNotEmpty &&
      uri.host.contains('.');
}

/// Problem with one service, or null.
String? serviceProblem(String title, String description, String price) {
  if (title.trim().isEmpty) {
    return 'Enter a title.';
  }
  if (codePointCount(title.trim()) > serviceTitleMax) {
    return 'Title: at most $serviceTitleMax characters.';
  }
  if (codePointCount(description) > serviceDescriptionMax) {
    return 'Description: at most $serviceDescriptionMax characters.';
  }
  if (_hasControl(description)) {
    return 'Description has characters that are not allowed.';
  }
  if (codePointCount(price) > servicePriceMax) {
    return 'Price: at most $servicePriceMax characters.';
  }
  return null;
}

/// Problem with one show, or null.
String? showProblem(String year, String text) {
  if (!_yearPattern.hasMatch(year.trim())) {
    return 'Year must be 4 digits.';
  }
  if (codePointCount(text) > showTextMax) {
    return 'Text: at most $showTextMax characters.';
  }
  return null;
}

/// PATCH body with only the fields that differ from [original]
/// (the server leaves absent fields alone). An emptied public email is sent
/// as an explicit null, which is how the server clears it. [avatarUploaded]
/// asks the server to build the avatar from the uploaded blob.
Map<String, Object?> changedFields(
  ProfileForm original,
  ProfileForm current, {
  bool avatarUploaded = false,
}) {
  final a = original.normalized;
  final b = current.normalized;
  final body = <String, Object?>{};
  if (a.displayName != b.displayName) body['displayName'] = b.displayName;
  if (a.handle != b.handle) body['handle'] = b.handle;
  if (a.bio != b.bio) body['bio'] = b.bio;
  if (a.city != b.city) body['city'] = b.city;
  if (a.website != b.website) body['website'] = b.website;
  if (a.publicEmail != b.publicEmail) {
    body['publicEmail'] = b.publicEmail.isEmpty ? null : b.publicEmail;
  }
  if (!listEquals(a.services, b.services)) {
    body['services'] = [for (final s in b.services) s.toJson()];
  }
  if (!listEquals(a.shows, b.shows)) {
    body['shows'] = [for (final s in b.shows) s.toJson()];
  }
  if (avatarUploaded) body['avatarUploaded'] = true;
  return body;
}

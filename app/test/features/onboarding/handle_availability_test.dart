import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/features/onboarding/handle_availability.dart';
import 'package:vela_app/features/onboarding/handle_rules.dart';

import '../../core/support/fakes.dart';
import 'support/onboarding_harness.dart';

/// Lets in-flight lookups finish by draining event-loop turns, not by
/// sleeping: a fixed wait was flaky under a loaded full-suite run.
Future<void> _settle() => pumpEventQueue(times: 500);

void main() {
  group('HandleRules', () {
    test('problems', () {
      expect(HandleRules.problem(''), isNull);
      expect(HandleRules.problem('ab'), HandleProblem.tooShort);
      expect(HandleRules.problem('a' * 21), HandleProblem.tooLong);
      expect(HandleRules.problem('a b'), HandleProblem.badCharacters);
      expect(HandleRules.problem('é_é'), HandleProblem.badCharacters);
      expect(HandleRules.problem('admin'), HandleProblem.reserved);
      expect(HandleRules.problem('abc'), isNull);
      expect(HandleRules.problem('a_1'), isNull);
    });

    test('normalize_trims_lowercases_and_drops_a_leading_at', () {
      expect(HandleRules.normalize(' @Ana_R '), 'ana_r');
    });
  });

  group('HandleAvailability stale guard', () {
    late OnboardingApi api;
    late ProviderContainer container;

    setUp(() {
      api = OnboardingApi();
      container = ProviderContainer(
        overrides: [
          sessionStorageProvider.overrideWithValue(FakeSessionStorage()),
          httpClientAdapterProvider.overrideWithValue(FakeAdapter(api.handle)),
        ],
      );
      addTearDown(container.dispose);
      container.listen(handleAvailabilityProvider, (_, _) {});
    });

    HandleCheck state() => container.read(handleAvailabilityProvider);
    HandleAvailability notifier() =>
        container.read(handleAvailabilityProvider.notifier);

    test(
      'late_free_answer_for_an_old_handle_does_not_validate_the_new_one',
      () async {
        // The adapter ignores cancel tokens, so the old answer still arrives.
        api.gates['old_one'] = Completer<void>();
        api.taken = {'new_one'};
        notifier().onChanged('old_one', debounce: Duration.zero);
        await _settle();
        notifier().onChanged('new_one', debounce: Duration.zero);
        await _settle();
        expect(state().handle, 'new_one');
        expect(state().status, HandleStatus.taken);

        api.gates['old_one']!.complete(); // old answer: "free"
        await _settle();

        expect(state().handle, 'new_one');
        expect(state().status, HandleStatus.taken);
      },
    );

    test(
      'late_taken_answer_for_an_old_handle_does_not_block_the_new_one',
      () async {
        api.gates['old_one'] = Completer<void>();
        api.taken = {'old_one'};
        notifier().onChanged('old_one', debounce: Duration.zero);
        await _settle();
        notifier().onChanged('new_one', debounce: Duration.zero);
        await _settle();
        expect(state().status, HandleStatus.available);

        api.gates['old_one']!.complete();
        await _settle();
        expect(state().handle, 'new_one');
        expect(state().status, HandleStatus.available);
      },
    );

    test('answer_after_the_field_became_invalid_is_dropped', () async {
      api.gates['abc'] = Completer<void>();
      notifier().onChanged('abc', debounce: Duration.zero);
      await _settle();
      notifier().onChanged('ab', debounce: Duration.zero);
      api.gates['abc']!.complete();
      await _settle();
      expect(state().status, HandleStatus.invalid);
      expect(state().handle, 'ab');
    });

    test('same_handle_typed_again_is_not_looked_up_twice', () async {
      notifier().onChanged('abc', debounce: Duration.zero);
      await _settle();
      notifier().onChanged('ABC ', debounce: Duration.zero);
      await _settle();
      expect(api.to('/v1/artists/abc'), hasLength(1));
      expect(state().status, HandleStatus.available);
    });

    test('server_error_is_unknown_not_available', () async {
      final failing = ProviderContainer(
        overrides: [
          sessionStorageProvider.overrideWithValue(FakeSessionStorage()),
          httpClientAdapterProvider.overrideWithValue(
            FakeAdapter((_) => jsonResponse(500, errEnvelope('server_error'))),
          ),
        ],
      );
      addTearDown(failing.dispose);
      failing.listen(handleAvailabilityProvider, (_, _) {});
      failing
          .read(handleAvailabilityProvider.notifier)
          .onChanged('abc', debounce: Duration.zero);
      await _settle();
      expect(
        failing.read(handleAvailabilityProvider).status,
        HandleStatus.unknown,
      );
    });
  });
}

import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/settings/delete_account_flow.dart';

/// Final review M-8: the delete-account copy states what the server does
/// (ledger S1b rulings; privacy policy "delete your account"): hidden at once,
/// final removal waits up to 30 days while content is under review, deleted
/// files linger up to 7 days in storage recovery copies.
void main() {
  test('copy matches the deletion behaviour and the privacy policy', () {
    expect(deleteExplanation, contains('hidden at once'));
    expect(deleteExplanation, contains('at most 30 days'));
    expect(deleteExplanation, contains('up to 7 more days'));
    expect(deleteExplanation, isNot(contains('backups are removed within')));
    expect(deleteExplanation, contains('cannot be undone'));
  });
}

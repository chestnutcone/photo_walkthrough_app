import 'package:test/test.dart';
import 'package:vela_api_client/vela_api_client.dart';


/// tests for MeApi
void main() {
  final instance = VelaApiClient().getMeApi();

  group(MeApi, () {
    // Get a signed avatar upload URL
    //
    //Future<UploadTicketEnvelope> avatarUpload() async
    test('test avatarUpload', () async {
      // TODO
    });

    // Finish onboarding
    //
    //Future<ProfileEnvelope> completeOnboarding(CompleteOnboardingRequest completeOnboardingRequest) async
    test('test completeOnboarding', () async {
      // TODO
    });

    // Delete the account and all data
    //
    //Future<Envelope> deleteAccount() async
    test('test deleteAccount', () async {
      // TODO
    });

    // Get a data export
    //
    //Future<ExportJobEnvelope> getExport(String id) async
    test('test getExport', () async {
      // TODO
    });

    // Get the signed-in profile
    //
    //Future<ProfileEnvelope> getMe() async
    test('test getMe', () async {
      // TODO
    });

    // Request a data export
    //
    //Future<ExportJobEnvelope> requestExport() async
    test('test requestExport', () async {
      // TODO
    });

    // Update profile fields
    //
    //Future<ProfileEnvelope> updateProfile(UpdateProfileRequest updateProfileRequest) async
    test('test updateProfile', () async {
      // TODO
    });

  });
}

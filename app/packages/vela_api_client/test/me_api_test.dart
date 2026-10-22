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
    //Future<MeEnvelope> completeOnboarding(CompleteOnboardingRequest completeOnboardingRequest) async
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
    //Future<MeEnvelope> getMe() async
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
    // Send avatarUploaded: true after uploading to the avatar upload URL; the server builds the 256 px avatar. 400 invalid_image when the upload is missing, not a JPEG/PNG/WebP, over 25 MB or over 60 megapixels.
    //
    //Future<MeEnvelope> updateProfile(UpdateProfileRequest updateProfileRequest) async
    test('test updateProfile', () async {
      // TODO
    });

  });
}

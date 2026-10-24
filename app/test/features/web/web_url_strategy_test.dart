import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/web/web_url_strategy.dart';

void main() {
  test('configure_installs_the_path_url_strategy', () {
    var installed = 0;
    configureWebUrlStrategy(install: () => installed++);
    expect(installed, 1);
  });

  test('the_default_installer_is_a_no_op_outside_the_browser', () {
    expect(configureWebUrlStrategy, returnsNormally);
  });
}

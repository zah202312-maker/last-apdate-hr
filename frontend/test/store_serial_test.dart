import 'package:flutter_test/flutter_test.dart';
import 'package:hr_app/features/ops/store_shared.dart';

void main() {
  test('next serial follows the highest number in use and ignores other formats', () {
    expect(nextSerial('ISS-', const []), 'ISS-00001');
    expect(nextSerial('ISS-', const ['ISS-00002', 'ISS-00010', 'ISS-7', 'OLD-99', 'ISS-ABC']), 'ISS-00011');
    expect(nextSerial('ITM-', const ['ITM-00003', 'ITM-0001']), 'ITM-00004');
  });
}

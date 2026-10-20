import 'package:flutter_test/flutter_test.dart';
import 'package:senbilirsin/core/version.dart';

void main() {
  test('sürüm karşılaştırması', () {
    expect(isVersionLessThan('1.0.0', '1.0.1'), isTrue);
    expect(isVersionLessThan('1.0.1', '1.0.0'), isFalse);
    expect(isVersionLessThan('1.0.0', '1.0.0'), isFalse);
    expect(isVersionLessThan('1.2', '1.2.0'), isFalse);
    expect(isVersionLessThan('1.9.0', '1.10.0'), isTrue); // sayısal karşılaştırma, metin değil
    expect(isVersionLessThan('0.0.1', '0.0.0'), isFalse);
    expect(isVersionLessThan('1.0.0+5', '1.0.1'), isTrue); // "+derleme" yok sayılır
    expect(isVersionLessThan('2.0.0', '1.99.99'), isFalse);
  });
}

import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:senbilirsin/auth/auth_gateway.dart';
import 'package:senbilirsin/auth/dev_auth_gateway.dart';

Future<DevAuthGateway> newGateway() async {
  SharedPreferences.setMockInitialValues({});
  return DevAuthGateway(await SharedPreferences.getInstance());
}

void main() {
  test('e-posta ile kayıt: doğrulanmamış başlar, doğrulama sonrası değişir', () async {
    final g = await newGateway();
    final user = await g.registerWithEmail('Ali@Test.com', '123456');
    expect(user.email, 'ali@test.com');
    expect(user.needsEmailVerification, isTrue);

    await g.devConfirmEmail();
    final reloaded = await g.reloadUser();
    expect(reloaded.emailVerified, isTrue);
    expect(reloaded.needsEmailVerification, isFalse);
  });

  test('geçersiz e-posta, zayıf parola ve mükerrer kayıt reddedilir', () async {
    final g = await newGateway();
    expect(() => g.registerWithEmail('yanlis', '123456'), throwsA(isA<AuthException>()));
    expect(() => g.registerWithEmail('a@b.com', '123'), throwsA(isA<AuthException>()));
    await g.registerWithEmail('a@b.com', '123456');
    expect(() => g.registerWithEmail('a@b.com', '654321'), throwsA(isA<AuthException>()));
  });

  test('yanlış parola ile giriş yapılamaz, doğru parola ile yapılır', () async {
    final g = await newGateway();
    await g.registerWithEmail('a@b.com', '123456');
    await g.signOut();
    expect(g.currentUser, isNull);
    expect(() => g.signInWithEmail('a@b.com', 'yanlis'), throwsA(isA<AuthException>()));
    final user = await g.signInWithEmail('a@b.com', '123456');
    expect(user.uid, isNotEmpty);
  });

  test('Google girişi doğrulanmış sayılır ve aynı e-postayı tekrar kullanır', () async {
    final g = await newGateway();
    final first = await g.signInWithGoogle(devEmail: 'x@y.com');
    expect(first.emailVerified, isTrue);
    expect(first.needsEmailVerification, isFalse);
    await g.signOut();
    final second = await g.signInWithGoogle(devEmail: 'x@y.com');
    expect(second.uid, first.uid);
  });

  test('en az bir giriş yöntemi bağlı kalır: tek yöntem çıkarılamaz', () async {
    final g = await newGateway();
    await g.registerWithEmail('a@b.com', '123456');
    await g.devConfirmEmail();
    expect(() => g.unlinkProvider(AuthProviders.password), throwsA(isA<AuthException>()));

    final linked = await g.linkGoogle();
    expect(linked.providers, containsAll([AuthProviders.password, AuthProviders.google]));

    final afterUnlink = await g.unlinkProvider(AuthProviders.password);
    expect(afterUnlink.providers, [AuthProviders.google]);
    // Oturum açılan yöntem çıkarıldığı için kalan yönteme geçildi
    expect(afterUnlink.signInProvider, AuthProviders.google);
    expect(() => g.unlinkProvider(AuthProviders.google), throwsA(isA<AuthException>()));
  });

  test('oturum geri yüklenir ve Authorization başlığı kimliği taşır', () async {
    SharedPreferences.setMockInitialValues({});
    final prefs = await SharedPreferences.getInstance();
    final g1 = DevAuthGateway(prefs);
    final user = await g1.registerWithEmail('a@b.com', '123456');

    final g2 = DevAuthGateway(prefs);
    expect(g2.currentUser, isNull);
    await g2.restore();
    expect(g2.currentUser?.uid, user.uid);

    final header = await g2.authorizationHeader();
    expect(header, startsWith('Dev uid=${user.uid};email=a@b.com;verified=false;provider=password'));
  });

  test('hesap silinince aynı e-posta ile giriş yapılamaz', () async {
    final g = await newGateway();
    await g.registerWithEmail('a@b.com', '123456');
    await g.reauthenticate(password: '123456');
    await g.deleteAuthAccount();
    expect(g.currentUser, isNull);
    expect(() => g.signInWithEmail('a@b.com', '123456'), throwsA(isA<AuthException>()));
  });

  test('hesap silme için yanlış parola yeniden doğrulamada reddedilir', () async {
    final g = await newGateway();
    await g.registerWithEmail('a@b.com', '123456');
    expect(() => g.reauthenticate(password: 'yanlis'), throwsA(isA<AuthException>()));
  });
}

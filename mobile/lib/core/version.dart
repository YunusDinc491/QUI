/// "1.2.3" gibi sürümleri karşılaştırır. Yalnızca sayısal parçalara bakar ("+build" ve "-önek" yok sayılır).
/// current < minimum ise true döner (uygulama güncellenmeli).
bool isVersionLessThan(String current, String minimum) {
  final a = _parts(current);
  final b = _parts(minimum);
  final length = a.length > b.length ? a.length : b.length;
  for (var i = 0; i < length; i++) {
    final x = i < a.length ? a[i] : 0;
    final y = i < b.length ? b[i] : 0;
    if (x != y) return x < y;
  }
  return false;
}

List<int> _parts(String version) => version
    .split(RegExp(r'[+-]'))
    .first
    .split('.')
    .map((p) => int.tryParse(p) ?? 0)
    .toList();

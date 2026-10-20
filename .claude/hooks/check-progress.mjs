// Stop hook: docs/ilerleme.md, projedeki en son değişen dosyadan eskiyse oturumun bitmesini engeller.
// Kural kaynağı: CLAUDE.md 0. madde (yapılan her şey, yapıldığı an ilerleme dosyasına yazılır).
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const root = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const progressFile = path.join(root, 'docs', 'ilerleme.md');

// Derleme/bağımlılık klasörleri kontrol dışı
const IGNORED_DIRS = new Set([
  '.git', 'node_modules', 'bin', 'obj', 'build', 'dist', 'coverage',
  '.dart_tool', '.gradle', '.idea', '.vs', '.next',
]);
// settings.local.json izin onaylarıyla otomatik değişir; ilerleme dosyası kendisiyle kıyaslanmaz
const IGNORED_FILES = new Set([
  path.normalize(path.join(root, '.claude', 'settings.local.json')),
  path.normalize(progressFile),
]);

// Stop hook'u zaten bir kez engellediyse sonsuz döngüye girme
let input = {};
try {
  input = JSON.parse(fs.readFileSync(0, 'utf8') || '{}');
} catch {
  input = {};
}
if (input.stop_hook_active) {
  process.exit(0);
}

function collect(dir, since, out) {
  let entries;
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return;
  }
  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (!IGNORED_DIRS.has(entry.name)) collect(full, since, out);
    } else if (entry.isFile()) {
      if (IGNORED_FILES.has(path.normalize(full))) continue;
      let stat;
      try {
        stat = fs.statSync(full);
      } catch {
        continue;
      }
      if (stat.mtimeMs > since) out.push({ file: path.relative(root, full), mtime: stat.mtimeMs });
    }
  }
}

function block(reason) {
  console.log(JSON.stringify({ decision: 'block', reason }));
  process.exit(0);
}

if (!fs.existsSync(progressFile)) {
  block('docs/ilerleme.md bulunamadı. CLAUDE.md 0. madde gereği ilerleme dosyası oluşturulmalı ve yapılan her şey oraya yazılmalı.');
}

// Kayıp koruması: günlükteki kayıt (### başlığı) sayısı son commit'tekinden az olamaz (günlük append-only)
const head = spawnSync('git', ['show', 'HEAD:docs/ilerleme.md'], { cwd: root, encoding: 'utf8' });
if (head.status === 0) {
  const count = (text) => (text.match(/^### /gm) || []).length;
  const before = count(head.stdout);
  const now = count(fs.readFileSync(progressFile, 'utf8'));
  if (now < before) {
    block(
      `docs/ilerleme.md KISALMIŞ: kayıt sayısı ${before} → ${now}. Günlük yalnızca sonuna eklenir. ` +
        "'git show HEAD:docs/ilerleme.md' ile geri yükle ve kaydı Edit aracıyla ekle.",
    );
  }
}

const progressMtime = fs.statSync(progressFile).mtimeMs;
const newer = [];
collect(root, progressMtime, newer);

if (newer.length > 0) {
  newer.sort((a, b) => b.mtime - a.mtime);
  const list = newer.slice(0, 10).map((n) => `- ${n.file}`).join('\n');
  const more = newer.length > 10 ? `\n... ve ${newer.length - 10} dosya daha` : '';
  block(
    'CLAUDE.md 0. madde (İlerleme kaydı): docs/ilerleme.md, aşağıdaki dosyalardan daha eski. ' +
      'Yapılan her şey (ne, neden, değişen dosyalar, doğrulama, sorun, sıradaki adım) yapıldığı an ilerleme dosyasına yazılmalı. ' +
      'Önce docs/ilerleme.md\'ye eksik kaydı ekle, sonra yanıtı bitir.\n' +
      'İlerleme dosyasından sonra değişen dosyalar:\n' + list + more,
  );
}

process.exit(0);

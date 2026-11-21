import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const html = fs.readFileSync(new URL('../docs/index.html', import.meta.url), 'utf8');
const script = fs.readFileSync(new URL('../docs/release.js', import.meta.url), 'utf8');
// The public website describes the published release while the app may be ahead in development.
const tag = 'v0.3.3';
assert(tag, 'Published release metadata has an explicit version');
const version = tag.slice(1);
const readme = fs.readFileSync(new URL('../README.md', import.meta.url), 'utf8');
const files = ['AutoTyper.exe', 'AutoTyper-arm64.exe', 'AutoTyperCompact.exe', 'AutoTyperCompact-arm64.exe',
  'AutoTyper-Setup-x64.msi', 'AutoTyper-Setup-arm64.msi', 'AutoTyperCompact-Setup-x64.msi', 'AutoTyperCompact-Setup-arm64.msi'];
const assets = files.flatMap(name => [name, `${name}.sha256`]);
const downloads = [...html.matchAll(/href="https:\/\/github.com\/santa512\/AutoTyper\/releases\/download\/([^/]+)\/([^"?#]+)"/g)];
assert(downloads.length >= 4, 'Both editions and checksums have direct download links');
for (const [, actualTag, asset] of downloads) {
  const expected = /^AutoTyper(?:\.exe|\.exe\.sha256|-Setup-x64\.msi(?:\.sha256)?)$/.test(asset) ? tag : 'v0.2.2';
  assert.equal(actualTag, expected, 'Main x64 is current; legacy editions stay on v0.2.2');
  assert(assets.includes(asset), `Known release asset: ${asset}`);
}
for (const asset of assets) {
  assert(downloads.some(([, , name]) => name === asset), `Page links to ${asset}`);
  const expected = ['AutoTyper.exe', 'AutoTyper.exe.sha256', 'AutoTyper-Setup-x64.msi', 'AutoTyper-Setup-x64.msi.sha256'].includes(asset) ? tag : 'v0.2.2';
  assert(readme.includes(`/releases/download/${expected}/${asset}`), `README links to ${asset}`);
}
assert(html.includes(`VERSION ${version}`) && html.includes(`Download ${tag}`), 'Visible page version and title are current');
assert(/Groq/.test(html) && /live captions/i.test(html) && /voice preview/i.test(html), 'Current features are documented');
assert(!/\bWinUI\b|\bWin32\b/i.test(html), 'Public page uses edition names');
assert(html.indexOf('/AutoTyper.exe"') < html.indexOf('/AutoTyperCompact.exe"'), 'Main app is the default download');
assert(/14 days/.test(html) && /tray/i.test(html) && /English/.test(html), 'Current features are documented');
assert(/Microsoft Translator/.test(html) && /encrypted backup/.test(html) && /Six global shortcuts/.test(html), 'Translation, backup and sixth hotkey are documented');
assert(/ARM64/.test(html) && /Run on startup/.test(html) && /Setup MSI/.test(html), 'Architectures, startup and setup downloads are documented');
assert(!/\b(?:MiB|KiB)\b/.test(html + readme), 'Public download sizes use decimal units');
assert(html.includes('155.7 MB') && html.includes('238.6 KB'), 'Offline sizes match the published assets');

async function metadataCase(failedTag) {
  const labels = { 'AutoTyper.exe': { textContent: 'fallback' }, 'AutoTyperCompact.exe': { textContent: 'fallback' } };
  const requested = [];
  vm.runInNewContext(script, {
    fetch: async url => {
      requested.push(url);
      if (url.endsWith('/' + failedTag)) throw new Error('offline');
      return { ok: true, json: async () => ({ assets: [
        ...(url.endsWith('/' + tag) ? [{ name: 'AutoTyper.exe', size: 155688178 }] : [{ name: 'AutoTyperCompact.exe', size: 238592 }])
      ] }) };
    },
    document: { querySelectorAll: selector => {
      const name = selector.match(/data-download-size="([^"]+)"/)[1];
      assert(name in labels);
      return [labels[name]];
    } }
  });
  await new Promise(setImmediate);
  assert.deepEqual(requested.sort(), ['v0.2.2', tag].map(t => `https://api.github.com/repos/santa512/AutoTyper/releases/tags/${t}`).sort());
  assert.equal(labels['AutoTyper.exe'].textContent, failedTag === tag ? 'fallback' : '155.7 MB');
  assert.equal(labels['AutoTyperCompact.exe'].textContent, failedTag === 'v0.2.2' ? 'fallback' : '238.6 KB');
}
await metadataCase(null);
await metadataCase(tag);
await metadataCase('v0.2.2');
console.log(`PASS: ${tag} download links, recommended edition, metadata sizes and offline fallback.`);

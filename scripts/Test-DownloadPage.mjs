import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const project = fs.readFileSync(new URL('../AutoTyper.csproj', import.meta.url), 'utf8');
const version = project.match(/<InformationalVersion>([^<]+)<\/InformationalVersion>/)[1];
const tag = `v${version}`;
const html = fs.readFileSync(new URL('../docs/index.html', import.meta.url), 'utf8');
const script = fs.readFileSync(new URL('../docs/release.js', import.meta.url), 'utf8');
const readme = fs.readFileSync(new URL('../README.md', import.meta.url), 'utf8');
const assets = ['AutoTyper.exe', 'AutoTyper.exe.sha256', 'AutoTyperCompact.exe', 'AutoTyperCompact.exe.sha256'];
const downloads = [...html.matchAll(/href="https:\/\/github.com\/santa512\/AutoTyper\/releases\/download\/([^/]+)\/([^"?#]+)"/g)];
assert(downloads.length >= 4, 'Both editions and checksums have direct download links');
for (const [, actualTag, asset] of downloads) {
  assert.equal(actualTag, tag, 'Page download version matches the app');
  assert(assets.includes(asset), `Known release asset: ${asset}`);
}
for (const asset of assets) {
  assert(downloads.some(([, , name]) => name === asset), `Page links to ${asset}`);
  assert(readme.includes(`/releases/download/${tag}/${asset}`), `README links to ${asset}`);
}
assert(html.includes(`VERSION ${version}`) && html.includes(`Download ${tag}`), 'Visible page version and title are current');
assert(!/\bWinUI\b|\bWin32\b/i.test(html), 'Public page uses edition names');
assert(html.indexOf('/AutoTyper.exe"') < html.indexOf('/AutoTyperCompact.exe"'), 'Main app is the default download');
assert(/14 days/.test(html) && /tray/i.test(html) && /English/.test(html), 'Current features are documented');
assert(/Microsoft Translator/.test(html) && /encrypted backup/.test(html) && /Six global shortcuts/.test(html), 'Translation, backup and sixth hotkey are documented');
assert(!/\b(?:MiB|KiB)\b/.test(html + readme), 'Public download sizes use decimal units');
assert(html.includes('114.1 MB') && html.includes('238.6 KB'), 'Offline sizes match the published assets');

async function metadataCase(fail) {
  const labels = { 'AutoTyper.exe': { textContent: 'fallback' }, 'AutoTyperCompact.exe': { textContent: 'fallback' } };
  let requested;
  vm.runInNewContext(script, {
    fetch: async url => {
      requested = url;
      if (fail) throw new Error('offline');
      return { ok: true, json: async () => ({ assets: [
        { name: 'AutoTyper.exe', size: 114075145 }, { name: 'AutoTyperCompact.exe', size: 238592 }
      ] }) };
    },
    document: { querySelectorAll: selector => {
      const name = selector.match(/data-download-size="([^"]+)"/)[1];
      assert(name in labels);
      return [labels[name]];
    } }
  });
  await new Promise(setImmediate);
  assert.equal(requested, `https://api.github.com/repos/santa512/AutoTyper/releases/tags/${tag}`);
  assert.equal(labels['AutoTyper.exe'].textContent, fail ? 'fallback' : '114.1 MB');
  assert.equal(labels['AutoTyperCompact.exe'].textContent, fail ? 'fallback' : '238.6 KB');
}
await metadataCase(false);
await metadataCase(true);
console.log(`PASS: ${tag} download links, recommended edition, metadata sizes and offline fallback.`);

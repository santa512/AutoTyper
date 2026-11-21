// Direct links and static sizes remain usable without this enhancement.
for (const [tag, names] of [
  ['v0.3.3', ['AutoTyper.exe']],
  ['v0.2.2', ['AutoTyperCompact.exe']]
]) {
  fetch(`https://api.github.com/repos/santa512/AutoTyper/releases/tags/${tag}`, {
    headers: { Accept: 'application/vnd.github+json' }
  }).then(response => response.ok ? response.json() : null).then(release => {
    for (const name of names) {
      const asset = release?.assets?.find(asset => asset.name === name);
      if (!(asset?.size > 0)) continue;
      const small = asset.size < 1000000;
      for (const label of document.querySelectorAll(`[data-download-size="${name}"]`)) {
        label.textContent = `${(asset.size / (small ? 1000 : 1000000)).toFixed(1)} ${small ? 'KB' : 'MB'}`;
      }
    }
  }).catch(() => { /* Preserve the static size if this release cannot be fetched. */ });
}

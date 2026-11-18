// Optional metadata enhancement; direct download links work without JavaScript.
fetch('https://api.github.com/repos/santa512/AutoTyper/releases/tags/v0.2', {
  headers: { Accept: 'application/vnd.github+json' }
}).then(response => response.ok ? response.json() : null).then(release => {
  for (const filename of ['AutoTyper.exe', 'AutoTyperCompact.exe']) {
    const executable = release?.assets?.find(asset => asset.name === filename);
    if (executable?.size > 0) {
      const small = executable.size < 1000000;
      for (const label of document.querySelectorAll(`[data-download-size="${filename}"]`)) {
        label.textContent = `${(executable.size / (small ? 1000 : 1000000)).toFixed(1)} ${small ? 'KB' : 'MB'}`;
      }
    }
  }
}).catch(() => { /* Keep the static size estimate when offline or rate limited. */ });

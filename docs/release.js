// Optional metadata enhancement; direct download links work without JavaScript.
fetch('https://api.github.com/repos/santa512/AutoTyper/releases/tags/v0.1', {
  headers: { Accept: 'application/vnd.github+json' }
}).then(response => response.ok ? response.json() : null).then(release => {
  const executable = release?.assets?.find(asset => asset.name === 'AutoTyper.exe');
  if (executable?.size > 0) {
    document.getElementById('download-size').textContent = `${(executable.size / 1048576).toFixed(1)} MB`;
  }
}).catch(() => { /* Keep the static size estimate when offline or rate limited. */ });

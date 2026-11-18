// Optional metadata enhancement; direct download links work without JavaScript.
fetch('https://api.github.com/repos/santa512/AutoTyper/releases/tags/v0.1', {
  headers: { Accept: 'application/vnd.github+json' }
}).then(response => response.ok ? response.json() : null).then(release => {
  for (const [filename, elementId] of [['AutoTyper.exe', 'download-size'], ['AutoTyperCompact.exe', 'compact-size']]) {
    const executable = release?.assets?.find(asset => asset.name === filename);
    const label = document.getElementById(elementId);
    if (label && executable?.size > 0) {
      const small = executable.size < 1048576;
      label.textContent = `${(executable.size / (small ? 1024 : 1048576)).toFixed(1)} ${small ? 'KB' : 'MB'}`;
    }
  }
}).catch(() => { /* Keep the static size estimate when offline or rate limited. */ });

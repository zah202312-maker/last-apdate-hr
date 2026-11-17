const { execFileSync } = require('child_process');
const path = require('path');
const chrome = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const roles = process.argv.slice(2);
for (const r of roles) {
  const { file, pages } = require('./' + r)();
  const html = path.join(__dirname, file);
  const pdf = path.join(__dirname, '..', file.replace('.html', '.pdf'));
  execFileSync(chrome, ['--headless=new', '--disable-gpu', '--no-pdf-header-footer', '--virtual-time-budget=15000', '--print-to-pdf=' + pdf, 'file:///' + html.split(path.sep).join('/')], { stdio: 'ignore' });
  console.log(r, pages, 'pages ->', pdf);
}

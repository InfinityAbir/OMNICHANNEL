// Minimal static file server used by the E2E to host the "customer site" HTML on its own origin
// (http://localhost:5173), separate from the product API (http://localhost:5068). This makes the
// widget embed truly cross-origin so the browser sends an Origin header, exercising real origin
// validation. Serves the self-hosted widget dir only; no other routes.
const http = require('http');
const fs = require('fs');
const path = require('path');

const ROOT = path.join(__dirname, '..', 'src', 'Omnichannel.Api', 'wwwroot', 'widget');
const PORT = process.env.PORT || 5173;
const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.map': 'application/json',
};

http
  .createServer((req, res) => {
    const urlPath = decodeURIComponent((req.url || '/').split('?')[0]);
    const safe = path.normalize(urlPath).replace(/^(\.\.[/\\])+/, '');
    let file = path.join(ROOT, safe === '/' ? 'customer-demo.html' : safe);

    // Redirect a bare directory to the demo page.
    if (fs.existsSync(file) && fs.statSync(file).isDirectory()) {
      file = path.join(file, 'customer-demo.html');
    }

    if (!file.startsWith(ROOT) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
      res.writeHead(404, { 'Content-Type': 'text/plain' });
      res.end('Not found');
      return;
    }

    res.writeHead(200, { 'Content-Type': MIME[path.extname(file)] || 'application/octet-stream' });
    fs.createReadStream(file).pipe(res);
  })
  .listen(PORT, () => {
    console.log(`Widget demo site listening on http://localhost:${PORT}`);
  });

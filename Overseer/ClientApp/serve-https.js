// Serves the client over HTTPS on port 44447 with the ASP.NET Core development certificate.
// The certificate is exported as PEM next to the one Visual Studio's SPA templates use, outside
// the repository, the first time it is missing. Extra arguments are passed to `ng serve`.
const { spawn, spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const certDir = process.env.APPDATA
  ? path.join(process.env.APPDATA, 'ASP.NET', 'https')
  : path.join(process.env.HOME || '', '.aspnet', 'https');
const name = process.env.npm_package_name || 'overseer';
const certFile = path.join(certDir, `${name}.pem`);
const keyFile = path.join(certDir, `${name}.key`);

if (!fs.existsSync(certFile) || !fs.existsSync(keyFile)) {
  fs.mkdirSync(certDir, { recursive: true });
  const exported = spawnSync('dotnet',
    ['dev-certs', 'https', '--export-path', certFile, '--format', 'Pem', '--no-password'],
    { stdio: 'inherit' });
  if (exported.status !== 0) {
    process.exit(exported.status ?? 1);
  }
}

const ng = path.join(__dirname, 'node_modules', '@angular', 'cli', 'bin', 'ng.js');
const server = spawn(process.execPath,
  [ng, 'serve', '--port', '44447', '--ssl', '--ssl-cert', certFile, '--ssl-key', keyFile,
    ...process.argv.slice(2)],
  { stdio: 'inherit' });
server.on('exit', code => process.exit(code ?? 0));

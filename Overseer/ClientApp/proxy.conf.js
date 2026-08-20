const { env } = require('process');

const target = env.ASPNETCORE_HTTPS_PORT ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}` :
  env.ASPNETCORE_URLS ? env.ASPNETCORE_URLS.split(';')[0] : 'https://localhost:7214';

const PROXY_CONFIG = [
  {
    context: [
      "/api",
      "/chathub"
    ],
    target: target,
    secure: false,
    ws: true,
    changeOrigin: true
  }
];

module.exports = PROXY_CONFIG;

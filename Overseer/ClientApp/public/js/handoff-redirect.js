/* Fast path for the game-client handoff page (AuthController.Handoff).

   The page's own <meta http-equiv="refresh"> performs the redirect; this only shortens the
   wait. It is a static file rather than an inline script because the CSP's script-src is
   'self'. The session id and the host's game state arrive in data attributes, so nothing is
   interpolated into the script and the file can be cached. */
(function () {
  var el = document.currentScript;
  if (!el) { return; }
  var sessionId = el.getAttribute('data-session-id');
  if (!sessionId || !/^[0-9]+$/.test(sessionId)) { return; }
  var gameOn = el.getAttribute('data-game-on');
  var target = '/chat?sessionId=' + sessionId;
  if (gameOn === '0' || gameOn === '1') { target += '&gameOn=' + gameOn; }
  setTimeout(function () {
    window.location.replace(target);
  }, 50);
})();

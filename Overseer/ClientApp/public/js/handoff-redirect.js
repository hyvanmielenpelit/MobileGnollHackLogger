/* Fast path for the game-client handoff page (AuthController.Handoff).

   The page's own <meta http-equiv="refresh"> performs the redirect; this only shortens the
   wait. It is a static file rather than an inline script because the CSP's script-src is
   'self'. The session id arrives in a data attribute, so nothing is interpolated into the
   script and the file can be cached. */
(function () {
  var el = document.currentScript;
  if (!el) { return; }
  var sessionId = el.getAttribute('data-session-id');
  if (!sessionId || !/^[0-9]+$/.test(sessionId)) { return; }
  setTimeout(function () {
    window.location.replace('/chat?sessionId=' + sessionId);
  }, 50);
})();

// Ticks BenchmarkPollTickerService's run and series pollers from outside the main thread, so a
// hidden, occluded or minimized tab's usual setInterval throttling (browsers cap it to about once
// a minute) does not slow the poll down. Started and stopped once per poller; nothing here is
// shared across instances, and this script has no imports and never evaluates anything dynamic.
let intervalId = null;

self.onmessage = function (event) {
  const data = event.data || {};
  if (data.type === 'start') {
    if (intervalId !== null) {
      clearInterval(intervalId);
    }
    intervalId = setInterval(function () {
      self.postMessage({ type: 'tick' });
    }, data.intervalMs);
  } else if (data.type === 'stop') {
    if (intervalId !== null) {
      clearInterval(intervalId);
      intervalId = null;
    }
  }
};

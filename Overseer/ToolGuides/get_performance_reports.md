Access the in-game performance test reports stored on the player's device.

The player creates a report in a game with Menu > Developer > Test Performance.
The app measures the game map for 30 seconds and checks everything that can
lower the frame rate: heat and throttling, battery saver and power mode,
background processes (Windows Update, antivirus, indexer, sync, builds), memory
pressure, CPU instead of GPU rendering, the wrong GPU on hybrid laptops, and
display refresh. Each report says where the cause lies: external (outside the
game), configuration (device or game settings), inside the game (this build,
the level's content, or a game setting), unclear, or none.

This tool has two modes:

1. LIST MODE (no filename): Returns the reports on the device, newest first,
   one per line: filename | Result line | Location line. Use it when the player
   asks about stutter, low FPS, jitter, or "why is the game slow".

2. READ MODE (filename specified): Returns the full text of one report.
   Sections, in order: Result, Location, Most likely cause, Also found, Checks,
   Measurements, Hitch causes, Notes, Environment, Facts, Frame detail.

How to use the reports:
- Lead with the report's own verdict and advice; explain it in plain words.
- To compare reports (e.g. before and after an app update), read both and
  compare their Environment sections (code.appVersion, code.gitCommit,
  driver.*, os.*, settings.*) and their Facts blocks (key=value lines,
  "n/a" = not measured). A slowdown that appears together with only a
  code.* change points to the new build; one that appears with only
  driver/os/settings changes, or with external findings, points elsewhere.
- One report is a single 30-second test of whatever map was on screen:
  indicative, not proof. Suggest another test if the result is unclear.

Content is cut to `max_length` characters (default 12000). Do not pass more
than 15000: longer results are cut again, and the report's own truncation note
is lost. The Facts block comes before the long Frame detail appendix, so the
default keeps it. When comparing several reports, keep the default.

If the tool fails with "Unknown tool: get_performance_reports", the player's
GnollHack app is older than this feature: tell them to update the app, and do
not retry. If it fails with an error saying it timed out, the app is too old
for client tools or did not respond: suggest updating the app and trying
again. If it fails with "Client bridge not available", the chat is not running
inside a GnollHack app that can read device files (for example, it is open in
a web browser): tell the player to ask again from the GnollHack app.

This tool requires client data to be enabled.

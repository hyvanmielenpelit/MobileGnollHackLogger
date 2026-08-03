Reads the GnollHack panic log (paniclog) from the player's device.

Contains entries written by the C game engine when a fatal error (panic) occurs. Each entry includes a timestamp and the panic message describing the crash cause.

The file may not exist if no panics have occurred — in that case the tool returns a message indicating no panic log was found. Panic logs are typically short.

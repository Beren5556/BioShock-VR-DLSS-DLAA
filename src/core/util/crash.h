#pragma once

namespace bvr::crash {

// Installs an unhandled-exception filter that writes a minidump to
// %LOCALAPPDATA%\BioshockVR\crash\ and then chains to the previous filter
// (the game installs its own dbghelp-based handler), plus a vectored handler
// for the always-fatal codes that bypass the filter entirely.
void install();

// Re-installs our unhandled-exception filter if something else displaced it.
// SetUnhandledExceptionFilter is global last-writer-wins, and we install at DLL
// attach - the game's own handler, the Steam overlay and the 2K SDK all install
// later. Session 23: an external tester's crash produced NO dump and NO log
// line, which is exactly what a displaced filter looks like. Cheap; call from
// the present loop. Logs once when it actually had to re-arm.
void rearm();

// WM_CLOSE is only a request: BS2 may confirm asynchronously or cancel it.
// For BS2 this is informational only: no watchdog, persistent engine gate, or
// exception suppression. Other hosts retain their legacy teardown-on-request.
void note_close_request(const char* why);

// The main window is being destroyed, or the OS session is ending. BS2 callers
// must not use this for a cancelable close request. Idempotently parks adapter
// machinery and starts the shutdown watchdog. BS2 faults still get a report
// and retain their real failure code; a watchdog expiry reports WAIT_TIMEOUT.
void note_teardown(const char* why);

// True once teardown is confirmed (BS2), or legacy teardown has been noted
// (other hosts). Cheap atomic read; adapters park engine work on this signal.
bool teardown_seen();

} // namespace bvr::crash

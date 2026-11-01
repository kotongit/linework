# Performance

Measured 1 Nov 2026 on a generated library of 2,000 photos, using the debug tool `--perf` (debug builds are slower than the shipped app).

| Work | Time |
|---|---|
| Open the library | 0.007 s |
| Finder check, nothing changed | 0.13 s, off the main thread |
| Sort visible photos | under 0.001 s |
| Save `photos.json` | 0.008 s |
| Tick summary for the calendar | under 0.001 s |
| Build a session queue | 0.007 s |

No change was justified by these numbers, apart from moving the main menu's one-second clock tick away from plan values. Grid scrolling can't be measured offscreen, so it's on the live check list.

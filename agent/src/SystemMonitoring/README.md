# System Monitoring

Windows PC monitoring adapter for Stream Deck agent section 21.

Use `ISystemMetricsProvider.GetSnapshot()` at roughly a 1 second interval. CPU usage is calculated from `GetSystemTimes` deltas, so the provider stores a baseline and the first immediate sample can report `0` until a later sample has elapsed time.

Always available through Win32/BCL:

- CPU usage
- RAM total and used
- uptime

Reported as `null`/N/A by this implementation because they require a separate safe provider such as a vendor SDK, PDH counter set, or hardware monitor service:

- GPU usage
- VRAM
- CPU temperature
- GPU temperature

This module intentionally does not use kernel drivers, unsafe hardware probing, or external dependencies.

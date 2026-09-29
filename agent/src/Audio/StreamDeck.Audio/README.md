# StreamDeck.Audio

`StreamDeck.Audio` exposes Windows system master volume state for the Stream Deck agent.

## Data

- `volume`: integer `0..100`
- `mute`: `true` / `false`

## Native API

The production endpoint uses Windows Core Audio endpoint volume COM APIs:

- `IMMDeviceEnumerator`
- default render endpoint, multimedia role
- `IAudioEndpointVolume`
- `IAudioEndpointVolumeCallback`

No player-specific API and no NuGet package is used by the library.

## Synchronization

`WindowsMasterVolumeService` subscribes to native endpoint change events. Changes caused by Windows, keyboard media keys or other devices are published through `AudioChanged`, so a later networking layer can forward an `audio.update` message to ESP32.

## Commands

- `SetVolume(int volumePercent)` clamps to `0..100`, writes master volume and refreshes current state.
- `ToggleMute()` flips current mute state and refreshes current state.
- `SetMute(bool)` is available for host UI and tests.

Failures from the native endpoint return `false` from service methods instead of crashing the agent. Disposal unregisters native callbacks.

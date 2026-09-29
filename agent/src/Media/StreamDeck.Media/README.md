# StreamDeck.Media

`StreamDeck.Media` is an isolated Windows media-session library for the Stream Deck agent. It uses Windows Global System Media Transport Controls from `Windows.Media.Control`; it does not use the Spotify API or any player-specific integration.

## App model and capabilities

- API entrypoint: `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()`.
- Required Windows app capability: `globalMediaControl`.
- Minimum Windows contract used by this module: Windows 10 version 1809 / UniversalApiContract v7 (`10.0.17763.0`).
- The host application must run on Windows and use a Windows-targeted .NET TFM.

## Event model

The monitor is event-driven. It subscribes to:

- manager `SessionsChanged`;
- session `MediaPropertiesChanged`;
- session `PlaybackInfoChanged`;
- session `TimelinePropertiesChanged`.

Snapshots are refreshed when one of these events arrives or when `StartAsync` initializes the monitor. The module does not run a tight polling loop.

## Data exposed

`MediaSnapshot` contains source app id, title, artist, album, playback state, timeline values and an optional thumbnail reference. The thumbnail is intentionally exposed as `IMediaThumbnailReference` so the Covers module can decide later when and how to open/cache the stream.

## Commands

The monitor exposes previous, play, pause, toggle play/pause and next. Commands return `false` when no session is selected or the native Windows command fails/throws. Cancellation still propagates as `OperationCanceledException`.

## Limitations

- Windows reports only sessions that publish Global System Media Transport Controls.
- Command success depends on the source app accepting the command.
- Metadata quality and thumbnail availability are controlled by the source app.
- Session selection defaults to Windows' current session, then a playing session, then the first available session.

# StreamDeck.Covers

PC-side cover pipeline for media thumbnails:

1. accepts a generic `Stream` from the future media layer,
2. hashes the source stream for cache lookup,
3. center-crops to a square,
4. resizes to 140x140,
5. writes JPEG quality 80,
6. uses the SHA-256 of the final JPEG bytes as `coverId`,
7. stores `{coverId}.jpg` on local disk for the later HTTP endpoint.

The implementation uses `SkiaSharp` 4.153.1 (MIT). It keeps the public API free of WinRT and HTTP concerns, works directly from streams, avoids buffering the full input bytes in memory, and writes non-seekable inputs to a temporary file while hashing them. SkiaSharp decodes one image at a time, then center-crops/resizes and encodes the small final JPEG. Native runtime assets are selected by the consuming target; the Windows Agent publish includes `SkiaSharp.NativeAssets.Win32`.

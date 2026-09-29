# StreamDeck.Configuration

`StreamDeck.Configuration` owns the public agent `config.json`.

Defaults:

- `server.port`: `8765`
- `monitoring.intervalMs`: `1000`
- `covers.size`: `140`
- `covers.jpegQuality`: `80`
- `shortcuts`: `[]`

Secrets and pairing tokens must not be stored in this JSON file. The production factory stores the file under the current user's LocalAppData folder.

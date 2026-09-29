namespace StreamDeck.WindowsAgent;

internal static class DashboardHtml
{
    public const string Content = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Stream Deck Agent</title>
  <style>
    :root { color-scheme: light dark; font-family: "Segoe UI", system-ui, sans-serif; }
    body { margin: 0; background: #101418; color: #eef3f7; }
    main { max-width: 860px; margin: 0 auto; padding: 28px; }
    h1 { font-size: 24px; margin: 0 0 20px; }
    section { border: 1px solid #2f3b46; border-radius: 8px; padding: 18px; margin-bottom: 16px; background: #171d23; }
    dl { display: grid; grid-template-columns: 150px 1fr; gap: 8px 16px; margin: 0; }
    dt { color: #9fb0bf; }
    dd { margin: 0; word-break: break-word; }
    table { width: 100%; border-collapse: collapse; }
    th, td { text-align: left; padding: 10px; border-bottom: 1px solid #2f3b46; }
    .empty { color: #9fb0bf; }
  </style>
</head>
<body>
  <main>
    <h1>Stream Deck Agent</h1>
    <section>
      <dl id="status"></dl>
    </section>
  </main>
  <script>
    async function json(url) {
      const response = await fetch(url, { cache: "no-store" });
      if (!response.ok) throw new Error(response.statusText);
      return response.json();
    }
    function text(value) {
      return value == null || value === "" ? "none" : String(value);
    }
    function setStatus(status) {
      const root = document.getElementById("status");
      root.replaceChildren();
      for (const [label, value] of [
        ["Host", status.hostStatus],
        ["Mode", status.mode],
        ["Device", `${text(status.deviceName)}${status.deviceId ? " (" + status.deviceId + ")" : ""}`],
        ["Device address", status.remoteAddress],
        ["CYD WebSocket", `ws://<PC-LAN-IP>:${status.hostPort}/ws`],
        ["Dashboard", `http://127.0.0.1:${status.dashboardPort}/`],
        ["LAN IPs", status.lanAddresses.length ? status.lanAddresses.join(", ") : "none"]
      ]) {
        const dt = document.createElement("dt");
        dt.textContent = label;
        const dd = document.createElement("dd");
        dd.textContent = text(value);
        root.append(dt, dd);
      }
    }
    async function refresh() {
      setStatus(await json("/api/status"));
    }
    refresh();
    setInterval(refresh, 3000);
  </script>
</body>
</html>
""";
}

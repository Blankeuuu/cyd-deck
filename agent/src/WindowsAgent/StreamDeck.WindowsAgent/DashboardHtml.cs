namespace StreamDeck.WindowsAgent;

internal static class DashboardHtml
{
    public const string Content = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>CYD-DECK Agent</title>
  <style>
    :root {
      color-scheme: dark;
      font-family: -apple-system, BlinkMacSystemFont, "SF Pro Display", "SF Pro Text", "Inter", "Segoe UI", system-ui, sans-serif;
    }
    body {
      margin: 0;
      background: #0D0E11;
      color: #F4F6F8;
      -webkit-font-smoothing: antialiased;
    }
    main {
      max-width: 880px;
      margin: 0 auto;
      padding: 40px 28px;
    }
    header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 24px;
    }
    h1 {
      font-size: 26px;
      font-weight: 700;
      letter-spacing: -0.02em;
      margin: 0;
    }
    .badge {
      font-size: 12px;
      font-weight: 600;
      padding: 5px 12px;
      border-radius: 999px;
      background: rgba(10, 132, 255, 0.16);
      color: #5299E0;
      border: 1px solid rgba(82, 153, 224, 0.28);
    }
    section {
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 14px;
      padding: 22px 24px;
      margin-bottom: 18px;
      background: rgba(30, 32, 40, 0.78);
      backdrop-filter: blur(20px);
      box-shadow: 0 12px 32px rgba(0, 0, 0, 0.32);
    }
    dl {
      display: grid;
      grid-template-columns: 168px 1fr;
      gap: 12px 20px;
      margin: 0;
      font-size: 14px;
    }
    dt {
      color: #9CA4B0;
      font-weight: 500;
    }
    dd {
      margin: 0;
      color: #F4F6F8;
      font-weight: 500;
      word-break: break-word;
    }
  </style>
</head>
<body>
  <main>
    <header>
      <h1>CYD-DECK Agent</h1>
      <span class="badge">macOS / Windows Control Center</span>
    </header>
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


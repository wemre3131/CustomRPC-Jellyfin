# 🎮 Kavasaki Presence

**Custom Rich Presence Jellyfin made by Kavasaki**

Shows what you're watching on Discord — with IMDB ratings and links, all without any API key!

---

## ✨ Features

- 🎬 Shows movie/show title, year, and media type on Discord
- ⭐ IMDB rating fetched automatically — **no API key required**
- 🔗 Clickable IMDB button in Discord
- 📺 TV episode info (S01E04 - Episode Name)
- ⏱️ Elapsed time timestamp
- 🎛️ Every feature individually toggleable
- 🎨 Beautiful config page in Jellyfin admin dashboard

---

## 📦 Installation

### Option A: Manual (Recommended)
1. Build the project:
   ```bash
   dotnet publish -c Release
   ```
2. Copy the output DLL to your Jellyfin plugins folder:
   - **Linux**: `~/.local/share/jellyfin/plugins/KavasakiPresence/`
   - **Windows**: `%AppData%\Jellyfin\plugins\KavasakiPresence\`
   - **Docker**: `/config/plugins/KavasakiPresence/`
3. Restart Jellyfin
4. Go to **Dashboard → Plugins → Kavasaki Presence** to configure

### Option B: Plugin Repository
Add this URL to Jellyfin plugin repositories:
```
https://raw.githubusercontent.com/wemre3131/CustomRPC-Jellyfin/refs/heads/main/manifest.json
```

---

## 🔑 Getting Your Discord Token

> ⚠️ **Warning**: Your user token is like a password. Never share it!

1. Open Discord in your browser or desktop app
2. Press `F12` to open Developer Tools
3. Go to the **Network** tab
4. Type `science` in the filter box
5. Click any request and look at the **Request Headers**
6. Find the `Authorization` header — that's your token

---

## ⚙️ Configuration

| Setting | Description |
|---|---|
| Discord Token | Your Discord user token |
| Discord App Client ID | From Discord Developer Portal (for custom images) |
| Enable Plugin | Master switch |
| Show Title | Movie/show name in presence |
| Show Year | Production year |
| Show IMDB Rating | ⭐ 8.3/10 — fetched without API key |
| IMDB Button | Clickable IMDB link in Discord |
| Show Episode Info | S01E04 - Episode Name for TV |
| Show Timestamp | Elapsed time counter |
| Jellyfin Button | "Watch on Jellyfin" button (needs public URL) |

---

## 🔧 How IMDB Works (No API Key!)

The plugin uses IMDB's public suggestion API and HTML scraping:

1. **Jellyfin metadata first**: If Jellyfin already has the IMDB ID (from your library scan), it uses that directly
2. **Search fallback**: If not, it searches `v3.sg.media-imdb.com/suggestion/` (IMDB's own autocomplete)
3. **Rating extraction**: Fetches the IMDB page and extracts the `ratingValue` from JSON-LD structured data

No third-party APIs, no keys needed. 🎉

---

## 🏗️ Building from Source

```bash
git clone https://github.com/wemre3131/CustomRPC-Jellyfin
cd CustomRPC-Jellyfin
dotnet build -c Release
```

**Requirements**: .NET 8.0 SDK, Jellyfin 10.9.x

---

## 📜 License

MIT — Made with ❤️ by Kavasaki

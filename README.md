# Book Notifier

![License](https://img.shields.io/github/license/RiisDev/BookNotifier)
![Last Commit](https://img.shields.io/github/last-commit/RiisDev/BookNotifier)
![Build](https://img.shields.io/github/actions/workflow/status/RiisDev/BookNotifier/docker-publish.yml?label=docker%20build)
![Language](https://img.shields.io/github/languages/top/RiisDev/BookNotifier)
![Image Tag](https://ghcr-badge.egpl.dev/riisdev/book-notifier/latest_tag?label=latest)
![Image Size](https://ghcr-badge.egpl.dev/riisdev/book-notifier/size)

A single self-hosted notifier that monitors your reading lists across multiple fiction platforms and sends Discord notifications when new books or chapters are released.

Supported platforms:

- [GoodReads](https://www.goodreads.com) — new books and series entries from authors on your shelf
- [ScribbleHub](https://www.scribblehub.com) — new chapters on stories in your reading list
- [Literotica](https://www.literotica.com) — new stories from your favourite authors

One container, one `.env` file. Run one notifier or all three concurrently.

---

## 📦 Requirements

- Docker installed
- Accounts on whichever platforms you want to monitor
- A Discord Webhook URL

---

## 🚀 Setup

### 1. Pull the Image

```bash
docker pull ghcr.io/riisdev/book-notifier:latest
```

### 2. Create a `.env` File

Set `NOTIFIER` to a comma-separated list of the platforms you want to monitor. Only include variables for the platforms you are running.

```env
# Which notifiers to run (comma-separated, any combination)
NOTIFIER=goodreads,scribblehub,literotica

# Shared
WEBHOOK=https://your.webhook.url/here

# ---- GoodReads ----
GOODREADS_RECHECK_MS=300000
USER_ID=your_goodreads_user_id
SHELF_TAG=your_shelf_name

# ---- ScribbleHub ----
SCRIBBLEHUB_RECHECK_MS=60000
USERNAME=your_scribblehub_username
PASSWORD=your_scribblehub_password
PRESET_COOKIE=your_session_cookie  # fallback if login hits a CAPTCHA

# ---- Literotica ----
LITEROTICA_RECHECK_MS=600000
LIT_USERNAME=your_literotica_username
LIT_PASSWORD=your_literotica_password
```

### 3. Run with Docker

```bash
docker run -d \
  --name book-notifier \
  --env-file .env \
  -v $(pwd)/data:/app/data \
  ghcr.io/riisdev/book-notifier:latest
```

Or build from source:

```bash
git clone https://github.com/riisdev/BookNotifier.git
cd BookNotifier
docker build -t book-notifier ./BookNotifier
docker run -d \
  --name book-notifier \
  --env-file .env \
  -v $(pwd)/data:/app/data \
  book-notifier
```

---

## 🛠 Configuration

### Shared

| Variable   | Description                                                                                       |
| ---------- | ---------------------------------------------                                                     |
| `NOTIFIER` | Comma-separated list of notifiers to run. Valid values: `goodreads`, `scribblehub`, `literotica`  |
| `WEBHOOK`  | Discord webhook URL where notifications will be sent                                              |

### GoodReads

| Variable               | Description                                                   |
| ---------------------- | ------------------------------------------------------------- |
| `GOODREADS_RECHECK_MS` | Interval in milliseconds between checks                       |
| `USER_ID`              | Your GoodReads user ID — found in your profile URL            |
| `SHELF_TAG`            | Shelf to watch (e.g. `to-read`, `currently-reading`)          |

### ScribbleHub

| Variable                | Description                                                                 |
| ----------------------- | ------------------------------------------------------------------------    |
| `SCRIBBLEHUB_RECHECK_MS`| Interval in milliseconds between checks                                     |
| `USERNAME`              | Your ScribbleHub username                                                   |
| `PASSWORD`              | Your ScribbleHub password                                                   |
| `PRESET_COOKIE`         | Pre-authenticated session cookie — used as fallback if login hits a CAPTCHA |

### Literotica

| Variable                | Description                             |
| ----------------------- | --------------------------------------- |
| `LITEROTICA_RECHECK_MS` | Interval in milliseconds between checks |
| `LIT_USERNAME`          | Your Literotica username                |
| `LIT_PASSWORD`          | Your Literotica password                |

### Volume

| Volume      | Description                                                        |
| ----------- | ------------------------------------------------------------------ |
| `/app/data` | Persistent cache used to detect new content across restarts        |

---

## 📝 How It Works

Each enabled notifier runs concurrently in its own loop on its own interval. They do not block or affect each other.

**GoodReads** fetches your shelf, collects all distinct authors, retrieves their full book lists, and checks for new standalone books and series entries against the local cache. A fresh HTTP client is created each cycle to avoid caching issues.

**ScribbleHub** logs in (falling back to `PRESET_COOKIE` on failure), fetches your reading list and each story's full table of contents, then compares the latest chapter against the cache. A fresh HTTP client is created each cycle to avoid Cloudflare and cookie issues.

**Literotica** logs in once and reuses the session across cycles, checking for new stories from your favourite authors each interval.

---

## 🔔 Notifications

| Notifier     | Triggers                                        |
| ------------ | ---------------------------------------------   |
| GoodReads    | New book by a tracked author; new series entry  |
| ScribbleHub  | New story on reading list; new chapter released |
| Literotica   | New story from a favourite author               |

---

## 🧹 Stopping and Cleaning Up

```bash
docker stop book-notifier
docker rm book-notifier
```

To reset the cache:

```bash
rm -rf ./data
```

---

## 🤖 AI Usage

Parts of this project were developed with AI assistance. Below is a summary of where it was used:

- **README generation** — documentation written with AI assistance based on source code
- **Summary Tags** — In instances of C# Summary tags, they're generated by Visual Studio built in agent
- **HTML parsing logic** — CSS & HTML DOM selector and regex patterns for scraping responses
- **Webhook payload formatting** — Discord embed structure and message formatting
- **Models Advice** — Internal records and class data types were often helped formed using AI (Cleanup / Standardizing)
- **GitHub Actions workflow** — CI/CD pipeline for building and publishing Docker images

---

## 📄 License

MIT License

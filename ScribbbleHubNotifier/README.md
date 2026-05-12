# ScribbleHub Story Notifier

![License](https://img.shields.io/github/license/RiisDev/BookNotifier)
![Build](https://img.shields.io/github/actions/workflow/status/RiisDev/BookNotifier/docker-publish.yml?label=docker%20build)
![Image Tag](https://ghcr-badge.egpl.dev/riisdev/scribblehub-notifier/latest_tag?label=latest)
![Image Size](https://ghcr-badge.egpl.dev/riisdev/scribblehub-notifier/size)

A lightweight notifier that monitors your [ScribbleHub](https://www.scribblehub.com) reading list and sends Discord notifications when new chapters are released.

> Note it only checks when the container gets ran, so to automate setup a CRON schedule or windows task scheduler.

## Features

* Logs into ScribbleHub using credentials or a pre-set session cookie
* Fetches your full reading list and each story's table of contents
* Detects new stories added to your reading list
* Detects new chapters on stories you're already tracking
* Sends notifications to a Discord webhook
* Simple Docker setup

---

## 📦 Requirements

* Docker installed
* A ScribbleHub account with a reading list
* A Discord Webhook URL

---

## 🚀 Setup

### 1. Clone the Repository or pull the image

```bash
git clone https://github.com/RiisDev/BookNotifier.git
cd BookNotifier
cd ScribbbleHubNotifier
```

```bash
docker pull ghcr.io/riisdev/scribblehub-notifier:latest
```

### 2. Create a `.env` File

Create a `.env` file in the root of the project. You can authenticate with either credentials **or** a pre-set session cookie:

**Option A — Username & Password:**
```env
WEBHOOK=https://your.webhook.url/here
USERNAME=your_scribblehub_username
PASSWORD=your_scribblehub_password
```

**Option B — Pre-set Cookie (fallback / captcha avoidance):**
```env
WEBHOOK=https://your.webhook.url/here
PRESET_COOKIE=your_session_cookie_string
```

> If login fails (e.g. due to a CAPTCHA), the notifier will automatically fall back to `PRESET_COOKIE`. If neither succeeds, the process will exit with an error.

### 3. Run with Docker

```bash
docker build -t scribblehub-notifier .
docker run -d \
  --name scribblehub-notifier \
  --env-file .env \
  -v $(pwd)/data:/app/data \
  scribblehub-notifier
```

Or build from source:

```bash
git clone https://github.com/RiisDev/BookNotifier.git
cd BookNotifier
docker build -t scribblehub-notifier ./ScribbbleHubNotifier
docker run -d \
  --name scribblehub-notifier \
  --env-file .env \
  -v $(pwd)/data:/app/data \
  scribblehub-notifier
```

---

## 🛠 Configuration

| Variable        | Description                                                                 |
| --------------- | --------------------------------------------------------------------------- |
| `WEBHOOK`       | Discord webhook URL where notifications will be sent                        |
| `USERNAME`      | Your ScribbleHub username                                                   |
| `PASSWORD`      | Your ScribbleHub password                                                   |
| `PRESET_COOKIE` | A pre-authenticated session cookie string (used as fallback or alternative) |

| Volume      | Description                                                       |
| ----------- | ----------------------------------------------------------------- |
| `/app/data` | Stores `books.json`, the chapter cache used to detect new content |

---

## 📝 How It Works

1. On startup, the notifier attempts to log in with `USERNAME` and `PASSWORD`.
2. If login fails (e.g. CAPTCHA), it falls back to `PRESET_COOKIE`.
3. Your ScribbleHub reading list is fetched, along with the full table of contents for each story.
4. The current state is compared against the cached `data/books.json` from the previous run.
5. Two types of changes trigger a notification:
   - A **new story** appears on your reading list that wasn't previously tracked.
   - An **existing story** has a new latest chapter compared to the cache.
6. The cache is updated with the latest state after each check.

---

## 🔔 Notifications

Two types of Discord notifications are sent:

* **New story** — a story on your reading list has never been seen before, including its current chapter count.
* **New chapter** — a story you're already tracking has a new latest chapter.

---

## 🧹 Stopping and Cleaning Up

```bash
docker stop scribblehub-notifier
docker rm scribblehub-notifier
```

To reset the chapter cache:

```bash
rm -rf ./data
```

---

## 📄 License

MIT License
# GoodReads Story Notifier

![License](https://img.shields.io/github/license/RiisDev/BookNotifier)
![Build](https://img.shields.io/github/actions/workflow/status/RiisDev/BookNotifier/docker-publish.yml?label=docker%20build)
![Image Tag](https://ghcr-badge.egpl.dev/RiisDev/goodreads-notifier/latest_tag?label=latest)
![Image Size](https://ghcr-badge.egpl.dev/RiisDev/goodreads-notifier/size)

A lightweight notifier that checks for new books from authors on your [GoodReads](https://goodreads.com) reading shelf and sends updates to a webhook.

> Note it only checks when the container gets ran, so to automate setup a CRON schedule or windows task scheduler.

## Features

* Fetches authors from a specified shelf on your GoodReads profile
* Checks for new books published by those authors
* Tracks new entries in series you're following
* Sends new book notifications to a specified webhook
* Simple Docker setup

---

## 📦 Requirements

* Docker installed
* A GoodReads account with a populated shelf
* A Discord (or compatible) Webhook URL

---

## 🚀 Setup

### 1. Clone the Repository or pull existing

```bash
docker pull ghcr.io/RiisDev/goodreads-notifier:latest
```

```bash
git clone https://github.com/RiisDev/BookNotifier.git
cd BookNotifier
cd GoodReadsNotifier
```

### 2. Create a `.env` File

Create a `.env` file in the root of the project with the following required environment variables:

```env
WEBHOOK=https://your.webhook.url/here
USER_ID=your_goodreads_user_id
SHELF_TAG=your_shelf_name
```

> **Finding your User ID:** Your GoodReads User ID can be found in the URL of your profile page: `https://www.goodreads.com/user/show/YOUR_USER_ID`

### 3. Run with Docker

Build and run the notifier with a local volume for persistent state:

```bash
docker build -t goodreads-notifier .
docker run -d \
  --name goodreads-notifier \
  --env-file .env \
  -v $(pwd)/data:/app/data \
  goodreads-notifier
```

Or run built variant

```bash
docker run -d \
  --name goodreads-notifier \
  --env-file .env \
  -v $(pwd)/data:/app/data \
  ghcr.io/RiisDev/goodreads-notifier:latest
```

---

## 🛠 Configuration

| Variable      | Description                                                        |
| ------------- | ------------------------------------------------------------------ |
| `WEBHOOK`     | The URL to which notifications will be POSTed                      |
| `USER_ID`     | Your GoodReads user ID (used to fetch your shelf)                  |
| `SHELF_TAG`   | The shelf name to watch (e.g. `to-read`, `currently-reading`)      |

| Volume      | Description                                          |
| ----------- | ---------------------------------------------------- |
| `/app/data` | Stores the book cache used to detect new releases    |

---

## 📝 How It Works

1. On startup, the notifier fetches all books from the configured shelf.
2. It collects the distinct authors from that shelf and retrieves each author's full book list from GoodReads.
3. For books that are part of a series, it also checks for new entries in those series.
4. Any book not seen in a previous run is sent as a notification to the configured webhook.
5. The known-books cache is saved to `/app/data` so state persists across restarts.

---

## 🔔 Notifications

Two types of notifications are sent:

* **New author book** — triggered when an author on your shelf publishes a book not previously seen.
* **New series entry** — triggered when a new book appears in a series associated with your shelf.

---

## 🧹 Stopping and Cleaning Up

```bash
docker stop goodreads-notifier
docker rm goodreads-notifier
```

To also remove the local data cache:

```bash
rm -rf ./data
```

---

## 📄 License

MIT License
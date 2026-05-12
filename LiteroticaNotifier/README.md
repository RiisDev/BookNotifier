# Literotica Story Notifier

![License](https://img.shields.io/github/license/RiisDev/BookNotifier)
![Build](https://img.shields.io/github/actions/workflow/status/RiisDev/BookNotifier/docker-publish.yml?label=docker%20build)
![Image Tag](https://ghcr-badge.egpl.dev/riisdev/literotica-notifier/latest_tag?label=latest)
![Image Size](https://ghcr-badge.egpl.dev/riisdev/literotica-notifier/size)

A lightweight notifier that checks for new stories from your favorite authors on [Literotica](https://literotica.com) and sends updates to a webhook.

## Features

* Fetches your favorite authors from your Literotica profile
* Periodically checks for new stories
* Sends new story notifications to a specified webhook
* Simple Docker setup

---

## 📦 Requirements

* Docker installed
* A Literotica account with favorite authors saved
* A Discord Webhook URL

---

## 🚀 Setup

### 1. Clone the Repository or pull the image

```bash
git clone https://github.com/RiisDev/BookNotifier.git
cd BookNotifier
cd LiteroticaNotifier
```

```bash
docker pull ghcr.io/riisdev/literotica-notifier:latest
```

### 2. Create a `.env` file

Create a `.env` file in the root of the project with the following required environment variables:

```env
WEBHOOK=https://your.webhook.url/here
LIT_USERNAME=your_literotica_username
RECHECK_MS=60000  # Check every 60 seconds (in milliseconds)
```

### 3. Run with Docker

To run the notifier with Docker and bind `/app/data` to a local volume (for persistent cache or state):

```bash
docker build -t literotica-notifier .

docker run -d \
  --name lit-notifier \
  --env-file .env \
  -v $(pwd)/data:/app/data \
  literotica-notifier
```

Or build from source:

```bash
git clone https://github.com/RiisDev/BookNotifier.git
cd BookNotifier
docker build -t literotica-notifier ./ScribbbleHubNotifier
docker run -d \
  --name literotica-notifier \
  --env-file .env \
  -v $(pwd)/data:/app/data \
  literotica-notifier
```

---

## 🛠 Configuration

| Variable       | Description                                              |
| -------------- | -------------------------------------------------------- |
| `WEBHOOK`      | The URL to which notifications will be sent              |
| `LIT_USERNAME` | Your Literotica username (used to pull favorite authors) |
| `RECHECK_MS`   | Interval in milliseconds to check for new stories        |

| Volume         |   Description                                              |
| -------------- | -------------------------------------------------------- |
| `/app/data`    |  This stores the story cache/database to compare to |

---

## 📝 Notes

* Favorite authors must be visible on your public profile.
* Story update checking is based on comparing known stories with new ones each interval.

---

## 🧹 Stopping and Cleaning Up

To stop and remove the running container:

```bash
docker stop lit-notifier
docker rm lit-notifier
```

---

## 📄 License

MIT License

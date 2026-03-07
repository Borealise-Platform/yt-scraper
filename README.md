A C# microservice that scrapes YouTube because Google decided API quotas should be
measured in "lol good luck". No API key. No quota. No mercy.

## What does it do

Exposes a tiny HTTP API that the Borealise backend calls instead of begging Google
for permission to read public data. Uses [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode)
which is doing God's work so we don't have to.

## Endpoints

| Route | What it does |
|-------|-------------|
| `GET /search?q=<query>&limit=<n>` | Searches YouTube like a normal person would |
| `GET /video?id=<videoId>` | Gets info about one (1) video |
| `GET /playlist?id=<playlistId>` | Fetches an entire playlist because why not |

## Running it

```bash
dotnet run
```

Starts on port `5001`. If that's taken:

```bash
dotnet run -- --port 3000
```

## Why C# and not just more TypeScript

Because the YouTube Data API has a daily quota of approximately 47 requests before
it starts returning 403s and crying. YoutubeExplode scrapes YouTube's internal
endpoints like a feral raccoon in a Google data center. It's not pretty but it works.

## Project structure

```
Program.cs          — 30 lines. boots the server. very important.
Models.cs           — records. just vibes.
Helpers.cs          — picks the best thumbnail. peak engineering.
YouTubeService.cs   — the actual scraping. do not look directly at it.
RequestHandler.cs   — routes requests. nothing fancy.
```

## Disclaimer

If this breaks it's YouTube's fault for having a quota in the first place.

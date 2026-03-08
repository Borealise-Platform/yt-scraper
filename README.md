A C# microservice that scrapes YouTube because Google decided API quotas should be
measured in "lol good luck". No API key required (unless you want one, we support that now too - shock horror). No quota. No mercy.

## What does it do

Exposes a tiny HTTP API that the Borealise backend calls instead of begging Google
for permission to read public data. Uses [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode)
which is doing God's work so we don't have to.

Also supports actual YouTube Data API keys if you're into that sort of thing.
You know, the ones that cost $0 and give you 10,000 units per day which sounds like a lot
until you realize one (1) search costs 100 units. But hey, you do you.

## Endpoints

| Route | What it does |
|-------|-------------|
| `GET /search?q=<query>&limit=<n>` | Searches YouTube like a normal person would |
| `GET /video?id=<videoId>` | Gets info about one (1) video |
| `GET /playlist?id=<playlistId>&limit=<n>` | Fetches a playlist. All of it. Unless you add a limit, then just that many. |

## Running it

```bash
dotnet run
```

Starts on port `5001`. If that's taken:

```bash
dotnet run -- --port 3000
```

Or actually, check `appsettings.json` if you bothered to configure anything there.
We won't judge (okay we will, but quietly).

## API Keys (optional)

If you're one of those people who actually has YouTube Data API credentials
sitting around gathering dust:

```json
{
  "YouTubeApiKeys": [
    "your-api-key-here",
    "another-one-because-rotation-is-trendy"
  ]
}
```

We'll try the API first. If it fails (quota exceeded, rate limited, or just having a bad day),
we'll gracefully fall back to scraping like the rest of us. You're welcome.

## Why C# and not just more TypeScript

Because the YouTube Data API has a daily quota of approximately 47 requests before
it starts returning 403s and crying. YoutubeExplode scrapes YouTube's internal
endpoints like a feral raccoon in a Google data center. It's not pretty but it works.

Also at one point we had a perfectly good C# scraper but the config file wasn't
being copied to the output directory so the API keys didn't load. That was a fun
afternoon. Check your build output. We learned so much.

## Project structure

```
Program.cs          — 30 lines. boots the server. very important.
Models.cs           — records. just vibes.
Helpers.cs          — picks the best thumbnail. peak engineering.
YouTubeService.cs   — the actual scraping. do not look directly at it.
YouTubeApiClient.cs — tries to use API keys. cries when they don't work.
RequestHandler.cs   — routes requests. nothing fancy.
Logger.cs           — because printf debugging is so 2023.
```

## Logging

We added logging because you clearly couldn't figure out what was going wrong
on your own. Check the console. It's got timestamps. It's got levels. It's got
more information than you probably need, but it's there if you want it.

## Disclaimer

If this breaks it's YouTube's fault for having a quota in the first place.
Also if you abuse it too hard YouTube will rate-limit you and we'll fall back
to scraping which is slower but at least it works until YouTube decides to change
their HTML again for absolutely no reason.

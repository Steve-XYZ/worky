# Worky

AI assistant that watches your X (Twitter) network for job opportunities and coaches your
outreach. Advisory only: Worky reads, ranks, and drafts — you send everything.

## Status

Scaffold. Current slices: recent-search scanner + heuristic job-signal classifier +
ranked digest in the terminal, and OAuth user-context graph sync into
`~/.worky/state.json`.

## Compliance stance

Read-only analysis plus human-approved sending. No auto-follow or auto-like
(X removed these API writes from all self-serve tiers in April 2026), no unsolicited
DMs or mass mentions (prohibited by X automation rules). X's pay-per-use API bills per
read; keep `--limit` sensible while calibrating queries.

## Prerequisites

- .NET 10 SDK
- X API bearer token from a developer project (pay-per-use tier):
  `export WORKY_BEARER_TOKEN=...`

## Run

### Login (user context)

```
export WORKY_CLIENT_ID=...
dotnet run --project src/Worky.Cli -- login
```

Prerequisites: an X developer app with OAuth 2.0 **User authentication** enabled
and its client id exported as `WORKY_CLIENT_ID`. The CLI prints a callback URL
(`http://127.0.0.1:<random port>/callback`) before opening the consent page;
register that exact URL as a redirect URI in your app's User authentication
settings, or login fails and the CLI repeats it. Scopes are the minimum for the
roadmap: `tweet.read users.read follows.read offline.access` (read your network,
keep the login refreshable). Tokens persist to `~/.worky/auth.json` with
owner-only permissions, refresh transparently, and are never printed.

### Scan (app bearer)

```
dotnet run --project src/Worky.Cli -- scan
dotnet run --project src/Worky.Cli -- scan --query '"backend engineer" hiring -is:retweet lang:en' --limit 200
```

Default query targets hiring phrases in English, excluding replies and reposts.
Output ranks matched posts by signal score, then recency, with the reason for each match.

When `~/.worky/state.json` exists, both scan modes add a `network match` reason and a
+1.0 signal bonus to posts authored by anyone you follow. A missing or corrupt snapshot
simply leaves scores untouched.

### Targeted scan (follow graph)

```
dotnet run --project src/Worky.Cli -- scan --targeted
dotnet run --project src/Worky.Cli -- scan --targeted --interests "rust,gamedev" --max-authors 50 --limit 200
```

Prerequisites: a snapshot from `worky sync-graph` younger than 7 days; targeted scans
read but never refresh it. The search aims at your follow graph instead of the whole
platform: followed authors are ranked by interest-keyword overlap with their name and
bio, the top `--max-authors` (default 100) are batched into `from:` queries that respect
X's query budgets, and each batch runs one recent search on the app bearer.
`--interests "a,b,c"` replaces the default hiring phrases (`hiring`, `"we're hiring"`,
`"job opening"`, `"open role"`, `"join our team"`) in the queries and steers author
ranking.

### Sync graph (user context)

```
dotnet run --project src/Worky.Cli -- sync-graph
dotnet run --project src/Worky.Cli -- sync-graph --max-pages 10 --refresh-graph
```

Prerequisites: a stored login (`worky login`) and `WORKY_CLIENT_ID` exported for
transparent token refresh. Snapshots who you follow into `~/.worky/state.json`:
your user id/username, followed users (id, username, name, description), and an
ingestion timestamp. Each page reads up to 100 accounts on X's pay-per-use API;
the default cap is 5 pages (~500 authors). A snapshot younger than 7 days is
reused and no calls are made; pass `--refresh-graph` to force a new one.

## Layout

```
src/Worky.Core         Domain models, X API v2 client, OAuth 2.0 PKCE flow, job-signal classifier, ranker, graph state, targeted scan engine
src/Worky.Cli          Terminal entry point (`login`, `scan`, `sync-graph` commands)
tests/Worky.Core.Tests xUnit tests for the classifier, ranker, auth plumbing, graph sync, and scan targeting
```

## Roadmap

- OAuth 2.0 user context: ingest who you follow and interact with to build an interest profile
- LLM second-stage classifier and relevance ranking against your profile
- Digest delivery (email/dashboard) and drafted reply suggestions
- Cost meter tracking X API credit spend

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

## Cost & limits

X bills Worky's reads on the pay-per-use tier. The prices below are approximations
from X's published pay-per-use rates, held as named constants in
`src/Worky.Core/CostEstimator.cs` — **measure them against your real developer
console before trusting any dollar figure**.

| Read type                           | Approx. price |
|-------------------------------------|---------------|
| Post read (third-party, app bearer) | $0.005        |
| User data read                      | $0.010        |
| Owned read                          | $0.001        |

Every command prints `estimated cost: $X.XX–$Y.YY` before it makes any HTTP call.
The floor prices all planned reads at the owned rate; the ceiling prices them at the
most expensive rate the command's shape could trigger — targeted scans additionally
assume one user read per requested author cap, and sync-graph assumes full
100-account pages up to `--max-pages`. After each run the CLI prints an
`actual reads:` line: a response-volume-based estimate (`~`) of what the run
returned, not a bill from X.

### Login setup

1. In your X developer app enable **User authentication** (OAuth 2.0).
2. Register the callback URL as a redirect URI: the CLI binds a random local port,
   so run `worky login` once, copy the exact printed
   `http://127.0.0.1:<port>/callback`, and paste it into *User authentication
   settings → Redirect URIs*. A mismatched URI fails the login; the CLI repeats the
   URL it expected.
3. Export the app's client id as `WORKY_CLIENT_ID`.

Scopes requested: `tweet.read users.read follows.read offline.access` — the minimum
to read your network and keep the login refreshable. Worky deliberately requests no
write scopes: it never posts, follows, likes, or messages on your behalf.

### Rate limits

X enforces per-endpoint request windows and replies with HTTP 429 carrying an
`x-rate-limit-reset` epoch timestamp. Worky raises a typed `XRateLimitException`
and never retries automatically. Scan commands print the reset time in your local
timezone on stderr, then still rank and show whatever posts were already collected
("rate limited, showing partial results") before exiting with code 1. `sync-graph`
writes its snapshot only after every page succeeds, so a rate-limited sync leaves
any prior snapshot untouched.

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
- Calibrate the cost estimator against real developer-console billing data

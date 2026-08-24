# Worky

AI assistant that watches your X (Twitter) network for job opportunities and coaches your
outreach. Advisory only: Worky reads, ranks, and drafts — you send everything.

## Status

Scaffold. Current slice: recent-search scanner + heuristic job-signal classifier +
ranked digest in the terminal.

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

```
dotnet run --project src/Worky.Cli -- scan
dotnet run --project src/Worky.Cli -- scan --query '"backend engineer" hiring -is:retweet lang:en' --limit 200
```

Default query targets hiring phrases in English, excluding replies and reposts.
Output ranks matched posts by signal score, then recency, with the reason for each match.

## Layout

```
src/Worky.Core         Domain models, X API v2 client, job-signal classifier, ranker
src/Worky.Cli          Terminal entry point (`scan` command)
tests/Worky.Core.Tests xUnit tests for the classifier and ranker
```

## Roadmap

- OAuth 2.0 user context: ingest who you follow and interact with to build an interest profile
- LLM second-stage classifier and relevance ranking against your profile
- Digest delivery (email/dashboard) and drafted reply suggestions
- Cost meter tracking X API credit spend

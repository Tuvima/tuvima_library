## Summary

<!-- What does this PR do? 1-3 bullet points. -->

-

## Motivation

<!-- Why is this change needed? Link to an issue if applicable: Fixes #123 -->

## Changes

<!-- List the key changes. Group by area if touching multiple parts. -->

### Engine (`src/MediaEngine.Api`, `Domain`, `Storage`, etc.)

-

### Dashboard (`src/MediaEngine.Web`)

-

### Configuration (`config/`)

-

## How to test

<!-- Step-by-step instructions for reviewers to verify this works. -->

1.

## Checklist

- [ ] `dotnet build` passes with **0 errors, 0 warnings**
- [ ] `dotnet test` passes — all tests green
- [ ] Docker build succeeds (`docker build -t test .`)
- [ ] New dependencies are AGPLv3-compatible (see `CLAUDE.md` §5.1)
- [ ] Documentation updated if needed (`README.md`, `CLAUDE.md`, `config/`)
- [ ] No secrets, API keys, or local paths committed
- [ ] Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `chore:`)

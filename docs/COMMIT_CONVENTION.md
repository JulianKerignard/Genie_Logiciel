# Commit & Branch Convention

All commits **in English**. Conventional Commits format.

## Format

```
<type>(<scope>): <subject>
```

- Imperative (`add`, not `added`)

Example: `feat(backup): support differential strategy`

## Prefixes

| Prefix | When | Example |
|---|---|---|
| `feat` | New feature | `feat(cli): parse '1-3' selections` |
| `fix` | Bug fix | `fix(state): prevent concurrent writes` |
| `refactor` | Code change, no behavior change | `refactor(backup): extract file walker` |
| `docs` | Docs only | `docs(readme): document run modes` |
| `test` | Tests only | `test(easylog): cover multi-thread append` |
| `chore` | Tooling, deps, config | `chore: add .gitignore for .net` |
| `perf` | Perf improvement | `perf(backup): stream files` |
| `ci` | GitHub Actions | `ci: always post review verdict` |
| `revert` | Reverts a commit | `revert: "feat(backup): ..."` |

Breaking change: `feat(easylog)!: rename ILogger`

## Branches

One branch per unit of work. Never code on `staging` or `main`.

| Prefix | Usage |
|---|---|
| `feat/xxx` | new feature |
| `fix/xxx` | bug |
| `refactor/xxx` | refactor |
| `docs/xxx` | doc only |
| `test/xxx` | test only |
| `chore/xxx` | tooling, config |
| `ci/xxx` | CI/CD |
| `hotfix/xxx` | urgent prod fix (starts from main) |

Kebab-case, short.

## Flow

```
staging -> feat/my-feature -> PR -> staging -> PR -> main (release)
```

1. `git checkout staging && git pull`
2. `git checkout -b feat/my-feature`
3. code + commits
4. `git push -u origin feat/my-feature`
5. PR to `staging`, wait for Claude + teammate review
6. Squash merge, delete branch

## Validate staging before release

- [ ] `dotnet build` OK
- [ ] `dotnet test` OK
- [ ] App runs end-to-end
- [ ] Docs & CHANGELOG up to date

## Forbidden

- No AI mentions (Claude, Anthropic, Co-Authored-By, "Generated with")
- No secret in message
- No French

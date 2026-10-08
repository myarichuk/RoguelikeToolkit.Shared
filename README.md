# RoguelikeToolkit.Shared

[![CI](https://github.com/myarichuk/RoguelikeToolkit.Shared/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/myarichuk/RoguelikeToolkit.Shared/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/myarichuk/RoguelikeToolkit.Shared/branch/master/graph/badge.svg)](https://codecov.io/gh/myarichuk/RoguelikeToolkit.Shared)

Apache-2.0 licensed open-source building blocks for roguelike (and other) .NET games.

| Package | Target | Description |
|---|---|---|
| `RoguelikeToolkit.EventAggregator` | `netstandard2.1` | Synchronous in-process publish/subscribe messaging. |
| `RoguelikeToolkit.StateMachine` | `netstandard2.1` | Minimal finite state machine (`Configure` / `CanFire` / `Fire`). |

Test projects (`*.Tests`, xUnit v3) live under [`tests/`](tests/) and target `net10.0`.

## Unity compatibility

The libraries target `netstandard2.1` (supported by Unity 2021.2+) so the
packed DLLs run on Unity, Godot, and any other netstandard2.1 host, while
compiling them only requires the .NET SDK:

- **Zero runtime dependencies** — each shipped DLL references only `netstandard`.
- **C# 9.0 language cap** on `src/*` so new code cannot use
  runtime-dependent features (e.g. static abstract members, required members)
  that older Unity runtimes cannot execute. Test projects stay on `latest`.
- The xUnit suites run against the actual `netstandard2.1` binaries
  (built via `ProjectReference` and loaded by the `net10.0` test host).

To use a package in Unity, install it via UPM (see below). As a manual
fallback, drop `lib/netstandard2.1/*.dll` from the `.nupkg` into your
project's `Assets/Plugins` folder.

## Install in Unity

Each library is a UPM package (`package.json` + `.asmdef` alongside the
sources at the package root):

| Library | UPM name |
|---|---|
| EventAggregator | `com.rogueliketoolkit.event-aggregator` |
| StateMachine | `com.rogueliketoolkit.state-machine` |

- **Via git URL** (works today): Package Manager → `+` → *Add package from
  git URL…*, e.g.
  `https://github.com/myarichuk/RoguelikeToolkit.Shared.git?path=/src/RoguelikeToolkit.EventAggregator`
- **Via OpenUPM** (after the one-time registration below): add the
  `https://package.openupm.com` scoped registry and install by name, with
  in-editor updates.

One-time OpenUPM registration: fork
[`openupm/openupm`](https://github.com/openupm/openupm), add your package
entry, and open a PR. Afterwards every GitHub Release tarball is published
automatically.

## Build & test

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download) (pinned via `global.json`).

```powershell
dotnet build
dotnet test
```

### Coverage

Coverage is tracked per package, matching the independent versioning:
[`CI`](.github/workflows/ci.yml) tests each `*.Tests` project with
[coverlet](https://github.com/coverlet-coverage/coverlet) (already referenced
by the test projects, settings in [`coverlet.runsettings`](coverlet.runsettings)),
renders a report per package with
[ReportGenerator](https://github.com/danielpalme/ReportGenerator) (pinned in
[`.config/dotnet-tools.json`](.config/dotnet-tools.json)), posts both
summaries to the job summary, uploads the HTML reports as the
`coverage-report` artifact, and fails the run if either package drops below
90% line coverage. Each package is also uploaded to Codecov under its own
flag (`event-aggregator`, `state-machine`). Reproduce it locally:

```powershell
dotnet test tests/RoguelikeToolkit.EventAggregator.Tests -c Release -s coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory ./TestResults/event-aggregator
dotnet test tests/RoguelikeToolkit.StateMachine.Tests -c Release -s coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory ./TestResults/state-machine
dotnet tool restore
dotnet reportgenerator -reports:"TestResults/event-aggregator/**/coverage.cobertura.xml" -targetdir:"coveragereport/event-aggregator" -reporttypes:"MarkdownSummaryGithub;Cobertura;Html"
dotnet reportgenerator -reports:"TestResults/state-machine/**/coverage.cobertura.xml" -targetdir:"coveragereport/state-machine" -reporttypes:"MarkdownSummaryGithub;Cobertura;Html"
```

## Versioning, changelog & publishing

Releases are fully automated per package; each library versions independently.

1. **Commit** using [Conventional Commits](https://www.conventionalcommits.org/)
   (`feat:`, `fix:`, …). Only `feat`/`fix`/breaking changes trigger a release.
2. **Release-please** ([`release-please.yml`](.github/workflows/release-please.yml))
   opens a *Release PR* on `main` that updates `CHANGELOG.md`, bumps the UPM
   `package.json` version, and — once merged — creates a tag
   (`event-aggregator-v1.2.3`) plus a GitHub Release with changelog notes.
3. **Publish** ([`publish.yml`](.github/workflows/publish.yml)) runs on the tag
   and ships that package to both distributions:
   - NuGet (version from the tag via [MinVer](https://github.com/adamralph/minver)
     `MinVerTagPrefix`; `--skip-duplicate` makes repushes idempotent),
   - UPM tarball attached to the GitHub Release (consumed by OpenUPM).

   > External publishing (NuGet push, OpenUPM notify) runs only when the
   > `PUBLISH_ENABLED` repo variable is `true`; until the one-time setup
   > below is done those steps skip, while release-please tags, GitHub
   > Releases, and UPM tarball attach still work.

A package with no releasable commits gets no tag and no publish — that is the
"only if changed" gate. [`CI`](.github/workflows/ci.yml) additionally
validates the UPM packages (`npm pack --dry-run`) on every push/PR.

Setup (one time):

1. On nuget.org, register this repo as a **trusted publisher** (account menu →
   Trusted Publishing → Add GitHub Actions: owner `myarichuk`, repository
   `RoguelikeToolkit.Shared`, workflow file `publish.yml`, scoped to the
   `RoguelikeToolkit.*` prefix). The workflow mints a short-lived key via
   OIDC — no API key secret to store or rotate.
2. Register the UPM names on OpenUPM (one-time PR, see above).
3. Create the `PUBLISH_ENABLED` repo variable (Settings → Secrets and
   variables → Actions → Variables tab) with value `true`.

[`CI`](.github/workflows/ci.yml) builds and tests every push / pull request to `main`.

## License

[Apache-2.0](LICENSE) — see `LICENSE` for the full text.

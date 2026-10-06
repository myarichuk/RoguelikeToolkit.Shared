# RoguelikeToolkit.Shared

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

To use a package in Unity, drop `lib/netstandard2.1/*.dll` from the `.nupkg`
into your project's `Assets/Plugins` folder (Unity has no built-in
nuget.org consumer).

## Build & test

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download) (pinned via `global.json`).

```powershell
dotnet build
dotnet test
```

## Versioning & publishing

Versions are derived from git tags with [MinVer](https://github.com/adamralph/minver):
tag a release as `v1.2.3` and every package packed from that commit gets version `1.2.3`.

The [`Publish`](.github/workflows/publish.yml) workflow runs on pushes to `main` and on
`v*` tags and publishes **only the packages whose sources changed**
(detected with `dorny/paths-filter`; shared files such as `Directory.Build.props`
count as a change for both packages). Tag builds publish both packages.
`dotnet nuget push --skip-duplicate` makes republishing idempotent, so a package
whose version already exists on NuGet is skipped.

Setup:

1. Update `RepositoryUrl` / `PackageProjectUrl` in [`Directory.Build.props`](Directory.Build.props)
   to point at your fork.
2. Add a `NUGET_API_KEY` secret (nuget.org API key) to the repository.

[`CI`](.github/workflows/ci.yml) builds and tests every push / pull request to `main`.

## License

[Apache-2.0](LICENSE) — see `LICENSE` for the full text.

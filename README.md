# RoguelikeToolkit.Shared

[![CI](https://github.com/myarichuk/RoguelikeToolkit.Shared/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/myarichuk/RoguelikeToolkit.Shared/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/myarichuk/RoguelikeToolkit.Shared/branch/master/graph/badge.svg)](https://codecov.io/gh/myarichuk/RoguelikeToolkit.Shared)

Small .NET building blocks for roguelike (and other) games. Apache-2.0.

- `RoguelikeToolkit.EventAggregator` — synchronous in-process pub/sub (`EventBus`). Thread-safe; publishing allocates nothing.
- `RoguelikeToolkit.StateMachine` — FSM with shared context, ordered guarded branches, entry/exit actions. Thread-safe; `Post` from any thread and `Pump` on yours (e.g. Unity's main thread) without the machine knowing about Unity.

Both target `netstandard2.1` (Unity 2021.2+) with zero dependencies. Language is capped at C# 9 in `src/` so the shipped DLLs stay runnable on older Unity runtimes. Tests are xUnit under `tests/`.

## Use

Unity (UPM), via git URL for now. **Experimental:** the packages ship no `.meta` files yet, which Unity may need to import them from an immutable package folder, so this path is unverified:

- `https://github.com/myarichuk/RoguelikeToolkit.Shared.git?path=/src/RoguelikeToolkit.EventAggregator`
- `https://github.com/myarichuk/RoguelikeToolkit.Shared.git?path=/src/RoguelikeToolkit.StateMachine`

(OpenUPM takes over once the packages are registered there. For plain .NET, grab them from NuGet.)

## Build

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download):

```powershell
dotnet build
dotnet test
```

## Conventions

- **Public API is tracked.** Anything public must appear in the package's `PublicAPI.Unshipped.txt`, or the build fails (RS0016).
  Add it with `dotnet format analyzers src/<Package> --diagnostics RS0016`. Everything is still "unshipped" while the packages are 0.x.
- **Sources must compile the way Unity compiles them** (C# 9, nullable context off): keep `#nullable enable` at the top of every `src/` file.
  CI builds `eng/UnityCompat` to prove it.
- **Every Unity-visible file in a package has a `.meta`** (deterministic GUIDs). `python3 eng/packages.py meta --fix` creates them.
- **Package versions live in `Directory.Packages.props`;** workflow actions are pinned to commit SHAs (Dependabot keeps both fresh).

### Adding a package

Create `src/RoguelikeToolkit.<Name>/` with a `package.json` (name `com.rogueliketoolkit.<component>`), then run
`python3 eng/packages.py check`. It lists everything else that needs an entry: csproj (`PackageId`, `MinVerTagPrefix`), asmdef,
README, API files, `.npmignore`, solution, release-please config and manifest, and the tests project. CI and the publish workflow
discover the package from the folder, so they need no edit.

## Releases

Automatic per package, nothing to do by hand. Push `feat:`/`fix:` commits to `master`, release-please opens a release PR, merge it and the tag + publish (NuGet, UPM tarball) happen on their own. Unchanged packages get no release.

One-time setup (already done here, noted for forks): `PUBLISH_ENABLED=true` repo variable, nuget.org trusted publisher for `publish.yml`, OpenUPM registration.

## License

[Apache-2.0](LICENSE).

# RoguelikeToolkit.Shared

[![CI](https://github.com/myarichuk/RoguelikeToolkit.Shared/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/myarichuk/RoguelikeToolkit.Shared/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/myarichuk/RoguelikeToolkit.Shared/branch/master/graph/badge.svg)](https://codecov.io/gh/myarichuk/RoguelikeToolkit.Shared)

Small .NET building blocks for roguelike (and other) games. Apache-2.0.

- `RoguelikeToolkit.EventAggregator` — synchronous in-process pub/sub.
- `RoguelikeToolkit.StateMachine` — FSM with shared context, guards, entry/exit actions.

Both target `netstandard2.1` (Unity 2021.2+) with zero dependencies. Language is capped at C# 9 in `src/` so the shipped DLLs stay runnable on older Unity runtimes. Tests are xUnit under `tests/`.

## Use

Unity (UPM), via git URL for now:

- `https://github.com/myarichuk/RoguelikeToolkit.Shared.git?path=/src/RoguelikeToolkit.EventAggregator`
- `https://github.com/myarichuk/RoguelikeToolkit.Shared.git?path=/src/RoguelikeToolkit.StateMachine`

(OpenUPM takes over once the packages are registered there. For plain .NET, grab them from NuGet.)

## Build

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download):

```powershell
dotnet build
dotnet test
```

## Releases

Automatic per package, nothing to do by hand. Push `feat:`/`fix:` commits to `master`, release-please opens a release PR, merge it and the tag + publish (NuGet, UPM tarball) happen on their own. Unchanged packages get no release.

One-time setup (already done here, noted for forks): `PUBLISH_ENABLED=true` repo variable, nuget.org trusted publisher for `publish.yml`, OpenUPM registration.

## License

[Apache-2.0](LICENSE).

#!/usr/bin/env python3
"""Repo tooling for the packages under src/. Standard library only.

  packages.py matrix                  CI matrix (JSON) of every package
  packages.py resolve <tag>           outputs (key=value lines) for a release tag such as state-machine-v1.2.3
  packages.py check                   consistency of every package with the rest of the repo
  packages.py meta [--fix]            verify (or create/prune) Unity .meta files
  packages.py pack-check <nupkg-dir> [--component <c>]
                                      verify the packed NuGet packages and UPM tarballs

A package is a directory src/<Name>/ that contains a package.json. Everything else is derived from it:
  component   = package.json "name" without the "com.rogueliketoolkit." prefix   (event-aggregator)
  csproj      = src/<Name>/<Name>.csproj
  tests       = tests/<Name>.Tests/<Name>.Tests.csproj
  git tag     = <component>-v<version>

Adding a package: create the directory, then `packages.py meta --fix` and `packages.py check`; the check lists every
other file that needs an entry (solution, release-please config and manifest).
"""
import hashlib
import json
import re
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PREFIX = "com.rogueliketoolkit."
GUID_SALT = "RoguelikeToolkit.Shared/"

# Files Unity does not import (hidden) and never needs a .meta for.
def unity_visible(path: Path, pkg: Path) -> bool:
    rel = path.relative_to(pkg)
    if any(part.startswith(".") or part.endswith("~") for part in rel.parts):
        return False
    if rel.parts[0] in ("bin", "obj"):
        return False
    return path.is_file() and path.suffix != ".meta"


def packages():
    result = []
    for pj in sorted((ROOT / "src").glob("*/package.json")):
        d = pj.parent
        data = json.loads(pj.read_text())
        name = data["name"]
        if not name.startswith(PREFIX):
            raise SystemExit(f"{pj}: name must start with {PREFIX}")
        result.append({
            "name": name,
            "component": name[len(PREFIX):],
            "title": d.name.split(".", 1)[1],
            "dir": d.relative_to(ROOT).as_posix(),
            "csproj": f"{d.relative_to(ROOT).as_posix()}/{d.name}.csproj",
            "tests": f"tests/{d.name}.Tests/{d.name}.Tests.csproj",
            "tests_dir": f"tests/{d.name}.Tests",
            "version": data.get("version"),
            "json": data,
        })
    return result


def public(pkg):
    return {k: v for k, v in pkg.items() if k != "json"}


# ---------------------------------------------------------------- matrix / resolve

def cmd_matrix(_args):
    print(json.dumps({"include": [public(p) for p in packages()]}))


def cmd_resolve(args):
    tag = args[0]
    m = re.fullmatch(r"(.+)-v(\d+\.\d+\.\d+(?:[-+].+)?)", tag)
    if not m:
        raise SystemExit(f"'{tag}' is not <component>-v<semver>")
    for p in packages():
        if p["component"] == m.group(1):
            for k in ("name", "component", "title", "dir", "csproj"):
                print(f"{k}={p[k]}")
            print(f"version={m.group(2)}")
            return
    raise SystemExit(f"No package with component '{m.group(1)}' (tag {tag})")


# ---------------------------------------------------------------- meta

TEXT = "{kind}:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
MONO = ("MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n"
        "  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def meta_text(path: Path) -> str:
    rel = path.relative_to(ROOT).as_posix()
    guid = hashlib.md5((GUID_SALT + rel).encode()).hexdigest()
    if path.suffix == ".cs":
        body = MONO
    elif path.name == "package.json":
        body = TEXT.format(kind="PackageManifestImporter")
    elif path.suffix == ".asmdef":
        body = TEXT.format(kind="AssemblyDefinitionImporter")
    elif path.suffix in (".md", ".txt", ".json"):
        body = TEXT.format(kind="TextScriptImporter")
    else:
        body = TEXT.format(kind="DefaultImporter")
    return f"fileFormatVersion: 2\nguid: {guid}\n{body}"


def meta_problems(fix: bool):
    problems, guids = [], {}
    for p in packages():
        pkg = ROOT / p["dir"]
        for f in sorted(pkg.rglob("*")):
            if f.suffix == ".meta" or not f.is_file():
                continue
            if not unity_visible(f, pkg):
                continue
            meta = Path(str(f) + ".meta")
            expected = meta_text(f)
            if not meta.exists():
                problems.append(f"missing {meta.relative_to(ROOT)}")
                if fix:
                    meta.write_text(expected)
            elif not re.search(r"^guid: [0-9a-f]{32}$", meta.read_text(), re.M):
                # Unity may rewrite a meta it owns (importer settings); a valid guid is the only contract.
                problems.append(f"no valid guid in {meta.relative_to(ROOT)}")
                if fix:
                    meta.write_text(expected)
        for meta in sorted(pkg.rglob("*.meta")):
            rel = meta.relative_to(pkg)
            if any(part in ("bin", "obj") for part in rel.parts):
                continue
            target = Path(str(meta)[:-5])
            if not target.exists():
                problems.append(f"orphan {meta.relative_to(ROOT)}")
                if fix:
                    meta.unlink()
                continue
            g = re.search(r"^guid: (\w+)$", meta.read_text(), re.M)
            if g:
                other = guids.setdefault(g.group(1), meta)
                if other != meta:
                    problems.append(f"duplicate guid {g.group(1)}: {other.relative_to(ROOT)} and {meta.relative_to(ROOT)}")
    return problems


def cmd_meta(args):
    fix = "--fix" in args
    problems = meta_problems(fix)
    for line in problems:
        print(("fixed: " if fix else "error: ") + line)
    if problems and not fix:
        raise SystemExit(f"{len(problems)} .meta problem(s); run: python3 eng/packages.py meta --fix")


# ---------------------------------------------------------------- check

def cmd_check(_args):
    errors = []
    err = errors.append
    config = json.loads((ROOT / "release-please-config.json").read_text())
    manifest = json.loads((ROOT / ".release-please-manifest.json").read_text())
    slnx = (ROOT / "RoguelikeToolkit.slnx").read_text()
    pkgs = packages()
    if not pkgs:
        err("no packages found under src/")

    for p in pkgs:
        d, j, c = p["dir"], p["json"], p["component"]
        stem = Path(d).name
        for must in (f"{stem}.csproj", f"{stem}.asmdef", "package.json", "README.md", ".npmignore",
                     "PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt"):
            if not (ROOT / d / must).exists():
                err(f"{d}: missing {must}")
        csproj = (ROOT / p["csproj"]).read_text() if (ROOT / p["csproj"]).exists() else ""
        for prop, want in (("PackageId", stem), ("MinVerTagPrefix", f"{c}-v")):
            m = re.search(rf"<{prop}>(.*?)</{prop}>", csproj)
            if not m or m.group(1) != want:
                err(f"{p['csproj']}: <{prop}> must be '{want}'")
        asmdef = ROOT / d / f"{stem}.asmdef"
        if asmdef.exists() and json.loads(asmdef.read_text()).get("name") != stem:
            err(f"{asmdef.relative_to(ROOT)}: name must be '{stem}'")
        if j.get("license") != "Apache-2.0":
            err(f"{d}/package.json: license must be Apache-2.0")
        if "unity" not in j:
            err(f"{d}/package.json: missing 'unity' minimum version")
        if config.get("packages", {}).get(d, {}).get("component") != c:
            err(f"release-please-config.json: packages['{d}'] needs component '{c}'")
        if manifest.get(d) != j.get("version"):
            err(f".release-please-manifest.json['{d}'] = {manifest.get(d)!r} but package.json version = {j.get('version')!r}")
        for proj in (p["csproj"], p["tests"]):
            if proj not in slnx:
                err(f"RoguelikeToolkit.slnx: missing {proj}")
            if not (ROOT / proj).exists():
                err(f"{proj} does not exist")
    known = {p["dir"] for p in pkgs}
    for d in list(config.get("packages", {})) + list(manifest):
        if d not in known:
            err(f"release-please entry '{d}' has no package under src/")

    # A non-generic type named like a package's namespace (RoguelikeToolkit.EventAggregator.EventAggregator) is
    # unusable from any RoguelikeToolkit.* namespace: the simple name binds to the sibling namespace first (CS0118).
    titles = {p["title"] for p in pkgs}
    decl = re.compile(r"\b(?:class|struct|interface|enum|record|delegate)\s+(?:\w+\s+)*?(" + "|".join(map(re.escape, sorted(titles))) + r")\b(?!\s*<)") if titles else None
    for p in pkgs:
        for cs in sorted((ROOT / p["dir"]).rglob("*.cs")):
            if any(part in ("bin", "obj") for part in cs.relative_to(ROOT / p["dir"]).parts):
                continue
            for n, line in enumerate(cs.read_text().splitlines(), 1):
                m = decl.search(line) if decl else None
                if m and not line.lstrip().startswith("//"):
                    err(f"{cs.relative_to(ROOT)}:{n}: type '{m.group(1)}' has the same name as a package namespace; "
                        f"consumers under RoguelikeToolkit.* could not use it unqualified")

    for wf in sorted((ROOT / ".github/workflows").glob("*.yml")):
        for n, line in enumerate(wf.read_text().splitlines(), 1):
            m = re.match(r"\s*-?\s*uses:\s*([^\s#]+)", line)
            if m and not m.group(1).startswith("./") and not re.search(r"@[0-9a-f]{40}$", m.group(1)):
                err(f"{wf.relative_to(ROOT)}:{n}: '{m.group(1)}' must be pinned to a full commit SHA")

    for line in meta_problems(False):
        err(line)

    for e in errors:
        print("error:", e)
    if errors:
        raise SystemExit(f"{len(errors)} problem(s)")
    print(f"ok: {len(pkgs)} package(s) consistent")


# ---------------------------------------------------------------- pack-check

def run(cmd, cwd):
    return subprocess.run(cmd, cwd=cwd, check=True, capture_output=True, text=True).stdout


def cmd_pack_check(args):
    nupkgs = Path(args[0]).resolve()
    only = args[args.index("--component") + 1] if "--component" in args else None
    selected = [p for p in packages() if only in (None, p["component"])]
    if not selected:
        raise SystemExit(f"no package with component '{only}'")
    errors = []
    for p in selected:
        stem = Path(p["dir"]).name
        found = sorted(nupkgs.glob(f"{stem}.*.nupkg"))
        found = [f for f in found if not f.name.endswith(".symbols.nupkg")]
        if len(found) != 1:
            errors.append(f"{stem}: expected exactly one .nupkg in {nupkgs}, found {[f.name for f in found]}")
        else:
            names = set(zipfile.ZipFile(found[0]).namelist())
            for need in (f"lib/netstandard2.1/{stem}.dll", f"lib/netstandard2.1/{stem}.xml", "README.md"):
                if need not in names:
                    errors.append(f"{found[0].name}: missing {need}")
            leaked = [n for n in names if n.endswith("PublicAPI.Shipped.txt") or n.endswith(".cs")]
            if leaked:
                errors.append(f"{found[0].name}: unexpected files {leaked}")
            if not list(nupkgs.glob(f"{stem}.*.snupkg")):
                errors.append(f"{stem}: no .snupkg symbol package")

        out = run(["npm", "pack", "--dry-run", "--json", "--pack-destination", tempfile.gettempdir()], ROOT / p["dir"])
        files = {f["path"] for f in json.loads(out)[0]["files"]}
        for f in sorted(files):
            if f.endswith(".meta"):
                if f[:-5] not in files:
                    errors.append(f"{stem} tarball: orphan {f}")
            elif f + ".meta" not in files:
                errors.append(f"{stem} tarball: {f} has no .meta")
        for bad in ("bin/", "obj/"):
            if any(f.startswith(bad) for f in files):
                errors.append(f"{stem} tarball contains {bad}")
        for bad_ext in (".csproj", "PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt"):
            if any(f.endswith(bad_ext) for f in files):
                errors.append(f"{stem} tarball contains {bad_ext} (add it, and its .meta, to .npmignore)")
        for need in ("package.json", "README.md", f"{stem}.asmdef"):
            if need not in files:
                errors.append(f"{stem} tarball: missing {need}")

    for e in errors:
        print("error:", e)
    if errors:
        raise SystemExit(f"{len(errors)} problem(s)")
    print(f"ok: {len(selected)} package(s) pack cleanly")


COMMANDS = {"matrix": cmd_matrix, "resolve": cmd_resolve, "check": cmd_check, "meta": cmd_meta, "pack-check": cmd_pack_check}

if __name__ == "__main__":
    if len(sys.argv) < 2 or sys.argv[1] not in COMMANDS:
        print(__doc__)
        raise SystemExit(2)
    COMMANDS[sys.argv[1]](sys.argv[2:])

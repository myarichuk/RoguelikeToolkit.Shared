# Releasing

Releases are automatic and per package. You merge a release PR; everything after that is a workflow.

```
conventional commits on master
        │
        ▼
release-please.yml  ──►  "chore: release master" PR (versions, CHANGELOGs)
        │  you review and merge it (merge commit or rebase, never squash a PR of many commits)
        ▼
tags  <component>-v<version>   +   GitHub Releases
        │  the tag push triggers
        ▼
publish.yml  ──►  build, test, pack, inspect  ──►  NuGet  +  UPM tarball on the Release  +  OpenUPM
```

`feat:` bumps the minor version, `fix:` the patch, `!` or a `BREAKING CHANGE:` footer bumps the minor while a package is
0.x (`bump-minor-pre-major`). A package with no relevant commits gets no release. A new package's first release is
`initial-version` from `release-please-config.json` (without it release-please uses 1.0.0).

## The `RELEASE_PLEASE_TOKEN` secret

`release-please.yml` authenticates with a personal access token instead of the built-in `GITHUB_TOKEN`. This is
deliberate: events created with `GITHUB_TOKEN` **do not trigger other workflows**. With it, the release PR would get
no CI run, and the tags and releases release-please creates would never start `publish.yml`, so nothing would ever
publish and nothing would say why.

### Create it

1. GitHub → your avatar → **Settings** → **Developer settings** → **Personal access tokens** → **Fine-grained tokens**
   → **Generate new token**.
2. **Token name:** `RoguelikeToolkit.Shared release-please`.
3. **Resource owner:** the account that owns the repo (`myarichuk`).
4. **Expiration:** pick the longest you are willing to rotate (fine-grained tokens expire; 90 days to 1 year is typical).
   Put the expiry date in your calendar now.
5. **Repository access:** **Only select repositories** → `RoguelikeToolkit.Shared`.
6. **Repository permissions** (everything else stays "No access"; *Metadata: Read-only* is added automatically):

   | Permission | Access | Why |
   |---|---|---|
   | Contents | Read and write | create the release branch, tags and GitHub Releases |
   | Pull requests | Read and write | open and update the release PR |
   | Issues | Read and write | create and apply the `autorelease:` labels (labels are an Issues API) |

7. **Generate token** and copy it. GitHub shows it once.

(A classic token with the `repo` scope also works, but it can touch every repository you own. Prefer the fine-grained one.)

### Store it

Repo → **Settings** → **Secrets and variables** → **Actions** → **Repository secrets** → **New repository secret**:
name `RELEASE_PLEASE_TOKEN` (exactly; the workflow reads `secrets.RELEASE_PLEASE_TOKEN`), value = the token.
Do not paste it into an issue, a PR, a commit or a workflow file.

### Check that it works

Push any `feat:`/`fix:` commit to `master`, then open **Actions → Release Please**.

- The run is green and a PR titled `chore: release master` exists or is updated.
- That PR has a **CI run** on it. (No checks at all on the release PR is the classic sign that `GITHUB_TOKEN` is being
  used, or the secret is empty.)
- After merging it, a `publish.yml` run starts for each new tag.

### Rotate it

Before it expires (or immediately if it leaks): create a new token the same way, overwrite the secret with the same
name, then **revoke the old token** under Developer settings. There is nothing else to change. An expired token shows up as
`Release Please` failing with `Bad credentials` / `401`, with no release PR updated.

### Things to know

- **The release PR is authored by the token's owner.** If you protect `master` and require an approving review, you
  cannot approve a PR you authored. Either allow yourself to bypass that rule for release PRs, or switch to a GitHub App
  (below).
- **The token carries your identity.** Anything release-please does appears as you in the history and audit log.
- **Forks** do not get the secret, so release-please simply does nothing there. That is intended.

### Alternative: a GitHub App (no expiry, no personal identity)

If you would rather not tie releases to a person: create a GitHub App owned by you or an org, with the same three
repository permissions as above, install it on this repository only, store its ID and private key as secrets
(`RELEASE_APP_ID`, `RELEASE_APP_PRIVATE_KEY`), and add a step before release-please:

```yaml
- uses: actions/create-github-app-token@<pin to a commit SHA, like every other action here>
  id: app-token
  with:
    app-id: ${{ secrets.RELEASE_APP_ID }}
    private-key: ${{ secrets.RELEASE_APP_PRIVATE_KEY }}
- uses: googleapis/release-please-action@<existing SHA>
  with:
    token: ${{ steps.app-token.outputs.token }}
```

Pull-request and tag events created by an App token trigger workflows just like a PAT's do, and the PR is authored by
the App, so you can approve it. `eng/packages.py check` requires action references to be pinned to full SHAs.

## Other one-time setup

- `PUBLISH_ENABLED` repository variable = `true` (**Settings → Secrets and variables → Actions → Variables**). Without
  it `publish.yml` still builds, tests, packs and attaches the UPM tarball, but does not push to NuGet or notify OpenUPM.
- A nuget.org **trusted publisher** for `publish.yml` (account menu → Trusted Publishing), so no API key is stored.
- OpenUPM registration of the packages (optional).

# RepositoryTemplate

Shared GitHub Actions building blocks for the Hawkynt repositories.

## `nuget-publish`

Publishes packages to nuget.org over [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing):
it exchanges the job's GitHub OIDC token for a short-lived API key, pushes every `.nupkg` and
`.snupkg` in a directory, then polls the flat-container index and **fails if a package was accepted
but never became available** (the silent-rejection case). When no Trusted Publishing policy is
configured it falls back to a stored API key.

### Usage

```yaml
jobs:
  publish-nuget:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      id-token: write          # required for Trusted Publishing
    steps:
      - uses: actions/download-artifact@v4
        with: { name: nuget-packages, path: dist-nuget }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }

      - uses: Hawkynt/RepositoryTemplate/nuget-publish@v1
        with:
          packages-path: dist-nuget
          user: ${{ secrets.NUGET_USER }}
          nuget-token: ${{ secrets.NUGET_TOKEN }}   # optional fallback
```

Trusted Publishing needs a policy on nuget.org (your username ▸ Trusted Publishing) naming the
repository and the workflow file that calls this action, and the calling job must grant
`id-token: write`.

### Inputs

| Input | Required | Default | Description |
|-------|----------|---------|-------------|
| `packages-path` | yes | — | Directory holding the `.nupkg`/`.snupkg` files to push. |
| `user` | no | `""` | nuget.org account name for Trusted Publishing. |
| `nuget-token` | no | `""` | Fallback API key, used only when no policy is configured. |
| `source` | no | `https://api.nuget.org/v3/index.json` | Push source. |
| `timeout-seconds` | no | `900` | How long to wait for availability before failing. |

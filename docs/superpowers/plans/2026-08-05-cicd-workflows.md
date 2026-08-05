# CI/CD Workflows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a CI test workflow and container image publishing (continuous plus tag-triggered release) to `psatool-busybar-agent`, modeled on the sibling `screengrabber` repo's workflows.

**Architecture:** Three new GitHub Actions workflow files under `.github/workflows/`, plus a one-line Dockerfile change so the existing multi-stage build has a named final stage the workflows can target. No SSH deploy step: unlike `screengrabber` (a hosted API `screengrabber` deploys to its own server), this app runs on whatever machine sits near a user's physical BUSY Bar, so there is no single server for CI to roll out to. The workflows only build and publish the image; users pull it themselves.

**Tech Stack:** GitHub Actions, Docker Buildx, GHCR, Docker Hub (conditional), .NET 10 SDK.

## Global Constraints

- Solution file: `PsaToolAgent.sln`. .NET SDK version: `10.0.x` (matches the Dockerfile's `mcr.microsoft.com/dotnet/sdk:10.0` base image).
- GHCR image name is computed dynamically from `${{ github.repository }}` (lowercased), matching `screengrabber`'s pattern — do not hardcode the GHCR path.
- Docker Hub image name is hardcoded: `homotechsual/psatool-busybar-agent` (matches `screengrabber`'s `homotechsual/screengrabber` convention).
- No SSH deploy job in any workflow. `publish.yml` (this plan's name for the continuous-publish workflow — deliberately not called `deploy.yml`, since it doesn't deploy anywhere) only builds and pushes images.
- This repo's CI workflow does not collect coverage or upload to Codecov (no Codecov account exists for this repo, unlike `screengrabber`) — just restore, build, test.
- These workflows will not run successfully until this repo has a GitHub remote (same standing situation as the existing `.github/workflows/deploy-docs.yml`); GHCR/Docker Hub credentials also won't exist until secrets are configured. That's expected — the workflows should be correct and ready, not functional today.
- Reference source for all three workflows: `J:\Projects\screengrabber\.github\workflows\ci.yml`, `deploy.yml`, and `release.yml`. This plan's file contents are already the adapted versions — copy them exactly as given below, do not re-derive from the source files yourselves.
- No unit tests exist for YAML workflow files. Verification is: re-read each created file and confirm it matches this plan's content exactly, and (for `release.yml` only, since it's a close line-for-line adaptation of `screengrabber`'s `release.yml`) a `git diff --no-index` comparison against that source file to confirm only the expected substitutions were made.
- This environment's Bash tool has an intermittent Cygwin fork issue (`cygheap read copy failed`). If a `git`/`docker` command via Bash fails with that error, retry the exact same command using the PowerShell tool instead.

---

### Task 1: CI test workflow

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Produces: a workflow that runs `dotnet restore/build/test` against `PsaToolAgent.sln` on push to `main`, on pull requests, and on manual dispatch. No outputs consumed by later tasks.

- [ ] **Step 1: Create `.github/workflows/ci.yml`**

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
  workflow_dispatch:

jobs:
  test:
    runs-on: ubuntu-latest
    permissions:
      contents: read
    steps:
      - uses: actions/checkout@v7

      - name: Set up .NET
        uses: actions/setup-dotnet@v6
        with:
          dotnet-version: 10.0.x

      - name: Restore dependencies
        run: dotnet restore PsaToolAgent.sln

      - name: Build solution
        run: dotnet build PsaToolAgent.sln -c Release --no-restore

      - name: Run tests
        run: dotnet test PsaToolAgent.sln -c Release --no-build
```

- [ ] **Step 2: Verify the file content**

Read `.github/workflows/ci.yml` back and confirm it matches Step 1's content exactly (2-space indentation throughout, no tabs, no trailing differences).

- [ ] **Step 3: Sanity-check the commands locally**

Run (PowerShell, from the repo root):

```powershell
dotnet restore PsaToolAgent.sln
dotnet build PsaToolAgent.sln -c Release --no-restore
dotnet test PsaToolAgent.sln -c Release --no-build
```

Expected: all three succeed, ending with `Passed! - Failed: 0` from the test run. This confirms the exact commands the workflow runs are valid on this solution, even though the workflow itself can't be executed locally.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "Add CI workflow to test on push and pull request"
```

---

### Task 2: Named final Docker stage and continuous image publishing

**Files:**
- Modify: `Dockerfile`
- Create: `.github/workflows/publish.yml`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `Dockerfile`'s runtime stage is now named `final`, which `publish.yml` (this task) and `release.yml` (Task 3) both target via `target: final`.

- [ ] **Step 1: Modify `Dockerfile`**

Add `AS final` to the runtime stage's `FROM` line — this is currently the only change needed; nothing else in the file changes:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/PsaToolAgent/PsaToolAgent.csproj src/PsaToolAgent/
RUN dotnet restore src/PsaToolAgent/PsaToolAgent.csproj
COPY src/PsaToolAgent/ src/PsaToolAgent/
RUN dotnet publish src/PsaToolAgent/PsaToolAgent.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "PsaToolAgent.dll"]
```

- [ ] **Step 2: Verify the Dockerfile still builds with the named stage**

Run (PowerShell, from the repo root):

```powershell
docker build --target final -t psatool-busybar-agent:test .
```

Expected: build succeeds (this requires Docker Desktop or another Docker daemon to be running locally; if none is available in this environment, skip this step and note it in your report — the change is a one-line stage name addition with no semantic effect on the build graph, so the risk of skipping is low, but say so explicitly rather than silently skipping).

- [ ] **Step 3: Create `.github/workflows/publish.yml`**

```yaml
name: Publish

on:
  push:
    branches: [main]
  workflow_dispatch:

env:
  REGISTRY: ghcr.io
  DOCKERHUB_IMAGE: homotechsual/psatool-busybar-agent

jobs:
  build-and-push:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write
    steps:
      - uses: actions/checkout@v7

      - name: Compute image name
        id: image
        run: echo "name=${REGISTRY}/$(echo '${{ github.repository }}' | tr '[:upper:]' '[:lower:]')" >> "$GITHUB_OUTPUT"

      - name: Compute Docker Hub image name
        id: dockerhub
        env:
          DOCKERHUB_USERNAME: ${{ secrets.DOCKERHUB_USERNAME }}
          DOCKERHUB_TOKEN: ${{ secrets.DOCKERHUB_TOKEN }}
        run: |
          if [ -n "$DOCKERHUB_USERNAME" ] && [ -n "$DOCKERHUB_TOKEN" ]; then
            echo "enabled=true" >> "$GITHUB_OUTPUT"
            echo "name=${DOCKERHUB_IMAGE}" >> "$GITHUB_OUTPUT"
          else
            echo "enabled=false" >> "$GITHUB_OUTPUT"
            echo "name=" >> "$GITHUB_OUTPUT"
          fi

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v4

      - name: Log in to GHCR
        uses: docker/login-action@v4
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Build and push image to GHCR
        uses: docker/build-push-action@v7
        with:
          context: .
          file: ./Dockerfile
          target: final
          push: true
          tags: |
            ${{ steps.image.outputs.name }}:edge
            ${{ steps.image.outputs.name }}:${{ github.sha }}
          cache-from: type=gha
          cache-to: type=gha,mode=max

      - name: Log in to Docker Hub
        if: ${{ steps.dockerhub.outputs.enabled == 'true' }}
        uses: docker/login-action@v4
        with:
          username: ${{ secrets.DOCKERHUB_USERNAME }}
          password: ${{ secrets.DOCKERHUB_TOKEN }}

      - name: Build and push image to Docker Hub
        if: ${{ steps.dockerhub.outputs.enabled == 'true' }}
        uses: docker/build-push-action@v7
        with:
          context: .
          file: ./Dockerfile
          target: final
          push: true
          tags: |
            ${{ steps.dockerhub.outputs.name }}:edge
            ${{ steps.dockerhub.outputs.name }}:${{ github.sha }}
          cache-from: type=gha
          cache-to: type=gha,mode=max
```

Note: this intentionally omits `screengrabber/deploy.yml`'s `deploy:` job (the SSH rollout to a server) and the `outputs:` block that job depended on — per this plan's Global Constraints, there is no server for this app to be deployed to automatically.

- [ ] **Step 4: Verify the file content**

Read `.github/workflows/publish.yml` back and confirm it matches Step 3's content exactly.

- [ ] **Step 5: Commit**

```bash
git add Dockerfile .github/workflows/publish.yml
git commit -m "Add named final Docker stage and continuous image publishing to GHCR and Docker Hub"
```

---

### Task 3: Tag-triggered release workflow

**Files:**
- Create: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: `Dockerfile`'s `final` stage from Task 2 (referenced via `target: final`, same as `publish.yml`).
- Produces: a workflow triggered by `v*.*.*` tags (or manual dispatch with a version input) that tests, publishes semver-tagged images to GHCR and (conditionally) Docker Hub, verifies the Docker Hub tags landed, and creates a GitHub Release.

- [ ] **Step 1: Create `.github/workflows/release.yml`**

This is a close adaptation of `screengrabber`'s `release.yml` — solution name and image names changed, nothing else:

```yaml
name: Release

on:
  push:
    tags:
      - "v*.*.*"
  workflow_dispatch:
    inputs:
      version:
        description: "Release version (for example 1.0.0 or v1.0.0)"
        required: true
        type: string

env:
  REGISTRY: ghcr.io
  DOCKERHUB_IMAGE: homotechsual/psatool-busybar-agent

jobs:
  test:
    runs-on: ubuntu-latest
    permissions:
      contents: read
    steps:
      - uses: actions/checkout@v7

      - name: Set up .NET
        uses: actions/setup-dotnet@v6
        with:
          dotnet-version: 10.0.x

      - name: Restore dependencies
        run: dotnet restore PsaToolAgent.sln

      - name: Build solution
        run: dotnet build PsaToolAgent.sln -c Release --no-restore

      - name: Run tests
        run: dotnet test PsaToolAgent.sln -c Release --no-build

  publish-images:
    needs: test
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write
    outputs:
      version: ${{ steps.version.outputs.version }}
      ghcr_image: ${{ steps.images.outputs.ghcr }}
      dockerhub_image: ${{ steps.images.outputs.dockerhub }}
      dockerhub_enabled: ${{ steps.images.outputs.dockerhub_enabled }}
    steps:
      - uses: actions/checkout@v7

      - name: Resolve release version
        id: version
        env:
          INPUT_VERSION: ${{ github.event.inputs.version }}
        run: |
          if [ -n "$INPUT_VERSION" ]; then
            VERSION="$INPUT_VERSION"
          else
            VERSION="${GITHUB_REF_NAME}"
          fi

          VERSION="${VERSION#v}"

          if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
            echo "Invalid version: $VERSION"
            exit 1
          fi

          MAJOR="${VERSION%%.*}"
          REST="${VERSION#*.}"
          MINOR="${REST%%.*}"

          echo "version=$VERSION" >> "$GITHUB_OUTPUT"
          echo "major=$MAJOR" >> "$GITHUB_OUTPUT"
          echo "minor=$MINOR" >> "$GITHUB_OUTPUT"

      - name: Resolve image names
        id: images
        env:
          DOCKERHUB_USERNAME: ${{ secrets.DOCKERHUB_USERNAME }}
          DOCKERHUB_TOKEN: ${{ secrets.DOCKERHUB_TOKEN }}
        run: |
          GHCR_IMAGE="${REGISTRY}/$(echo '${{ github.repository }}' | tr '[:upper:]' '[:lower:]')"
          echo "ghcr=$GHCR_IMAGE" >> "$GITHUB_OUTPUT"

          if [ -n "$DOCKERHUB_USERNAME" ] && [ -n "$DOCKERHUB_TOKEN" ]; then
            echo "dockerhub_enabled=true" >> "$GITHUB_OUTPUT"
            echo "dockerhub=${DOCKERHUB_IMAGE}" >> "$GITHUB_OUTPUT"
          else
            echo "dockerhub_enabled=false" >> "$GITHUB_OUTPUT"
            echo "dockerhub=" >> "$GITHUB_OUTPUT"
          fi

      - name: Require Docker Hub credentials for tagged releases
        if: ${{ github.event_name == 'push' && startsWith(github.ref, 'refs/tags/v') && steps.images.outputs.dockerhub_enabled != 'true' }}
        run: |
          echo "DOCKERHUB_USERNAME and DOCKERHUB_TOKEN must be set for tagged releases."
          exit 1

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v4

      - name: Log in to GHCR
        uses: docker/login-action@v4
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Build and push release image to GHCR
        uses: docker/build-push-action@v7
        with:
          context: .
          file: ./Dockerfile
          target: final
          push: true
          tags: |
            ${{ steps.images.outputs.ghcr }}:${{ steps.version.outputs.version }}
            ${{ steps.images.outputs.ghcr }}:${{ steps.version.outputs.major }}.${{ steps.version.outputs.minor }}
            ${{ steps.images.outputs.ghcr }}:${{ steps.version.outputs.major }}
            ${{ steps.images.outputs.ghcr }}:latest
            ${{ steps.images.outputs.ghcr }}:${{ github.sha }}
          cache-from: type=gha
          cache-to: type=gha,mode=max

      - name: Log in to Docker Hub
        if: ${{ steps.images.outputs.dockerhub_enabled == 'true' }}
        uses: docker/login-action@v4
        with:
          username: ${{ secrets.DOCKERHUB_USERNAME }}
          password: ${{ secrets.DOCKERHUB_TOKEN }}

      - name: Build and push release image to Docker Hub
        if: ${{ steps.images.outputs.dockerhub_enabled == 'true' }}
        uses: docker/build-push-action@v7
        with:
          context: .
          file: ./Dockerfile
          target: final
          push: true
          tags: |
            ${{ steps.images.outputs.dockerhub }}:${{ steps.version.outputs.version }}
          cache-from: type=gha
          cache-to: type=gha,mode=max

      - name: Create Docker Hub release alias tags
        if: ${{ steps.images.outputs.dockerhub_enabled == 'true' }}
        env:
          REPO: ${{ steps.images.outputs.dockerhub }}
          VERSION: ${{ steps.version.outputs.version }}
          MAJOR: ${{ steps.version.outputs.major }}
          MINOR: ${{ steps.version.outputs.minor }}
        run: |
          set -euo pipefail

          SOURCE="${REPO}:${VERSION}"
          docker buildx imagetools create -t "${REPO}:${MAJOR}.${MINOR}" "$SOURCE"
          docker buildx imagetools create -t "${REPO}:${MAJOR}" "$SOURCE"
          docker buildx imagetools create -t "${REPO}:latest" "$SOURCE"

      - name: Verify Docker Hub semver tags
        if: ${{ steps.images.outputs.dockerhub_enabled == 'true' }}
        env:
          REPO: ${{ steps.images.outputs.dockerhub }}
          VERSION: ${{ steps.version.outputs.version }}
          MAJOR: ${{ steps.version.outputs.major }}
          MINOR: ${{ steps.version.outputs.minor }}
        run: |
          set -euo pipefail

          REPO_PATH="${REPO%/*}/${REPO#*/}"
          REQUIRED_TAGS=("$VERSION" "$MAJOR.$MINOR" "$MAJOR" "latest")

          for attempt in 1 2 3 4 5 6; do
            TAGS_JSON="$(curl -fsSL "https://hub.docker.com/v2/repositories/${REPO_PATH}/tags?page_size=100")"
            missing=0

            for tag in "${REQUIRED_TAGS[@]}"; do
              if ! echo "$TAGS_JSON" | grep -q "\"name\":\"${tag}\""; then
                echo "Attempt ${attempt}: missing Docker Hub tag: ${tag}"
                missing=1
              fi
            done

            if [ "$missing" -eq 0 ]; then
              echo "Verified Docker Hub tags: $VERSION, $MAJOR.$MINOR, $MAJOR, latest"
              exit 0
            fi

            sleep 10
          done

          echo "Docker Hub semver tag verification failed after retries."
          exit 1

  release:
    needs: publish-images
    if: ${{ github.event_name == 'push' && startsWith(github.ref, 'refs/tags/v') }}
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - name: Create GitHub release
        uses: softprops/action-gh-release@v3
        with:
          tag_name: ${{ github.ref_name }}
          generate_release_notes: true
          body: |
            Published images:

            - GHCR: `${{ needs.publish-images.outputs.ghcr_image }}:${{ needs.publish-images.outputs.version }}`
            - Docker Hub: `${{ needs.publish-images.outputs.dockerhub_image }}:${{ needs.publish-images.outputs.version }}`
```

- [ ] **Step 2: Verify against the source workflow**

Run (PowerShell, from the repo root):

```powershell
git diff --no-index ..\screengrabber\.github\workflows\release.yml .github\workflows\release.yml
```

Expected diff, and nothing else: `Screengrabber.sln` becomes `PsaToolAgent.sln` (three occurrences, in the `restore`/`build`/`test` run lines); `DOCKERHUB_IMAGE: homotechsual/screengrabber` becomes `DOCKERHUB_IMAGE: homotechsual/psatool-busybar-agent`. If any other line differs, fix it to match the source workflow before continuing — everything else must be byte-for-byte identical, since it is generic GitHub Actions plumbing, not project specific.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Add tag-triggered release workflow with semver image tags and GitHub Release"
```

Note in the commit body: like `publish.yml`, this workflow cannot succeed until this repository has a GitHub remote; a tagged release additionally requires `DOCKERHUB_USERNAME`/`DOCKERHUB_TOKEN` secrets to be configured (the workflow enforces this itself via the "Require Docker Hub credentials for tagged releases" step) or Docker Hub publishing will be skipped and the run will fail that check on an actual tag push.

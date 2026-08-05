# psatool-busybar-agent Docs Site Design

## Context

`busybar-dotnet` (the sibling project this app consumes as a NuGet package) has a public
Docusaurus docs site at `busybar-dotnet.homotechsual.dev`, deployed via GitHub Actions to
Cloudflare Pages. This project gets a docs site built the same way: same Docusaurus scaffold,
same theme, same deploy pipeline shape, adapted content.

Unlike `busybar-dotnet`, this project is an application, not a published library, so some parts
of the source site do not carry over as is: there is no NuGet package to install, no auto
generated API reference, and no existing CI workflow to badge.

Style rule for every word of content written under this spec: no em dashes anywhere. Use commas,
parentheses, or separate sentences instead.

## Goal

A Docusaurus 3 site under `website/` in this repo, themed to match `busybar-dotnet`, with
hand written guide content covering what this app is, how to configure and deploy it, how its
priority logic works, and how to add a new PSA provider. Wired for Cloudflare Pages deployment
via GitHub Actions, even though the deployment cannot succeed yet (this repo has no GitHub
remote and no Cloudflare Pages project exists for it).

## Site structure

Copy `busybar-dotnet/website`'s directory layout into this repo's `website/`:

```
website/
  docs/
    intro.mdx
    configuration.mdx
    architecture.mdx
    providers.mdx
    deployment.mdx
  src/
    css/
      custom.css
  static/
    img/
      favicon.svg
      logo.svg
  docusaurus.config.ts
  sidebars.ts
  package.json
  tsconfig.json
  README.md
  .gitignore
  .yarnrc.yml
```

Not copied from `busybar-dotnet/website`:

- `docs/api/` and everything that generates it (`scripts/generate-api-docs.ps1`'s equivalent).
  There is no NuGet package for this app, so there is nothing to generate an API reference from.
- `src/pages/` (`index.tsx`, `index.module.css`, `markdown-page.mdx`) and
  `src/components/HomepageFeatures`. No custom marketing homepage; the intro doc IS the
  homepage (see below).
- `static/img/docusaurus-social-card.jpg`, `docusaurus.png`, `undraw_docusaurus_*.svg`,
  `icon-cloud.svg`, `icon-coverage.svg`, `icon-hardware.svg`, `icon-typed.svg`. These back the
  homepage feature grid and default template pages this site does not have.

## Homepage

No `src/pages/index.tsx`. `docs/intro.mdx` sets `slug: /` in its front matter, making it the
site's homepage directly. `sidebars.ts` still lists it first in `docsSidebar` so it also appears
normally in the sidebar and in prev/next navigation.

## Theming

Copy `src/css/custom.css` verbatim, including its existing header comment crediting the
homotechsual site family palette. Same color tokens (red or terracotta primary, rounded corners,
dark navbar and footer), same dark mode overrides, same footer styling block. No new theme
variables.

### Logo and favicon

`busybar-dotnet`'s logo is a 2x3 grid of rounded squares in the brand red, representing the BUSY
Bar's LED matrix. This project's logo is a different but related mark: three stacked rounded
bars, colored to match this app's own priority tiers as already defined in
`BusyBarRenderer.cs` (`CriticalTriggerColor` red `#FF0000`, `CriticalP2Color` orange `#FFA500`,
`CriticalP3Color` yellow `#FFFF00`), on a transparent background (no backdrop rect, matching the
existing pattern so the mark does not disappear or clash against the navbar).

`static/img/logo.svg`:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32" width="32" height="32" role="img" aria-label="psatool-busybar-agent logo">
  <rect x="4" y="4" width="24" height="6" rx="1.5" fill="#FF0000" />
  <rect x="4" y="13" width="24" height="6" rx="1.5" fill="#FFA500" />
  <rect x="4" y="22" width="24" height="6" rx="1.5" fill="#FFFF00" />
</svg>
```

`static/img/favicon.svg`: the same three bars, with a dark backdrop rect (`#263141`, matching
the navbar background) behind them, following `busybar-dotnet`'s existing convention that the
favicon keeps a backdrop (it sits on the browser tab, not on an identically colored navbar) while
the navbar logo does not.

No `themeConfig.image` (social card) field. `busybar-dotnet` uses a generated
`docusaurus-social-card.jpg`; this site skips that asset rather than fabricate a placeholder one,
since the field is optional.

## docusaurus.config.ts

Adapted from `busybar-dotnet/website/docusaurus.config.ts`:

- `title`: `'PSA BusyBar Agent'`
- `tagline`: `'A priority-ranked helpdesk dashboard for the BUSY Bar, driven by your PSA'`
- `favicon`: `'img/favicon.svg'`
- `url`: `'https://psatool-busybar-agent.homotechsual.dev'`
- `baseUrl`: `'/'`
- `organizationName`: `'homotechsual'`
- `projectName`: `'psatool-busybar-agent'`
- `onBrokenLinks`: `'throw'` (no anchor-injection step exists here, so no need for the
  `onBrokenAnchors: 'ignore'` busybar-dotnet carries for its generated API docs; leave
  `onBrokenAnchors` at its Docusaurus default)
- `presets` classic docs: `sidebarPath: './sidebars.ts'`, `routeBasePath: '/'`,
  `editUrl: 'https://github.com/homotechsual/psatool-busybar-agent/tree/main/website/'`, blog
  disabled, `customCss: './src/css/custom.css'`
- `themeConfig.colorMode`: same as busybar-dotnet (`defaultMode: 'dark'`, switch enabled, respect
  `prefers-color-scheme`)
- `themeConfig.navbar`: `title: 'PSA BusyBar Agent'`, logo as above, one `docSidebar` item
  (`sidebarId: 'docsSidebar'`, `label: 'Docs'`), plus the GitHub link item (same shape as
  busybar-dotnet's, pointed at this repo). No second sidebar item, since there is no API
  reference.
- `themeConfig.footer`: `style: 'dark'`. "Docs" column links to Getting Started (`/`),
  Configuration (`/configuration`), Architecture (`/architecture`), Adding a PSA provider
  (`/providers`), and Deployment (`/deployment`). "More" column has only a GitHub link (no NuGet
  equivalent to link to). Same copyright and "Designed with (heart) by homotechsual" markup as
  busybar-dotnet, unchanged (this project shares the same MJCO copyright line and the same
  homotechsual site family attribution).
- `themeConfig.prism`: same as busybar-dotnet (`prismThemes.github` light, `prismThemes.dracula`
  dark).

## sidebars.ts

```ts
const sidebars: SidebarsConfig = {
  docsSidebar: ['intro', 'configuration', 'architecture', 'providers', 'deployment'],
};
```

No `apiSidebar`.

## package.json

Same `name: 'website'`, same `packageManager` (yarn via Corepack), same `engines.node: '>=20.0'`,
same dependency set as busybar-dotnet's website (`@docusaurus/core`, `@docusaurus/faster`,
`@docusaurus/preset-classic`, `@mdx-js/react`, `clsx`, `prism-react-renderer`, `react`,
`react-dom`, plus the matching devDependencies), same `browserslist` block. Scripts drop
`generate-api-docs` and `build:full` (nothing to generate); everything else (`start`, `build`,
`swizzle`, `deploy`, `clear`, `serve`, `write-translations`, `write-heading-ids`, `typecheck`)
stays.

## Content pages

Every page below draws from this repo's existing `README.md` and
`docs/superpowers/specs/2026-07-31-psatool-busybar-agent-design.md`, rewritten in sentence case
prose with no em dashes, matching the sentence case convention already adopted for the BUSY Bar
display text itself.

### `docs/intro.mdx` (slug: `/`)

- Front matter: `sidebar_position: 1`, `slug: /`
- Heading: "Getting started"
- One paragraph describing what the app does: polls a PSA (Halo first, others pluggable) on a
  timer, evaluates a priority order, and renders the result to a physical BUSY Bar over the
  network.
- A small architecture diagram in a fenced code block, reusing the pipeline from the design spec:
  `IPsaDataProvider -> PsaSnapshot -> PriorityEngine -> DashboardState -> BusyBarRenderer`.
- "Quick start" section: clone the repo, `cp .env.example .env` and fill in Halo credentials,
  `docker compose up -d --build`. No NuGet install section (this app is not installed as a
  package).
- Links to the other four pages.
- No badges section. This repo has no CI workflow, no published package, and no coverage
  reporting yet, so there is nothing genuine to badge; do not fabricate any.

### `docs/configuration.mdx`

- Front matter: `sidebar_position: 2`
- The configuration table from `README.md`, reproduced as a Markdown table: `Psa:Provider`,
  `Psa:PollIntervalSeconds`, `Psa:SlaRiskThresholdMinutes`, `Psa:Halo:BaseUrl`,
  `Psa:Halo:ClientId` / `ClientSecret`, `Psa:Halo:Scope`, `Psa:Halo:OrganisationId`,
  `BusyBar:Address`, `Dashboard:HeaderText`, `Dashboard:DisplayCycleSeconds`, with the same
  defaults and descriptions.
- A short note on the three ways to set configuration: `appsettings.json`, environment variables
  (double underscore nesting), or `dotnet user-secrets` for local development, and never
  committing real `ClientId` or `ClientSecret` values.

### `docs/architecture.mdx`

- Front matter: `sidebar_position: 3`
- The component pipeline diagram (same as intro's, expanded).
- One subsection per component, each two or three sentences: `IPsaDataProvider`,
  `HaloPsaDataProvider`, `PsaSnapshot`, `PriorityEngine`, `DashboardState`, `BusyBarRenderer`,
  `PollingBackgroundService`, drawn from the design spec's component descriptions.
- The five level priority order `PriorityEngine` evaluates top to bottom, as a numbered list:
  rank 1 tickets present, SLA risk tickets present, VIP customer tickets present, unassigned
  tickets present, otherwise normal.
- A short note on the display's page cycling: when CRITICAL mode has more than one thing to show
  (a rank 2 or rank 3 tier, or SLA risk tickets alongside whatever triggered CRITICAL), the
  display cycles between pages every `Dashboard:DisplayCycleSeconds` seconds.

### `docs/providers.mdx`

- Front matter: `sidebar_position: 4`
- Heading: "Adding a PSA provider"
- Explains the seam: `IPsaDataProvider` has one method, `GetSnapshotAsync`, returning a
  `PsaSnapshot`. A new provider is a new class implementing that interface plus a DI
  registration, not a rewrite of the worker.
- Walks through `PsaSnapshot`'s fields and what each means for a new provider to populate:
  `OpenTicketCount`, `PriorityCounts` (rank keyed, provider agnostic, rank 1 is always the most
  urgent tier whatever a given PSA calls it), the optional `PriorityNames` (a pure display
  enrichment, leave null if the PSA has no such concept), `SlaRiskTickets` (every ticket with a
  known SLA timer, unfiltered by any threshold, since `PriorityEngine` alone applies the
  threshold), `UnassignedTicketCount`, `VipTickets`, and the optional `OrganizationName`.
- Explains registration: add a branch in `Program.cs`'s provider selection (alongside the
  existing `Halo` branch) that registers the new implementation against `IPsaDataProvider`, and
  select it by setting `Psa:Provider` to the new provider's name in configuration.
- Notes that authentication is entirely up to the provider (Halo's OAuth2 client credentials flow
  in `HaloAuthClient` is Halo specific, not part of the `IPsaDataProvider` contract).

### `docs/deployment.mdx`

- Front matter: `sidebar_position: 5`
- "Least privilege" subsection: register a dedicated read only Halo API client, do not reuse an
  admin scoped one, verify the configured scope actually exists and is granted; the container
  runs as a non root user with capabilities dropped, `no-new-privileges` set, and a read only root
  filesystem with a `tmpfs` `/tmp`.
- "Docker networking" subsection: the BUSY Bar presents as a USB Ethernet adapter with its own
  IP, so reaching it from a container is a network routing question. Linux hosts use
  `network_mode: host`. Windows (Docker Desktop, WSL2) needs either mirrored networking mode
  bridging the adapter into WSL2, or running the worker directly on Windows instead of in Docker.
  Includes the `curl -sf http://<BusyBar address>/api/version` verification step, run from the
  Docker host, not from inside the container.
- "Deploy" subsection: `cp .env.example .env` and fill in the Halo credentials and base URL, then
  `docker compose up -d --build`.

## Other copied files

- `tsconfig.json`: copied verbatim from `busybar-dotnet/website`. It is generic Docusaurus TS
  config with nothing project specific in it.
- `.yarnrc.yml`: copied verbatim (`nodeLinker: node-modules`, `enableScripts: true`).
- `.gitignore`: copied, minus the `/docs/api` entry and its comment. That entry exists in
  busybar-dotnet to keep the generated API reference out of version control; this site has no
  generated `docs/api`, so the entry and its comment do not apply here.

## Deploy workflow

Copy `.github/workflows/deploy-docs.yml` into this repo's `.github/workflows/`, adapted:

- Drop the `.NET` setup step, the `dotnet tool restore` step, and the whole "build the library,
  regenerate the API reference" step, since there is no library build or API doc generation here.
- Keep the Node and Yarn setup steps, `yarn install --immutable`, and the deploy step, but the
  build command becomes plain `yarn build` (not `yarn build:full`, which no longer exists).
- `cloudflare/wrangler-action` step: `command: 'pages deploy website/build --project-name=psatool-busybar-agent --branch=...'`, same branch resolution logic as busybar-dotnet's (falls back to the ref name so a detached `actions/checkout` HEAD does not get treated as a non production deploy).
- Same trigger conditions (push to `main`, PR open or sync from a non fork, manual dispatch), same
  concurrency group, same deployment summary and PR/commit comment steps, with the repository
  name in the summary text updated to this project's.
- This workflow will fail to deploy until this repo has a GitHub remote and a Cloudflare Pages
  project named `psatool-busybar-agent` exists with `CLOUDFLARE_ACCOUNT_ID` and
  `CLOUDFLARE_API_TOKEN` secrets configured. That is expected and out of scope for this work; the
  workflow is wired up ready for when that happens.

## website/README.md

Adapted from busybar-dotnet's: same install (`corepack enable`, `yarn install`) and local dev
(`yarn start`) sections, same "uses Yarn, not npm" note. Drops the API reference generation
paragraph entirely (nothing to regenerate). Build section becomes just `yarn build`, static
output to `build/`.

## What this explicitly does not include

- No auto generated API or type reference.
- No custom marketing homepage or feature-icon grid.
- No CI workflow for the main app (build or test on push); this spec is docs site scope only.
- No actual Cloudflare Pages project creation, GitHub remote creation, or DNS setup. Those are
  external actions outside this repo, not something a docs site PR can do.

## Testing

Docusaurus sites do not carry unit tests. Verification is: `yarn build` completes with no broken
link errors (`onBrokenLinks: 'throw'` catches internal link mistakes across the five pages), and
`yarn start` serves the site locally for a visual check of the homepage, sidebar, dark mode
toggle, and each of the five pages.

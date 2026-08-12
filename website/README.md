# website

The PSA BusyBar Agent docs site, built with [Docusaurus](https://docusaurus.io/). Deployed to
[psatool.busy.homotechsual.dev](https://psatool.busy.homotechsual.dev) via
Cloudflare Pages (see `../.github/workflows/deploy-docs.yml`).

Uses [Yarn](https://yarnpkg.com/) (pinned through Corepack, see `packageManager` in
`package.json`), not npm.

## Install

```bash
corepack enable
yarn install
```

## Local development

```bash
yarn start
```

Starts a local dev server and opens a browser window. Most changes reload live.

## Build

```bash
yarn build
```

Static output goes to `build/`.

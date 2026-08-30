# Finance App Frontend

React and Vite frontend for the Finance App.

Development and production are managed from the monorepo root with Docker Compose. See [the root README](../README.md) for setup, environment variables, commands, migrations, and troubleshooting.

## Static artwork

App-owned artwork belongs in `public/static` and should be referenced in frontend code with
`staticAssetUrl` from `src/lib/staticAssets.js`. Vite serves files in this directory at
`/static/<filename>` during development and copies them unchanged into the production build.

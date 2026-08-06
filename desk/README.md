# MorphDB Studio

Desktop database management client for MorphDB, similar to pgAdmin, DBeaver, or TablePlus.

## Status: parked

**Not released, and not currently maintained.** No version has ever been published — the feature
list below is a plan, not a changelog. Treat the source here as work in progress that is paused,
not as a component you can install.

What being parked means in practice:

- **No releases.** `release-desk.yml` no longer runs on a tag; it can only be dispatched by hand,
  and even then it builds installers as artifacts without publishing.
- **No dependency upkeep.** Known advisories in the dependency graph are recorded, not chased. The
  server is unaffected — it shares no dependencies with this client.
- **Checks follow the code.** `ci-desk.yml` runs typecheck, tests and a critical-severity audit on
  changes under `desk/`, so nothing here rots silently, but a push elsewhere pays nothing for it.

Being parked is a statement about attention, not about intent: the client is paused because the
server is where the work is, and the state is written down here so that the repository and the
reader agree on it. Unparking is a decision, and reverting each bullet above is what it takes.

## Tech Stack

- **Electron** - Cross-platform desktop framework
- **Vite** - Fast build tooling via electron-vite
- **React 19** - UI framework
- **TypeScript** - Type safety
- **Tailwind CSS v4** - Styling
- **TanStack Query** - Data fetching and caching
- **TanStack Table** - Data grid virtualization
- **Zustand** - State management
- **Lucide React** - Icons

## Features

- [ ] Connection management (multiple servers)
- [ ] Project and table explorer
- [ ] Table CRUD (create, edit, delete tables)
- [ ] Column management (add, edit, delete columns)
- [ ] Data grid with inline editing
- [ ] Bulk import/export (CSV, JSON, Excel)
- [ ] Query console (OData, GraphQL)
- [ ] Dark/light theme

## Development

```bash
# Install dependencies
npm install

# Start development mode
npm run dev

# Build for production
npm run build

# Build for specific platform
npm run build:win
npm run build:mac
npm run build:linux
```

## Project Structure

```
desk/
├── src/
│   ├── main/           # Electron main process
│   │   └── index.ts
│   ├── preload/        # Preload scripts (IPC bridge)
│   │   └── index.ts
│   └── renderer/       # React application
│       ├── components/
│       │   ├── dialogs/
│       │   ├── layout/
│       │   └── ui/
│       ├── hooks/
│       ├── lib/
│       ├── stores/
│       ├── styles/
│       ├── types/
│       ├── App.tsx
│       └── main.tsx
├── build/              # Build resources (icons)
├── resources/          # Static resources
└── electron.vite.config.ts
```

## License

Apache License 2.0

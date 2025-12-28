# MorphDB Studio

Desktop database management client for MorphDB, similar to pgAdmin, DBeaver, or TablePlus.

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

MIT

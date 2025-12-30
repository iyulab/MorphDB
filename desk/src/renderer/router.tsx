import { createHashRouter, Navigate } from 'react-router-dom'
import { RootLayout } from './layouts/RootLayout'
import { ExplorerPage } from './routes/explorer'
import { SettingsPage } from './routes/settings'
import { ProjectsPage } from './routes/projects'
import { ErrorBoundary } from './components/ErrorBoundary'

export const router = createHashRouter([
  {
    path: '/',
    element: <RootLayout />,
    errorElement: <ErrorBoundary />,
    children: [
      {
        index: true,
        element: <Navigate to="/explorer" replace />
      },
      {
        path: 'explorer',
        element: <ExplorerPage />
      },
      {
        path: 'explorer/:tableName',
        element: <ExplorerPage />
      },
      {
        path: 'projects',
        element: <ProjectsPage />
      },
      {
        path: 'settings',
        element: <SettingsPage />
      }
    ]
  }
])

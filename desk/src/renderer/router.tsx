import { createHashRouter, Navigate } from 'react-router-dom'
import { RootLayout } from './layouts/RootLayout'
import { ExplorerPage } from './routes/explorer'
import { SettingsPage } from './routes/settings'
import { ProjectsPage } from './routes/projects'
import { ViewsPage } from './routes/views'
import { WebhooksPage } from './routes/webhooks'
import { OrganizationsPage } from './routes/organizations'
import { BackupsPage } from './routes/backups'
import { AuditPage } from './routes/audit'
import { QuotaPage } from './routes/quota'
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
        path: 'views',
        element: <ViewsPage />
      },
      {
        path: 'webhooks',
        element: <WebhooksPage />
      },
      {
        path: 'organizations',
        element: <OrganizationsPage />
      },
      {
        path: 'backups',
        element: <BackupsPage />
      },
      {
        path: 'audit',
        element: <AuditPage />
      },
      {
        path: 'quota',
        element: <QuotaPage />
      },
      {
        path: 'settings',
        element: <SettingsPage />
      }
    ]
  }
])

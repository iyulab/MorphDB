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
import { SecurityPage } from './routes/security'
import { SsoPage } from './routes/sso'
import { RouteErrorBoundary } from './components/ErrorBoundary'

export const router = createHashRouter([
  {
    path: '/',
    element: <RootLayout />,
    errorElement: <RouteErrorBoundary />,
    children: [
      {
        index: true,
        element: <Navigate to="/explorer" replace />
      },
      {
        path: 'explorer',
        element: <ExplorerPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'explorer/:tableName',
        element: <ExplorerPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'projects',
        element: <ProjectsPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'views',
        element: <ViewsPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'webhooks',
        element: <WebhooksPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'organizations',
        element: <OrganizationsPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'backups',
        element: <BackupsPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'audit',
        element: <AuditPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'quota',
        element: <QuotaPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'security',
        element: <SecurityPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'sso',
        element: <SsoPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'settings',
        element: <SettingsPage />,
        errorElement: <RouteErrorBoundary />
      }
    ]
  }
])

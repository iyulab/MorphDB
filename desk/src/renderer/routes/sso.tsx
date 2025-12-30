import { useState, type ReactElement } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  KeyRound,
  Plus,
  Trash2,
  Edit,
  Play,
  Pause,
  TestTube,
  RefreshCw,
  AlertTriangle,
  Check,
  X,
  ExternalLink,
  ChevronDown,
  Building2,
  Users,
  Settings2
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { useConnectionStore } from '@/stores/connectionStore'
import {
  MorphDBClient,
  type SsoConfigApiResponse,
  type SsoProviderType,
  type SsoConfigStatus,
  type OrganizationRole,
  type CreateSsoConfigApiRequest,
  type UpdateSsoConfigApiRequest,
  type OrganizationApiResponse
} from '@/lib/api'
import { cn } from '@/lib/utils'

const providerLabels: Record<SsoProviderType, string> = {
  oidc: 'OpenID Connect',
  entraId: 'Microsoft Entra ID',
  google: 'Google Workspace',
  okta: 'Okta',
  auth0: 'Auth0',
  keycloak: 'Keycloak',
  saml: 'SAML 2.0'
}

const providerLogos: Record<SsoProviderType, string> = {
  oidc: 'OIDC',
  entraId: 'MS',
  google: 'G',
  okta: 'OK',
  auth0: 'A0',
  keycloak: 'KC',
  saml: 'SAML'
}

const statusColors: Record<SsoConfigStatus, string> = {
  disabled: 'bg-muted text-muted-foreground',
  active: 'bg-success/10 text-success',
  testing: 'bg-warning/10 text-warning',
  error: 'bg-destructive/10 text-destructive'
}

const roleLabels: Record<OrganizationRole, string> = {
  Member: 'Member',
  Admin: 'Admin',
  Owner: 'Owner',
  Viewer: 'Viewer'
}

export function SsoPage(): ReactElement {
  const { activeConnection, getApiKey } = useConnectionStore()
  const queryClient = useQueryClient()
  const [selectedOrganizationId, setSelectedOrganizationId] = useState<string>('')
  const [showCreateModal, setShowCreateModal] = useState(false)
  const [editingConfig, setEditingConfig] = useState<SsoConfigApiResponse | null>(null)
  const [testResult, setTestResult] = useState<{
    configId: string
    success: boolean
    message: string
  } | null>(null)

  const createClient = async (): Promise<MorphDBClient | null> => {
    if (!activeConnection) return null
    const apiKey = await getApiKey(activeConnection.id)
    if (!apiKey) return null
    return new MorphDBClient({
      url: activeConnection.url,
      apiKey,
      tenantId: activeConnection.tenantId
    })
  }

  // Load organizations
  const { data: organizations, isLoading: orgsLoading } = useQuery({
    queryKey: ['organizations'],
    queryFn: async () => {
      const client = await createClient()
      if (!client) throw new Error('No connection')
      return client.listOrganizations()
    },
    enabled: !!activeConnection
  })

  // Load SSO configs for selected organization
  const { data: configs, isLoading: configsLoading } = useQuery({
    queryKey: ['sso-configs', selectedOrganizationId],
    queryFn: async () => {
      const client = await createClient()
      if (!client) throw new Error('No connection')
      return client.listSsoConfigs(selectedOrganizationId)
    },
    enabled: !!selectedOrganizationId
  })

  const deleteMutation = useMutation({
    mutationFn: async (configId: string) => {
      const client = await createClient()
      if (!client) throw new Error('No connection')
      await client.deleteSsoConfig(configId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['sso-configs', selectedOrganizationId] })
    }
  })

  const activateMutation = useMutation({
    mutationFn: async (configId: string) => {
      const client = await createClient()
      if (!client) throw new Error('No connection')
      await client.activateSsoConfig(configId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['sso-configs', selectedOrganizationId] })
    }
  })

  const deactivateMutation = useMutation({
    mutationFn: async (configId: string) => {
      const client = await createClient()
      if (!client) throw new Error('No connection')
      await client.deactivateSsoConfig(configId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['sso-configs', selectedOrganizationId] })
    }
  })

  const testMutation = useMutation({
    mutationFn: async (configId: string) => {
      const client = await createClient()
      if (!client) throw new Error('No connection')
      return { configId, result: await client.testSsoConfig(configId) }
    },
    onSuccess: (data) => {
      setTestResult({
        configId: data.configId,
        success: data.result.success,
        message: data.result.message
      })
      queryClient.invalidateQueries({ queryKey: ['sso-configs', selectedOrganizationId] })
    }
  })

  const formatDate = (dateStr?: string): string => {
    if (!dateStr) return 'Never'
    return new Date(dateStr).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    })
  }

  if (!activeConnection) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="text-center text-muted-foreground">
          <KeyRound className="mx-auto mb-4 h-12 w-12 opacity-50" />
          <p>Select a connection to manage SSO configuration</p>
        </div>
      </div>
    )
  }

  return (
    <div className="flex h-full flex-col">
      {/* Header */}
      <div className="border-b px-6 py-4">
        <h1 className="text-xl font-semibold">Single Sign-On (SSO)</h1>
        <p className="text-sm text-muted-foreground">
          Configure identity providers for organization authentication
        </p>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-y-auto p-6">
        <div className="space-y-6">
          {/* Organization Selector */}
          <div className="rounded-lg border p-4">
            <label className="mb-2 flex items-center gap-2 text-sm font-medium">
              <Building2 className="h-4 w-4" />
              Select Organization
            </label>
            {orgsLoading ? (
              <div className="flex items-center gap-2 text-sm text-muted-foreground">
                <RefreshCw className="h-4 w-4 animate-spin" />
                Loading organizations...
              </div>
            ) : organizations && organizations.length > 0 ? (
              <select
                value={selectedOrganizationId}
                onChange={(e) => {
                  setSelectedOrganizationId(e.target.value)
                  setTestResult(null)
                }}
                className="w-full max-w-md rounded-md border bg-background px-3 py-2 text-sm"
              >
                <option value="">Choose an organization...</option>
                {organizations.map((org: OrganizationApiResponse) => (
                  <option key={org.organizationId} value={org.organizationId}>
                    {org.name}
                  </option>
                ))}
              </select>
            ) : (
              <div className="text-sm text-muted-foreground">
                No organizations found. Create an organization first.
              </div>
            )}
          </div>

          {/* SSO Configurations */}
          {selectedOrganizationId && (
            <>
              {/* Header with Add button */}
              <div className="flex items-center justify-between">
                <div>
                  <h2 className="text-lg font-medium">SSO Configurations</h2>
                  <p className="text-sm text-muted-foreground">
                    Configure identity providers for this organization
                  </p>
                </div>
                <Button onClick={() => setShowCreateModal(true)}>
                  <Plus className="mr-2 h-4 w-4" />
                  Add Provider
                </Button>
              </div>

              {/* Configs List */}
              {configsLoading ? (
                <div className="flex items-center justify-center py-12">
                  <RefreshCw className="h-6 w-6 animate-spin text-muted-foreground" />
                </div>
              ) : configs && configs.length > 0 ? (
                <div className="space-y-4">
                  {configs.map((config: SsoConfigApiResponse) => (
                    <div
                      key={config.ssoConfigId}
                      className={cn(
                        'rounded-lg border p-4 transition-colors',
                        config.status === 'disabled' && 'opacity-60'
                      )}
                    >
                      <div className="flex items-start justify-between">
                        <div className="flex items-start gap-4">
                          {/* Provider Icon */}
                          <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-primary/10 text-primary font-bold">
                            {providerLogos[config.providerType]}
                          </div>

                          {/* Config Info */}
                          <div>
                            <div className="flex items-center gap-2">
                              <h3 className="font-medium">{config.name}</h3>
                              <span
                                className={cn(
                                  'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
                                  statusColors[config.status]
                                )}
                              >
                                {config.status}
                              </span>
                            </div>
                            <p className="text-sm text-muted-foreground">
                              {providerLabels[config.providerType]}
                            </p>
                            <div className="mt-2 space-y-1 text-xs text-muted-foreground">
                              <div className="flex items-center gap-4">
                                <span>
                                  <strong>Authority:</strong> {config.authority}
                                </span>
                              </div>
                              <div className="flex items-center gap-4">
                                <span>
                                  <strong>Client ID:</strong> {config.clientId}
                                </span>
                                <span>
                                  <strong>Secret:</strong>{' '}
                                  {config.hasClientSecret ? '••••••••' : 'Not set'}
                                </span>
                              </div>
                              <div className="flex items-center gap-4">
                                <span>
                                  <strong>Auto-provision:</strong>{' '}
                                  {config.autoProvisionUsers ? 'Yes' : 'No'}
                                </span>
                                <span>
                                  <strong>Default Role:</strong> {roleLabels[config.defaultRole]}
                                </span>
                              </div>
                              {config.allowedDomains && config.allowedDomains.length > 0 && (
                                <div>
                                  <strong>Allowed Domains:</strong>{' '}
                                  {config.allowedDomains.join(', ')}
                                </div>
                              )}
                              <div className="flex items-center gap-4 pt-1">
                                <span>Created: {formatDate(config.createdAt)}</span>
                                <span>Last used: {formatDate(config.lastUsedAt)}</span>
                              </div>
                            </div>

                            {/* Error Message */}
                            {config.lastError && (
                              <div className="mt-2 flex items-start gap-2 rounded-md bg-destructive/10 p-2 text-xs text-destructive">
                                <AlertTriangle className="h-3 w-3 mt-0.5 flex-shrink-0" />
                                {config.lastError}
                              </div>
                            )}

                            {/* Test Result */}
                            {testResult && testResult.configId === config.ssoConfigId && (
                              <div
                                className={cn(
                                  'mt-2 flex items-start gap-2 rounded-md p-2 text-xs',
                                  testResult.success
                                    ? 'bg-success/10 text-success'
                                    : 'bg-destructive/10 text-destructive'
                                )}
                              >
                                {testResult.success ? (
                                  <Check className="h-3 w-3 mt-0.5" />
                                ) : (
                                  <X className="h-3 w-3 mt-0.5" />
                                )}
                                {testResult.message}
                              </div>
                            )}
                          </div>
                        </div>

                        {/* Actions */}
                        <div className="flex items-center gap-1">
                          <Button
                            variant="ghost"
                            size="icon"
                            title="Test Configuration"
                            onClick={() => testMutation.mutate(config.ssoConfigId)}
                            disabled={testMutation.isPending}
                          >
                            <TestTube className="h-4 w-4" />
                          </Button>
                          {config.status === 'active' ? (
                            <Button
                              variant="ghost"
                              size="icon"
                              title="Deactivate"
                              onClick={() => deactivateMutation.mutate(config.ssoConfigId)}
                              disabled={deactivateMutation.isPending}
                            >
                              <Pause className="h-4 w-4" />
                            </Button>
                          ) : (
                            <Button
                              variant="ghost"
                              size="icon"
                              title="Activate"
                              onClick={() => activateMutation.mutate(config.ssoConfigId)}
                              disabled={activateMutation.isPending}
                            >
                              <Play className="h-4 w-4 text-success" />
                            </Button>
                          )}
                          <Button
                            variant="ghost"
                            size="icon"
                            title="Edit"
                            onClick={() => setEditingConfig(config)}
                          >
                            <Edit className="h-4 w-4" />
                          </Button>
                          <Button
                            variant="ghost"
                            size="icon"
                            title="Delete"
                            onClick={() => deleteMutation.mutate(config.ssoConfigId)}
                            disabled={deleteMutation.isPending}
                          >
                            <Trash2 className="h-4 w-4 text-destructive" />
                          </Button>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
                <div className="rounded-lg border border-dashed p-8 text-center text-muted-foreground">
                  <KeyRound className="mx-auto mb-2 h-8 w-8 opacity-50" />
                  <p>No SSO configurations for this organization</p>
                  <Button
                    variant="link"
                    size="sm"
                    className="mt-2"
                    onClick={() => setShowCreateModal(true)}
                  >
                    Add your first identity provider
                  </Button>
                </div>
              )}
            </>
          )}
        </div>
      </div>

      {/* Create Modal */}
      {showCreateModal && (
        <SsoConfigModal
          createClient={createClient}
          organizationId={selectedOrganizationId}
          onClose={() => setShowCreateModal(false)}
          onSaved={() => {
            setShowCreateModal(false)
            queryClient.invalidateQueries({ queryKey: ['sso-configs', selectedOrganizationId] })
          }}
        />
      )}

      {/* Edit Modal */}
      {editingConfig && (
        <SsoConfigModal
          createClient={createClient}
          organizationId={selectedOrganizationId}
          existingConfig={editingConfig}
          onClose={() => setEditingConfig(null)}
          onSaved={() => {
            setEditingConfig(null)
            queryClient.invalidateQueries({ queryKey: ['sso-configs', selectedOrganizationId] })
          }}
        />
      )}
    </div>
  )
}

// ============================================================================
// SSO Config Modal
// ============================================================================

interface SsoConfigModalProps {
  createClient: () => Promise<MorphDBClient | null>
  organizationId: string
  existingConfig?: SsoConfigApiResponse
  onClose: () => void
  onSaved: () => void
}

function SsoConfigModal({
  createClient,
  organizationId,
  existingConfig,
  onClose,
  onSaved
}: SsoConfigModalProps): ReactElement {
  const isEdit = !!existingConfig

  const [name, setName] = useState(existingConfig?.name || '')
  const [providerType, setProviderType] = useState<SsoProviderType>(
    existingConfig?.providerType || 'oidc'
  )
  const [authority, setAuthority] = useState(existingConfig?.authority || '')
  const [clientId, setClientId] = useState(existingConfig?.clientId || '')
  const [clientSecret, setClientSecret] = useState('')
  const [scopes, setScopes] = useState(existingConfig?.scopes?.join(', ') || 'openid, profile, email')
  const [allowedDomains, setAllowedDomains] = useState(
    existingConfig?.allowedDomains?.join(', ') || ''
  )
  const [autoProvision, setAutoProvision] = useState(existingConfig?.autoProvisionUsers ?? true)
  const [defaultRole, setDefaultRole] = useState<OrganizationRole>(
    existingConfig?.defaultRole || 'Member'
  )

  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleSubmit = async (): Promise<void> => {
    if (!name.trim()) {
      setError('Name is required')
      return
    }
    if (!authority.trim()) {
      setError('Authority URL is required')
      return
    }
    if (!clientId.trim()) {
      setError('Client ID is required')
      return
    }

    setIsSubmitting(true)
    setError(null)

    try {
      const client = await createClient()
      if (!client) throw new Error('No connection')

      const scopeArray = scopes
        .split(',')
        .map((s) => s.trim())
        .filter((s) => s.length > 0)
      const domainArray =
        allowedDomains.trim().length > 0
          ? allowedDomains
              .split(',')
              .map((d) => d.trim())
              .filter((d) => d.length > 0)
          : undefined

      if (isEdit && existingConfig) {
        const request: UpdateSsoConfigApiRequest = {
          name: name.trim(),
          providerType,
          authority: authority.trim(),
          clientId: clientId.trim(),
          clientSecret: clientSecret || undefined,
          scopes: scopeArray,
          allowedDomains: domainArray,
          autoProvisionUsers: autoProvision,
          defaultRole
        }
        await client.updateSsoConfig(existingConfig.ssoConfigId, request)
      } else {
        const request: CreateSsoConfigApiRequest = {
          name: name.trim(),
          providerType,
          authority: authority.trim(),
          clientId: clientId.trim(),
          clientSecret: clientSecret || undefined,
          scopes: scopeArray,
          allowedDomains: domainArray,
          autoProvisionUsers: autoProvision,
          defaultRole
        }
        await client.createSsoConfig(organizationId, request)
      }

      onSaved()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save configuration')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="max-h-[90vh] w-full max-w-lg overflow-y-auto rounded-lg bg-background p-6 shadow-xl">
        <h2 className="mb-4 text-lg font-semibold">
          {isEdit ? 'Edit SSO Configuration' : 'Add SSO Provider'}
        </h2>

        <div className="space-y-4">
          {/* Name */}
          <div>
            <label className="mb-1 block text-sm font-medium">Name</label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g., Corporate SSO"
              className="w-full rounded-md border bg-background px-3 py-2 text-sm"
            />
          </div>

          {/* Provider Type */}
          <div>
            <label className="mb-1 block text-sm font-medium">Provider Type</label>
            <select
              value={providerType}
              onChange={(e) => setProviderType(e.target.value as SsoProviderType)}
              className="w-full rounded-md border bg-background px-3 py-2 text-sm"
            >
              <option value="oidc">OpenID Connect (Generic)</option>
              <option value="entraId">Microsoft Entra ID</option>
              <option value="google">Google Workspace</option>
              <option value="okta">Okta</option>
              <option value="auth0">Auth0</option>
              <option value="keycloak">Keycloak</option>
              <option value="saml">SAML 2.0</option>
            </select>
          </div>

          {/* Authority */}
          <div>
            <label className="mb-1 block text-sm font-medium">Authority URL</label>
            <input
              type="url"
              value={authority}
              onChange={(e) => setAuthority(e.target.value)}
              placeholder="https://login.microsoftonline.com/{tenant}/v2.0"
              className="w-full rounded-md border bg-background px-3 py-2 text-sm"
            />
            <p className="mt-1 text-xs text-muted-foreground">
              The OIDC issuer URL / authority endpoint
            </p>
          </div>

          {/* Client ID */}
          <div>
            <label className="mb-1 block text-sm font-medium">Client ID</label>
            <input
              type="text"
              value={clientId}
              onChange={(e) => setClientId(e.target.value)}
              placeholder="Application (client) ID"
              className="w-full rounded-md border bg-background px-3 py-2 text-sm"
            />
          </div>

          {/* Client Secret */}
          <div>
            <label className="mb-1 block text-sm font-medium">
              Client Secret {isEdit && '(leave empty to keep existing)'}
            </label>
            <input
              type="password"
              value={clientSecret}
              onChange={(e) => setClientSecret(e.target.value)}
              placeholder={isEdit ? '••••••••' : 'Client secret'}
              className="w-full rounded-md border bg-background px-3 py-2 text-sm"
            />
          </div>

          {/* Scopes */}
          <div>
            <label className="mb-1 block text-sm font-medium">Scopes</label>
            <input
              type="text"
              value={scopes}
              onChange={(e) => setScopes(e.target.value)}
              placeholder="openid, profile, email"
              className="w-full rounded-md border bg-background px-3 py-2 text-sm"
            />
            <p className="mt-1 text-xs text-muted-foreground">Comma-separated list of scopes</p>
          </div>

          {/* Allowed Domains */}
          <div>
            <label className="mb-1 block text-sm font-medium">Allowed Email Domains (optional)</label>
            <input
              type="text"
              value={allowedDomains}
              onChange={(e) => setAllowedDomains(e.target.value)}
              placeholder="company.com, subsidiary.com"
              className="w-full rounded-md border bg-background px-3 py-2 text-sm"
            />
            <p className="mt-1 text-xs text-muted-foreground">
              Restrict to specific email domains (leave empty to allow all)
            </p>
          </div>

          {/* Auto-provision */}
          <div className="flex items-center justify-between rounded-md border p-3">
            <div>
              <div className="font-medium text-sm">Auto-provision Users</div>
              <div className="text-xs text-muted-foreground">
                Automatically create users on first SSO login
              </div>
            </div>
            <label className="relative inline-flex cursor-pointer items-center">
              <input
                type="checkbox"
                checked={autoProvision}
                onChange={(e) => setAutoProvision(e.target.checked)}
                className="peer sr-only"
              />
              <div className="peer h-5 w-9 rounded-full bg-muted after:absolute after:left-[2px] after:top-[2px] after:h-4 after:w-4 after:rounded-full after:bg-white after:transition-all peer-checked:bg-primary peer-checked:after:translate-x-full"></div>
            </label>
          </div>

          {/* Default Role */}
          <div>
            <label className="mb-1 block text-sm font-medium">Default Role</label>
            <select
              value={defaultRole}
              onChange={(e) => setDefaultRole(e.target.value as OrganizationRole)}
              className="w-full rounded-md border bg-background px-3 py-2 text-sm"
            >
              <option value="Viewer">Viewer</option>
              <option value="Member">Member</option>
              <option value="Admin">Admin</option>
              <option value="Owner">Owner</option>
            </select>
            <p className="mt-1 text-xs text-muted-foreground">
              Role assigned to auto-provisioned users
            </p>
          </div>

          {/* Error */}
          {error && (
            <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">{error}</div>
          )}
        </div>

        <div className="mt-6 flex justify-end gap-3">
          <Button variant="outline" onClick={onClose} disabled={isSubmitting}>
            Cancel
          </Button>
          <Button onClick={handleSubmit} disabled={isSubmitting}>
            {isSubmitting ? (
              <>
                <RefreshCw className="mr-2 h-4 w-4 animate-spin" />
                Saving...
              </>
            ) : isEdit ? (
              'Update Configuration'
            ) : (
              'Create Configuration'
            )}
          </Button>
        </div>
      </div>
    </div>
  )
}

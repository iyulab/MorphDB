import { useState, type ReactElement } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Key,
  Shield,
  Lock,
  Plus,
  Trash2,
  RotateCw,
  Copy,
  Check,
  AlertTriangle,
  RefreshCw,
  ChevronRight,
  Eye,
  EyeOff,
  Clock,
  X,
  Play
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { useConnectionStore } from '@/stores/connectionStore'
import {
  MorphDBClient,
  type ApiKeyApiResponse,
  type ApiKeyType,
  type CreateApiKeyApiRequest,
  type SecurityPolicyApiResponse,
  type PolicyType,
  type CreateSecurityPolicyApiRequest,
  type UpdateSecurityPolicyApiRequest,
  type EncryptionInfoApiResponse,
  type KeyRotationStatusApiResponse,
  type TableApiResponse
} from '@/lib/api'
import { cn } from '@/lib/utils'

type SecurityTab = 'keys' | 'policies' | 'encryption'

export function SecurityPage(): ReactElement {
  const { activeConnection, getApiKey } = useConnectionStore()
  const [activeTab, setActiveTab] = useState<SecurityTab>('keys')

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

  if (!activeConnection) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="text-center text-muted-foreground">
          <Shield className="mx-auto mb-4 h-12 w-12 opacity-50" />
          <p>Select a connection to manage security settings</p>
        </div>
      </div>
    )
  }

  return (
    <div className="flex h-full flex-col">
      {/* Header */}
      <div className="border-b px-6 py-4">
        <h1 className="text-xl font-semibold">Security</h1>
        <p className="text-sm text-muted-foreground">
          Manage API keys, security policies, and encryption
        </p>
      </div>

      {/* Tab Navigation */}
      <div className="border-b px-6">
        <div className="flex gap-4">
          <button
            onClick={() => setActiveTab('keys')}
            className={cn(
              'flex items-center gap-2 border-b-2 px-1 py-3 text-sm font-medium transition-colors',
              activeTab === 'keys'
                ? 'border-primary text-primary'
                : 'border-transparent text-muted-foreground hover:text-foreground'
            )}
          >
            <Key className="h-4 w-4" />
            API Keys
          </button>
          <button
            onClick={() => setActiveTab('policies')}
            className={cn(
              'flex items-center gap-2 border-b-2 px-1 py-3 text-sm font-medium transition-colors',
              activeTab === 'policies'
                ? 'border-primary text-primary'
                : 'border-transparent text-muted-foreground hover:text-foreground'
            )}
          >
            <Shield className="h-4 w-4" />
            RLS Policies
          </button>
          <button
            onClick={() => setActiveTab('encryption')}
            className={cn(
              'flex items-center gap-2 border-b-2 px-1 py-3 text-sm font-medium transition-colors',
              activeTab === 'encryption'
                ? 'border-primary text-primary'
                : 'border-transparent text-muted-foreground hover:text-foreground'
            )}
          >
            <Lock className="h-4 w-4" />
            Encryption
          </button>
        </div>
      </div>

      {/* Tab Content */}
      <div className="flex-1 overflow-y-auto p-6">
        {activeTab === 'keys' && <ApiKeysTab createClient={createClient} />}
        {activeTab === 'policies' && <PoliciesTab createClient={createClient} />}
        {activeTab === 'encryption' && <EncryptionTab createClient={createClient} />}
      </div>
    </div>
  )
}

// ============================================================================
// API Keys Tab
// ============================================================================

interface ApiKeysTabProps {
  createClient: () => Promise<MorphDBClient | null>
}

function ApiKeysTab({ createClient }: ApiKeysTabProps): ReactElement {
  const queryClient = useQueryClient()
  const [showCreateModal, setShowCreateModal] = useState(false)
  const [newKeyResult, setNewKeyResult] = useState<{ key: ApiKeyApiResponse; rawKey: string } | null>(null)
  const [copiedKey, setCopiedKey] = useState(false)

  const { data: keys, isLoading, error } = useQuery({
    queryKey: ['api-keys'],
    queryFn: async () => {
      const client = await createClient()
      if (!client) throw new Error('No connection')
      return client.getApiKeys()
    }
  })

  const revokeMutation = useMutation({
    mutationFn: async (keyId: string) => {
      const client = await createClient()
      if (!client) throw new Error('No connection')
      await client.revokeApiKey(keyId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['api-keys'] })
    }
  })

  const rotateMutation = useMutation({
    mutationFn: async (keyId: string) => {
      const client = await createClient()
      if (!client) throw new Error('No connection')
      return client.rotateApiKey(keyId, true)
    },
    onSuccess: (result) => {
      setNewKeyResult(result)
      queryClient.invalidateQueries({ queryKey: ['api-keys'] })
    }
  })

  const copyToClipboard = async (text: string): Promise<void> => {
    await navigator.clipboard.writeText(text)
    setCopiedKey(true)
    setTimeout(() => setCopiedKey(false), 2000)
  }

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

  const isExpired = (expiresAt?: string): boolean => {
    if (!expiresAt) return false
    return new Date(expiresAt) < new Date()
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <RefreshCw className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    )
  }

  if (error) {
    return (
      <div className="flex items-center justify-center py-12 text-destructive">
        <AlertTriangle className="mr-2 h-5 w-5" />
        Failed to load API keys
      </div>
    )
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-medium">API Keys</h2>
          <p className="text-sm text-muted-foreground">
            Manage API keys for authentication. Keys are shown only once when created.
          </p>
        </div>
        <Button onClick={() => setShowCreateModal(true)}>
          <Plus className="mr-2 h-4 w-4" />
          Create Key
        </Button>
      </div>

      {/* New Key Result Modal */}
      {newKeyResult && (
        <div className="rounded-lg border border-warning bg-warning/10 p-4">
          <div className="flex items-start gap-3">
            <AlertTriangle className="h-5 w-5 text-warning" />
            <div className="flex-1">
              <h3 className="font-medium">New API Key Created</h3>
              <p className="text-sm text-muted-foreground">
                Save this key now. You won't be able to see it again.
              </p>
              <div className="mt-3 flex items-center gap-2 rounded-md bg-muted p-3 font-mono text-sm">
                <code className="flex-1 break-all">{newKeyResult.rawKey}</code>
                <Button
                  variant="ghost"
                  size="icon"
                  onClick={() => copyToClipboard(newKeyResult.rawKey)}
                >
                  {copiedKey ? (
                    <Check className="h-4 w-4 text-success" />
                  ) : (
                    <Copy className="h-4 w-4" />
                  )}
                </Button>
              </div>
              <Button
                variant="ghost"
                size="sm"
                className="mt-2"
                onClick={() => setNewKeyResult(null)}
              >
                Dismiss
              </Button>
            </div>
          </div>
        </div>
      )}

      {/* Keys Table */}
      <div className="rounded-lg border">
        <table className="w-full">
          <thead className="bg-muted/50">
            <tr>
              <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground uppercase">
                Name
              </th>
              <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground uppercase">
                Type
              </th>
              <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground uppercase">
                Prefix
              </th>
              <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground uppercase">
                Status
              </th>
              <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground uppercase">
                Last Used
              </th>
              <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground uppercase">
                Expires
              </th>
              <th className="px-4 py-3 text-right text-xs font-medium text-muted-foreground uppercase">
                Actions
              </th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {keys && keys.length > 0 ? (
              keys.map((key: ApiKeyApiResponse) => (
                <tr key={key.id} className="hover:bg-muted/30">
                  <td className="px-4 py-3">
                    <div className="font-medium">{key.name}</div>
                    {key.description && (
                      <div className="text-xs text-muted-foreground">{key.description}</div>
                    )}
                  </td>
                  <td className="px-4 py-3">
                    <span
                      className={cn(
                        'inline-flex items-center rounded-full px-2 py-1 text-xs font-medium',
                        key.keyType === 'service'
                          ? 'bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-400'
                          : 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400'
                      )}
                    >
                      {key.keyType}
                    </span>
                  </td>
                  <td className="px-4 py-3 font-mono text-sm text-muted-foreground">
                    {key.keyPrefix}...
                  </td>
                  <td className="px-4 py-3">
                    {!key.isActive ? (
                      <span className="inline-flex items-center text-xs text-destructive">
                        <X className="mr-1 h-3 w-3" />
                        Revoked
                      </span>
                    ) : isExpired(key.expiresAt) ? (
                      <span className="inline-flex items-center text-xs text-warning">
                        <Clock className="mr-1 h-3 w-3" />
                        Expired
                      </span>
                    ) : (
                      <span className="inline-flex items-center text-xs text-success">
                        <Check className="mr-1 h-3 w-3" />
                        Active
                      </span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-sm text-muted-foreground">
                    {formatDate(key.lastUsedAt)}
                  </td>
                  <td className="px-4 py-3 text-sm text-muted-foreground">
                    {key.expiresAt ? formatDate(key.expiresAt) : 'Never'}
                  </td>
                  <td className="px-4 py-3 text-right">
                    <div className="flex items-center justify-end gap-1">
                      <Button
                        variant="ghost"
                        size="icon"
                        title="Rotate Key"
                        disabled={!key.isActive || rotateMutation.isPending}
                        onClick={() => rotateMutation.mutate(key.id)}
                      >
                        <RotateCw className="h-4 w-4" />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon"
                        title="Revoke Key"
                        disabled={!key.isActive || revokeMutation.isPending}
                        onClick={() => revokeMutation.mutate(key.id)}
                      >
                        <Trash2 className="h-4 w-4 text-destructive" />
                      </Button>
                    </div>
                  </td>
                </tr>
              ))
            ) : (
              <tr>
                <td colSpan={7} className="px-4 py-8 text-center text-muted-foreground">
                  No API keys found. Create one to get started.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Create Key Modal */}
      {showCreateModal && (
        <CreateKeyModal
          createClient={createClient}
          onClose={() => setShowCreateModal(false)}
          onCreated={(result) => {
            setNewKeyResult(result)
            setShowCreateModal(false)
            queryClient.invalidateQueries({ queryKey: ['api-keys'] })
          }}
        />
      )}
    </div>
  )
}

interface CreateKeyModalProps {
  createClient: () => Promise<MorphDBClient | null>
  onClose: () => void
  onCreated: (result: { key: ApiKeyApiResponse; rawKey: string }) => void
}

function CreateKeyModal({ createClient, onClose, onCreated }: CreateKeyModalProps): ReactElement {
  const [name, setName] = useState('')
  const [keyType, setKeyType] = useState<ApiKeyType>('anon')
  const [description, setDescription] = useState('')
  const [expiresIn, setExpiresIn] = useState<string>('')
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleSubmit = async (): Promise<void> => {
    if (!name.trim()) {
      setError('Name is required')
      return
    }

    setIsSubmitting(true)
    setError(null)

    try {
      const client = await createClient()
      if (!client) throw new Error('No connection')

      let expiresAt: string | undefined
      if (expiresIn) {
        const days = parseInt(expiresIn, 10)
        const expDate = new Date()
        expDate.setDate(expDate.getDate() + days)
        expiresAt = expDate.toISOString()
      }

      const request: CreateApiKeyApiRequest = {
        name: name.trim(),
        keyType,
        description: description.trim() || undefined,
        expiresAt
      }

      const result = await client.createApiKey(request)
      onCreated(result)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create key')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-md rounded-lg bg-background p-6 shadow-xl">
        <h2 className="mb-4 text-lg font-semibold">Create API Key</h2>

        <div className="space-y-4">
          <div>
            <label className="mb-1 block text-sm font-medium">Name</label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g., Production Backend"
              className="w-full rounded-md border bg-background px-3 py-2 text-sm"
            />
          </div>

          <div>
            <label className="mb-1 block text-sm font-medium">Type</label>
            <div className="flex gap-3">
              <label className="flex items-center gap-2">
                <input
                  type="radio"
                  name="keyType"
                  value="anon"
                  checked={keyType === 'anon'}
                  onChange={() => setKeyType('anon')}
                  className="h-4 w-4"
                />
                <span className="text-sm">Anonymous</span>
              </label>
              <label className="flex items-center gap-2">
                <input
                  type="radio"
                  name="keyType"
                  value="service"
                  checked={keyType === 'service'}
                  onChange={() => setKeyType('service')}
                  className="h-4 w-4"
                />
                <span className="text-sm">Service</span>
              </label>
            </div>
            <p className="mt-1 text-xs text-muted-foreground">
              {keyType === 'service'
                ? 'Full access for backend services'
                : 'Limited access for client applications'}
            </p>
          </div>

          <div>
            <label className="mb-1 block text-sm font-medium">Description (optional)</label>
            <input
              type="text"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="What is this key used for?"
              className="w-full rounded-md border bg-background px-3 py-2 text-sm"
            />
          </div>

          <div>
            <label className="mb-1 block text-sm font-medium">Expires In (optional)</label>
            <select
              value={expiresIn}
              onChange={(e) => setExpiresIn(e.target.value)}
              className="w-full rounded-md border bg-background px-3 py-2 text-sm"
            >
              <option value="">Never expires</option>
              <option value="7">7 days</option>
              <option value="30">30 days</option>
              <option value="90">90 days</option>
              <option value="365">1 year</option>
            </select>
          </div>

          {error && (
            <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">
              {error}
            </div>
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
                Creating...
              </>
            ) : (
              'Create Key'
            )}
          </Button>
        </div>
      </div>
    </div>
  )
}

// ============================================================================
// RLS Policies Tab
// ============================================================================

interface PoliciesTabProps {
  createClient: () => Promise<MorphDBClient | null>
}

function PoliciesTab({ createClient }: PoliciesTabProps): ReactElement {
  const queryClient = useQueryClient()
  const [selectedTable, setSelectedTable] = useState<string>('')
  const [showCreateModal, setShowCreateModal] = useState(false)

  const { data: tables, isLoading: tablesLoading } = useQuery({
    queryKey: ['tables'],
    queryFn: async () => {
      const client = await createClient()
      if (!client) throw new Error('No connection')
      return client.listTables()
    }
  })

  const { data: policies, isLoading: policiesLoading } = useQuery({
    queryKey: ['security-policies', selectedTable],
    queryFn: async () => {
      if (!selectedTable) return []
      const client = await createClient()
      if (!client) throw new Error('No connection')
      return client.getSecurityPolicies(selectedTable)
    },
    enabled: !!selectedTable
  })

  const deleteMutation = useMutation({
    mutationFn: async (policyId: string) => {
      const client = await createClient()
      if (!client) throw new Error('No connection')
      await client.deleteSecurityPolicy(policyId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['security-policies', selectedTable] })
    }
  })

  const toggleMutation = useMutation({
    mutationFn: async ({ policyId, isActive }: { policyId: string; isActive: boolean }) => {
      const client = await createClient()
      if (!client) throw new Error('No connection')
      await client.updateSecurityPolicy(policyId, { isActive })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['security-policies', selectedTable] })
    }
  })

  const policyTypeLabels: Record<PolicyType, string> = {
    select: 'SELECT',
    insert: 'INSERT',
    update: 'UPDATE',
    delete: 'DELETE',
    all: 'ALL'
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-medium">Row-Level Security Policies</h2>
          <p className="text-sm text-muted-foreground">
            Define fine-grained access control policies for your tables
          </p>
        </div>
        <Button onClick={() => setShowCreateModal(true)} disabled={!selectedTable}>
          <Plus className="mr-2 h-4 w-4" />
          Create Policy
        </Button>
      </div>

      {/* Table Selector */}
      <div className="rounded-lg border p-4">
        <label className="mb-2 block text-sm font-medium">Select Table</label>
        {tablesLoading ? (
          <div className="flex items-center gap-2 text-sm text-muted-foreground">
            <RefreshCw className="h-4 w-4 animate-spin" />
            Loading tables...
          </div>
        ) : (
          <select
            value={selectedTable}
            onChange={(e) => setSelectedTable(e.target.value)}
            className="w-full max-w-md rounded-md border bg-background px-3 py-2 text-sm"
          >
            <option value="">Choose a table...</option>
            {tables?.map((table: TableApiResponse) => (
              <option key={table.id} value={table.name}>
                {table.displayName || table.name}
              </option>
            ))}
          </select>
        )}
      </div>

      {/* Policies List */}
      {selectedTable && (
        <div className="space-y-4">
          {policiesLoading ? (
            <div className="flex items-center justify-center py-8">
              <RefreshCw className="h-6 w-6 animate-spin text-muted-foreground" />
            </div>
          ) : policies && policies.length > 0 ? (
            <div className="space-y-3">
              {policies.map((policy: SecurityPolicyApiResponse) => (
                <div
                  key={policy.id}
                  className={cn(
                    'rounded-lg border p-4',
                    !policy.isActive && 'opacity-60'
                  )}
                >
                  <div className="flex items-start justify-between">
                    <div>
                      <div className="flex items-center gap-2">
                        <h3 className="font-medium">{policy.name}</h3>
                        <span
                          className={cn(
                            'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
                            policy.policyType === 'all'
                              ? 'bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-400'
                              : 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400'
                          )}
                        >
                          {policyTypeLabels[policy.policyType]}
                        </span>
                        {!policy.isActive && (
                          <span className="inline-flex items-center rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
                            Disabled
                          </span>
                        )}
                      </div>
                      {policy.description && (
                        <p className="mt-1 text-sm text-muted-foreground">{policy.description}</p>
                      )}
                      <div className="mt-2 rounded-md bg-muted p-2 font-mono text-xs">
                        {policy.expression}
                      </div>
                    </div>
                    <div className="flex items-center gap-1">
                      <Button
                        variant="ghost"
                        size="icon"
                        title={policy.isActive ? 'Disable' : 'Enable'}
                        onClick={() =>
                          toggleMutation.mutate({
                            policyId: policy.id,
                            isActive: !policy.isActive
                          })
                        }
                      >
                        {policy.isActive ? (
                          <EyeOff className="h-4 w-4" />
                        ) : (
                          <Eye className="h-4 w-4" />
                        )}
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon"
                        title="Delete"
                        onClick={() => deleteMutation.mutate(policy.id)}
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
              <Shield className="mx-auto mb-2 h-8 w-8 opacity-50" />
              <p>No policies defined for this table</p>
              <Button
                variant="link"
                size="sm"
                className="mt-2"
                onClick={() => setShowCreateModal(true)}
              >
                Create your first policy
              </Button>
            </div>
          )}
        </div>
      )}

      {/* Create Policy Modal */}
      {showCreateModal && selectedTable && (
        <CreatePolicyModal
          createClient={createClient}
          tableName={selectedTable}
          onClose={() => setShowCreateModal(false)}
          onCreated={() => {
            setShowCreateModal(false)
            queryClient.invalidateQueries({ queryKey: ['security-policies', selectedTable] })
          }}
        />
      )}
    </div>
  )
}

interface CreatePolicyModalProps {
  createClient: () => Promise<MorphDBClient | null>
  tableName: string
  onClose: () => void
  onCreated: () => void
}

function CreatePolicyModal({
  createClient,
  tableName,
  onClose,
  onCreated
}: CreatePolicyModalProps): ReactElement {
  const [name, setName] = useState('')
  const [policyType, setPolicyType] = useState<PolicyType>('select')
  const [expression, setExpression] = useState('')
  const [description, setDescription] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleSubmit = async (): Promise<void> => {
    if (!name.trim()) {
      setError('Name is required')
      return
    }
    if (!expression.trim()) {
      setError('Expression is required')
      return
    }

    setIsSubmitting(true)
    setError(null)

    try {
      const client = await createClient()
      if (!client) throw new Error('No connection')

      const request: CreateSecurityPolicyApiRequest = {
        name: name.trim(),
        tableName,
        policyType,
        expression: expression.trim(),
        description: description.trim() || undefined
      }

      await client.createSecurityPolicy(request)
      onCreated()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create policy')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-lg rounded-lg bg-background p-6 shadow-xl">
        <h2 className="mb-4 text-lg font-semibold">Create Security Policy</h2>
        <p className="mb-4 text-sm text-muted-foreground">
          Table: <strong>{tableName}</strong>
        </p>

        <div className="space-y-4">
          <div>
            <label className="mb-1 block text-sm font-medium">Policy Name</label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g., users_can_read_own_data"
              className="w-full rounded-md border bg-background px-3 py-2 text-sm"
            />
          </div>

          <div>
            <label className="mb-1 block text-sm font-medium">Policy Type</label>
            <select
              value={policyType}
              onChange={(e) => setPolicyType(e.target.value as PolicyType)}
              className="w-full rounded-md border bg-background px-3 py-2 text-sm"
            >
              <option value="select">SELECT (Read)</option>
              <option value="insert">INSERT (Create)</option>
              <option value="update">UPDATE (Modify)</option>
              <option value="delete">DELETE (Remove)</option>
              <option value="all">ALL Operations</option>
            </select>
          </div>

          <div>
            <label className="mb-1 block text-sm font-medium">Expression</label>
            <textarea
              value={expression}
              onChange={(e) => setExpression(e.target.value)}
              placeholder="e.g., user_id = {{user_id}}"
              rows={3}
              className="w-full rounded-md border bg-background px-3 py-2 font-mono text-sm"
            />
            <p className="mt-1 text-xs text-muted-foreground">
              Use placeholders like {'{{user_id}}'}, {'{{role}}'} for dynamic values
            </p>
          </div>

          <div>
            <label className="mb-1 block text-sm font-medium">Description (optional)</label>
            <input
              type="text"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="What does this policy do?"
              className="w-full rounded-md border bg-background px-3 py-2 text-sm"
            />
          </div>

          {error && (
            <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">
              {error}
            </div>
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
                Creating...
              </>
            ) : (
              'Create Policy'
            )}
          </Button>
        </div>
      </div>
    </div>
  )
}

// ============================================================================
// Encryption Tab
// ============================================================================

interface EncryptionTabProps {
  createClient: () => Promise<MorphDBClient | null>
}

function EncryptionTab({ createClient }: EncryptionTabProps): ReactElement {
  const queryClient = useQueryClient()
  const [selectedTable, setSelectedTable] = useState<string>('')

  const { data: encryptionInfo, isLoading: infoLoading, error: infoError } = useQuery({
    queryKey: ['encryption-info'],
    queryFn: async () => {
      const client = await createClient()
      if (!client) throw new Error('No connection')
      return client.getEncryptionInfo()
    },
    retry: false
  })

  const { data: tables } = useQuery({
    queryKey: ['tables'],
    queryFn: async () => {
      const client = await createClient()
      if (!client) throw new Error('No connection')
      return client.listTables()
    }
  })

  const { data: rotationStatus, refetch: refetchStatus } = useQuery({
    queryKey: ['rotation-status', selectedTable],
    queryFn: async () => {
      if (!selectedTable) return null
      const client = await createClient()
      if (!client) throw new Error('No connection')
      return client.getRotationStatus(selectedTable)
    },
    enabled: !!selectedTable && !!encryptionInfo?.enabled,
    refetchInterval: (query) => {
      const data = query.state.data as KeyRotationStatusApiResponse | null | undefined
      if (data && data.state === 'InProgress') return 2000
      return false
    }
  })

  const rotateTableMutation = useMutation({
    mutationFn: async (tableName: string) => {
      const client = await createClient()
      if (!client) throw new Error('No connection')
      return client.rotateTableKey(tableName)
    },
    onSuccess: () => {
      refetchStatus()
    }
  })

  const rotateTenantMutation = useMutation({
    mutationFn: async () => {
      const client = await createClient()
      if (!client) throw new Error('No connection')
      return client.rotateTenantKeys()
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['encryption-info'] })
    }
  })

  if (infoLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <RefreshCw className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    )
  }

  if (infoError || !encryptionInfo?.enabled) {
    return (
      <div className="rounded-lg border border-dashed p-8 text-center">
        <Lock className="mx-auto mb-4 h-12 w-12 text-muted-foreground opacity-50" />
        <h2 className="text-lg font-medium">Encryption Not Enabled</h2>
        <p className="mt-2 text-sm text-muted-foreground">
          Field-level encryption is not configured for this instance.
          <br />
          Contact your administrator to enable encryption.
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      {/* Info Cards */}
      <div className="grid gap-4 md:grid-cols-3">
        <div className="rounded-lg border p-4">
          <div className="flex items-center gap-2 text-sm text-muted-foreground">
            <Lock className="h-4 w-4" />
            Status
          </div>
          <div className="mt-1 flex items-center gap-2 text-2xl font-semibold text-success">
            <Check className="h-5 w-5" />
            Enabled
          </div>
        </div>
        <div className="rounded-lg border p-4">
          <div className="flex items-center gap-2 text-sm text-muted-foreground">
            <Key className="h-4 w-4" />
            Current Key Version
          </div>
          <div className="mt-1 text-2xl font-semibold">v{encryptionInfo.currentKeyVersion}</div>
        </div>
        <div className="rounded-lg border p-4">
          <div className="flex items-center gap-2 text-sm text-muted-foreground">
            <RotateCw className="h-4 w-4" />
            Available Versions
          </div>
          <div className="mt-1 text-2xl font-semibold">
            {encryptionInfo.availableKeyVersions.length}
          </div>
        </div>
      </div>

      {/* Rotate All Keys */}
      <div className="rounded-lg border p-4">
        <div className="flex items-center justify-between">
          <div>
            <h3 className="font-medium">Rotate All Keys</h3>
            <p className="text-sm text-muted-foreground">
              Re-encrypt all tables with the current key version
            </p>
          </div>
          <Button
            variant="outline"
            onClick={() => rotateTenantMutation.mutate()}
            disabled={rotateTenantMutation.isPending}
          >
            {rotateTenantMutation.isPending ? (
              <>
                <RefreshCw className="mr-2 h-4 w-4 animate-spin" />
                Rotating...
              </>
            ) : (
              <>
                <RotateCw className="mr-2 h-4 w-4" />
                Rotate All
              </>
            )}
          </Button>
        </div>
      </div>

      {/* Table-Specific Rotation */}
      <div className="rounded-lg border p-4">
        <h3 className="mb-4 font-medium">Per-Table Key Rotation</h3>
        <div className="flex gap-3">
          <select
            value={selectedTable}
            onChange={(e) => setSelectedTable(e.target.value)}
            className="flex-1 rounded-md border bg-background px-3 py-2 text-sm"
          >
            <option value="">Select a table...</option>
            {tables?.map((table: TableApiResponse) => (
              <option key={table.id} value={table.name}>
                {table.displayName || table.name}
              </option>
            ))}
          </select>
          <Button
            onClick={() => rotateTableMutation.mutate(selectedTable)}
            disabled={!selectedTable || rotateTableMutation.isPending}
          >
            {rotateTableMutation.isPending ? (
              <>
                <RefreshCw className="mr-2 h-4 w-4 animate-spin" />
                Rotating...
              </>
            ) : (
              <>
                <Play className="mr-2 h-4 w-4" />
                Rotate
              </>
            )}
          </Button>
        </div>

        {/* Rotation Status */}
        {selectedTable && rotationStatus && (
          <div className="mt-4 rounded-md bg-muted p-3">
            <div className="flex items-center justify-between text-sm">
              <span>Status: {rotationStatus.state}</span>
              <span>
                {rotationStatus.rowsProcessed} / {rotationStatus.totalRows} rows
              </span>
            </div>
            {rotationStatus.state === 'InProgress' && (
              <div className="mt-2">
                <div className="h-2 overflow-hidden rounded-full bg-muted-foreground/20">
                  <div
                    className="h-full bg-primary transition-all"
                    style={{ width: `${rotationStatus.progressPercent}%` }}
                  />
                </div>
                <div className="mt-1 text-xs text-muted-foreground">
                  {rotationStatus.progressPercent.toFixed(1)}% complete
                </div>
              </div>
            )}
            {rotationStatus.lastRotatedAt && (
              <div className="mt-2 text-xs text-muted-foreground">
                Last rotated: {new Date(rotationStatus.lastRotatedAt).toLocaleString()}
              </div>
            )}
          </div>
        )}
      </div>

      {/* Available Key Versions */}
      <div className="rounded-lg border p-4">
        <h3 className="mb-4 font-medium">Available Key Versions</h3>
        <div className="flex flex-wrap gap-2">
          {encryptionInfo.availableKeyVersions.map((version: number) => (
            <span
              key={version}
              className={cn(
                'inline-flex items-center rounded-full px-3 py-1 text-sm font-medium',
                version === encryptionInfo.currentKeyVersion
                  ? 'bg-primary text-primary-foreground'
                  : 'bg-muted text-muted-foreground'
              )}
            >
              v{version}
              {version === encryptionInfo.currentKeyVersion && (
                <span className="ml-1 text-xs">(current)</span>
              )}
            </span>
          ))}
        </div>
      </div>
    </div>
  )
}

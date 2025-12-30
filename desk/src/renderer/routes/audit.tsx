import { useState, type ReactElement } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  FileText,
  Search,
  Filter,
  RefreshCw,
  ChevronLeft,
  ChevronRight,
  Clock,
  User,
  AlertTriangle,
  AlertCircle,
  Info,
  Bug,
  Shield,
  X,
  BarChart3
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import {
  MorphDBClient,
  type AuditLogEntryApiResponse,
  type AuditLogQueryParams,
  type AuditLogPageApiResponse,
  type AuditStatsApiResponse,
  type ProjectApiResponse,
  type AuditCategory,
  type AuditSeverity
} from '@/lib/api'
import { useConnectionStore } from '@/stores/connectionStore'
import { cn } from '@/lib/utils'

const CATEGORY_OPTIONS: { value: number; label: string; category: AuditCategory }[] = [
  { value: 0, label: 'Auth', category: 'auth' },
  { value: 1, label: 'Data', category: 'data' },
  { value: 2, label: 'Schema', category: 'schema' },
  { value: 3, label: 'Admin', category: 'admin' },
  { value: 4, label: 'Security', category: 'security' },
  { value: 5, label: 'System', category: 'system' }
]

const SEVERITY_OPTIONS: { value: number; label: string; severity: AuditSeverity }[] = [
  { value: 0, label: 'Debug', severity: 'debug' },
  { value: 1, label: 'Info', severity: 'info' },
  { value: 2, label: 'Warning', severity: 'warning' },
  { value: 3, label: 'Error', severity: 'error' },
  { value: 4, label: 'Critical', severity: 'critical' }
]

function getSeverityIcon(severity: AuditSeverity): ReactElement {
  switch (severity) {
    case 'critical':
      return <AlertCircle className="h-4 w-4 text-destructive" />
    case 'error':
      return <AlertTriangle className="h-4 w-4 text-destructive" />
    case 'warning':
      return <AlertTriangle className="h-4 w-4 text-warning" />
    case 'info':
      return <Info className="h-4 w-4 text-primary" />
    case 'debug':
      return <Bug className="h-4 w-4 text-muted-foreground" />
  }
}

function getSeverityBadge(severity: AuditSeverity): string {
  switch (severity) {
    case 'critical':
      return 'bg-destructive/20 text-destructive border-destructive/30'
    case 'error':
      return 'bg-destructive/10 text-destructive border-destructive/20'
    case 'warning':
      return 'bg-warning/20 text-warning border-warning/30'
    case 'info':
      return 'bg-primary/10 text-primary border-primary/20'
    case 'debug':
      return 'bg-muted text-muted-foreground border-muted-foreground/20'
  }
}

function getCategoryBadge(category: AuditCategory): string {
  switch (category) {
    case 'auth':
      return 'bg-blue-500/10 text-blue-500 border-blue-500/20'
    case 'data':
      return 'bg-green-500/10 text-green-500 border-green-500/20'
    case 'schema':
      return 'bg-purple-500/10 text-purple-500 border-purple-500/20'
    case 'admin':
      return 'bg-orange-500/10 text-orange-500 border-orange-500/20'
    case 'security':
      return 'bg-red-500/10 text-red-500 border-red-500/20'
    case 'system':
      return 'bg-gray-500/10 text-gray-500 border-gray-500/20'
  }
}

function formatTimestamp(timestamp: string): string {
  return new Date(timestamp).toLocaleString()
}

function formatDuration(ms?: number): string {
  if (!ms) return '-'
  if (ms < 1000) return `${ms}ms`
  return `${(ms / 1000).toFixed(2)}s`
}

export function AuditPage(): ReactElement {
  const { activeConnection, getApiKey } = useConnectionStore()

  const [selectedProjectId, setSelectedProjectId] = useState<string>('')
  const [showFilters, setShowFilters] = useState(false)
  const [showStats, setShowStats] = useState(false)
  const [selectedLog, setSelectedLog] = useState<AuditLogEntryApiResponse | null>(null)

  // Filter state
  const [filters, setFilters] = useState<AuditLogQueryParams>({
    page: 1,
    pageSize: 50,
    descending: true
  })
  const [searchText, setSearchText] = useState('')

  // Helper to create API client
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

  // Fetch projects
  const { data: projects = [] } = useQuery<ProjectApiResponse[]>({
    queryKey: ['projects', activeConnection?.id],
    queryFn: async () => {
      const client = await createClient()
      if (!client) return []
      return client.listProjects()
    },
    enabled: !!activeConnection
  })

  // Fetch audit logs
  const {
    data: auditPage,
    isLoading: isLoadingLogs,
    refetch: refetchLogs
  } = useQuery<AuditLogPageApiResponse>({
    queryKey: ['audit-logs', activeConnection?.id, selectedProjectId, filters, searchText],
    queryFn: async () => {
      const client = await createClient()
      if (!client) throw new Error('Not connected')
      return client.queryAuditLogs(selectedProjectId, {
        ...filters,
        searchText: searchText || undefined
      })
    },
    enabled: !!activeConnection && !!selectedProjectId
  })

  // Fetch audit stats
  const { data: stats, isLoading: isLoadingStats } = useQuery<AuditStatsApiResponse>({
    queryKey: ['audit-stats', activeConnection?.id, selectedProjectId, filters.from, filters.to],
    queryFn: async () => {
      const client = await createClient()
      if (!client) throw new Error('Not connected')
      return client.getAuditStats(selectedProjectId, filters.from, filters.to)
    },
    enabled: !!activeConnection && !!selectedProjectId && showStats
  })

  const handleSearch = (e: React.FormEvent): void => {
    e.preventDefault()
    setFilters((prev) => ({ ...prev, page: 1 }))
    refetchLogs()
  }

  const handlePageChange = (newPage: number): void => {
    setFilters((prev) => ({ ...prev, page: newPage }))
  }

  const clearFilters = (): void => {
    setFilters({
      page: 1,
      pageSize: 50,
      descending: true
    })
    setSearchText('')
  }

  const hasActiveFilters =
    filters.category !== undefined ||
    filters.minSeverity !== undefined ||
    filters.actorId ||
    filters.resourceType ||
    filters.action ||
    filters.from ||
    filters.to ||
    searchText

  if (!activeConnection) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="text-center">
          <FileText className="mx-auto h-12 w-12 text-muted-foreground" />
          <h2 className="mt-4 text-lg font-semibold">No Connection</h2>
          <p className="mt-2 text-sm text-muted-foreground">
            Connect to a MorphDB server to view audit logs.
          </p>
        </div>
      </div>
    )
  }

  return (
    <div className="flex h-full flex-col">
      {/* Header */}
      <div className="flex items-center justify-between border-b px-6 py-4">
        <div className="flex items-center gap-3">
          <FileText className="h-6 w-6" />
          <div>
            <h1 className="text-xl font-semibold">Audit Logs</h1>
            <p className="text-sm text-muted-foreground">
              View and analyze activity logs for compliance and security
            </p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <Button
            variant={showStats ? 'secondary' : 'ghost'}
            size="sm"
            onClick={() => setShowStats(!showStats)}
          >
            <BarChart3 className="h-4 w-4 mr-2" />
            Stats
          </Button>
          <Button variant="ghost" size="sm" onClick={() => refetchLogs()}>
            <RefreshCw className="h-4 w-4" />
          </Button>
        </div>
      </div>

      {/* Project Selector & Filters */}
      <div className="border-b px-6 py-3 space-y-3">
        <div className="flex items-center gap-4">
          <div className="flex items-center gap-2">
            <label className="text-sm font-medium">Project:</label>
            <select
              value={selectedProjectId}
              onChange={(e) => setSelectedProjectId(e.target.value)}
              className="h-9 rounded-md border border-input bg-background px-3 text-sm min-w-[200px]"
            >
              <option value="">Select a project</option>
              {projects.map((project: ProjectApiResponse) => (
                <option key={project.id} value={project.id}>
                  {project.name}
                </option>
              ))}
            </select>
          </div>

          <form onSubmit={handleSearch} className="flex-1 flex gap-2">
            <div className="relative flex-1 max-w-md">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                value={searchText}
                onChange={(e) => setSearchText(e.target.value)}
                placeholder="Search in metadata..."
                className="pl-9"
              />
            </div>
            <Button type="submit" size="sm">
              Search
            </Button>
          </form>

          <Button
            variant={showFilters ? 'secondary' : 'outline'}
            size="sm"
            onClick={() => setShowFilters(!showFilters)}
          >
            <Filter className="h-4 w-4 mr-2" />
            Filters
            {hasActiveFilters && <span className="ml-2 h-2 w-2 rounded-full bg-primary" />}
          </Button>
        </div>

        {/* Expanded Filters */}
        {showFilters && (
          <div className="grid grid-cols-4 gap-4 pt-3 border-t">
            <div className="space-y-1">
              <label className="text-xs font-medium text-muted-foreground">Category</label>
              <select
                value={filters.category ?? ''}
                onChange={(e) =>
                  setFilters((prev) => ({
                    ...prev,
                    category: e.target.value ? parseInt(e.target.value) : undefined,
                    page: 1
                  }))
                }
                className="w-full h-9 rounded-md border border-input bg-background px-3 text-sm"
              >
                <option value="">All categories</option>
                {CATEGORY_OPTIONS.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
            </div>

            <div className="space-y-1">
              <label className="text-xs font-medium text-muted-foreground">Min Severity</label>
              <select
                value={filters.minSeverity ?? ''}
                onChange={(e) =>
                  setFilters((prev) => ({
                    ...prev,
                    minSeverity: e.target.value ? parseInt(e.target.value) : undefined,
                    page: 1
                  }))
                }
                className="w-full h-9 rounded-md border border-input bg-background px-3 text-sm"
              >
                <option value="">All severities</option>
                {SEVERITY_OPTIONS.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}+
                  </option>
                ))}
              </select>
            </div>

            <div className="space-y-1">
              <label className="text-xs font-medium text-muted-foreground">Actor ID</label>
              <Input
                value={filters.actorId ?? ''}
                onChange={(e) =>
                  setFilters((prev) => ({
                    ...prev,
                    actorId: e.target.value || undefined,
                    page: 1
                  }))
                }
                placeholder="Filter by actor..."
              />
            </div>

            <div className="space-y-1">
              <label className="text-xs font-medium text-muted-foreground">Action</label>
              <Input
                value={filters.action ?? ''}
                onChange={(e) =>
                  setFilters((prev) => ({
                    ...prev,
                    action: e.target.value || undefined,
                    page: 1
                  }))
                }
                placeholder="Filter by action..."
              />
            </div>

            <div className="space-y-1">
              <label className="text-xs font-medium text-muted-foreground">Resource Type</label>
              <Input
                value={filters.resourceType ?? ''}
                onChange={(e) =>
                  setFilters((prev) => ({
                    ...prev,
                    resourceType: e.target.value || undefined,
                    page: 1
                  }))
                }
                placeholder="e.g., table, column..."
              />
            </div>

            <div className="space-y-1">
              <label className="text-xs font-medium text-muted-foreground">From Date</label>
              <Input
                type="datetime-local"
                value={filters.from?.slice(0, 16) ?? ''}
                onChange={(e) =>
                  setFilters((prev) => ({
                    ...prev,
                    from: e.target.value ? new Date(e.target.value).toISOString() : undefined,
                    page: 1
                  }))
                }
              />
            </div>

            <div className="space-y-1">
              <label className="text-xs font-medium text-muted-foreground">To Date</label>
              <Input
                type="datetime-local"
                value={filters.to?.slice(0, 16) ?? ''}
                onChange={(e) =>
                  setFilters((prev) => ({
                    ...prev,
                    to: e.target.value ? new Date(e.target.value).toISOString() : undefined,
                    page: 1
                  }))
                }
              />
            </div>

            <div className="flex items-end">
              <Button variant="ghost" size="sm" onClick={clearFilters}>
                <X className="h-4 w-4 mr-2" />
                Clear Filters
              </Button>
            </div>
          </div>
        )}
      </div>

      {/* Stats Panel */}
      {showStats && selectedProjectId && (
        <div className="border-b px-6 py-4 bg-muted/30">
          {isLoadingStats ? (
            <div className="text-center text-muted-foreground">Loading stats...</div>
          ) : stats ? (
            <div className="grid grid-cols-5 gap-6">
              <div className="space-y-1">
                <div className="text-2xl font-bold">{stats.totalEvents.toLocaleString()}</div>
                <div className="text-xs text-muted-foreground">Total Events</div>
              </div>

              <div className="space-y-1">
                <div className="text-2xl font-bold text-destructive">
                  {(stats.errorRate * 100).toFixed(1)}%
                </div>
                <div className="text-xs text-muted-foreground">Error Rate</div>
              </div>

              <div className="space-y-2">
                <div className="text-xs font-medium text-muted-foreground">By Category</div>
                <div className="flex flex-wrap gap-1">
                  {Object.entries(stats.byCategory).map(([cat, count]) => (
                    <span
                      key={cat}
                      className={cn(
                        'px-2 py-0.5 text-xs rounded-full border',
                        getCategoryBadge(cat as AuditCategory)
                      )}
                    >
                      {cat}: {String(count)}
                    </span>
                  ))}
                </div>
              </div>

              <div className="space-y-2">
                <div className="text-xs font-medium text-muted-foreground">By Severity</div>
                <div className="flex flex-wrap gap-1">
                  {Object.entries(stats.bySeverity).map(([sev, count]) => (
                    <span
                      key={sev}
                      className={cn(
                        'px-2 py-0.5 text-xs rounded-full border',
                        getSeverityBadge(sev as AuditSeverity)
                      )}
                    >
                      {sev}: {String(count)}
                    </span>
                  ))}
                </div>
              </div>

              <div className="space-y-2">
                <div className="text-xs font-medium text-muted-foreground">Top Actions</div>
                <div className="space-y-1">
                  {stats.topActions.slice(0, 3).map((action) => (
                    <div key={action.action} className="flex justify-between text-xs">
                      <span className="truncate">{action.action}</span>
                      <span className="text-muted-foreground">{action.eventCount}</span>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          ) : null}
        </div>
      )}

      {/* Main Content */}
      <div className="flex-1 overflow-hidden flex">
        {/* Log List */}
        <div className="flex-1 overflow-auto">
          {!selectedProjectId ? (
            <div className="flex h-full items-center justify-center">
              <div className="text-center text-muted-foreground">
                <Shield className="mx-auto h-12 w-12 mb-4" />
                <p>Select a project to view audit logs</p>
              </div>
            </div>
          ) : isLoadingLogs ? (
            <div className="flex h-full items-center justify-center">
              <RefreshCw className="h-8 w-8 animate-spin text-muted-foreground" />
            </div>
          ) : !auditPage?.items.length ? (
            <div className="flex h-full items-center justify-center">
              <div className="text-center text-muted-foreground">
                <FileText className="mx-auto h-12 w-12 mb-4" />
                <p>No audit logs found</p>
                {hasActiveFilters && (
                  <Button variant="link" onClick={clearFilters} className="mt-2">
                    Clear filters
                  </Button>
                )}
              </div>
            </div>
          ) : (
            <table className="w-full">
              <thead className="sticky top-0 bg-background border-b">
                <tr className="text-left text-xs text-muted-foreground">
                  <th className="px-4 py-3 font-medium">Timestamp</th>
                  <th className="px-4 py-3 font-medium">Severity</th>
                  <th className="px-4 py-3 font-medium">Category</th>
                  <th className="px-4 py-3 font-medium">Action</th>
                  <th className="px-4 py-3 font-medium">Actor</th>
                  <th className="px-4 py-3 font-medium">Resource</th>
                  <th className="px-4 py-3 font-medium">Duration</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {auditPage.items.map((log: AuditLogEntryApiResponse) => (
                  <tr
                    key={log.id}
                    className={cn(
                      'hover:bg-muted/50 cursor-pointer transition-colors',
                      selectedLog?.id === log.id && 'bg-muted'
                    )}
                    onClick={() => setSelectedLog(log)}
                  >
                    <td className="px-4 py-3 text-sm">
                      <div className="flex items-center gap-2">
                        <Clock className="h-3 w-3 text-muted-foreground" />
                        {formatTimestamp(log.timestamp)}
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          'inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs border',
                          getSeverityBadge(log.severity)
                        )}
                      >
                        {getSeverityIcon(log.severity)}
                        {log.severity}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          'px-2 py-0.5 rounded-full text-xs border',
                          getCategoryBadge(log.category)
                        )}
                      >
                        {log.category}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-sm font-mono">{log.action}</td>
                    <td className="px-4 py-3 text-sm">
                      {log.actorId ? (
                        <div className="flex items-center gap-1">
                          <User className="h-3 w-3 text-muted-foreground" />
                          <span className="truncate max-w-[120px]" title={log.actorId}>
                            {log.actorId}
                          </span>
                        </div>
                      ) : (
                        <span className="text-muted-foreground">-</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-sm">
                      {log.resourceType ? (
                        <span className="font-mono text-xs">
                          {log.resourceType}
                          {log.resourceId && (
                            <span className="text-muted-foreground">:{log.resourceId}</span>
                          )}
                        </span>
                      ) : (
                        <span className="text-muted-foreground">-</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-sm text-muted-foreground">
                      {formatDuration(log.durationMs)}
                    </td>
                    <td className="px-4 py-3">
                      {log.statusCode ? (
                        <span
                          className={cn(
                            'px-2 py-0.5 rounded text-xs font-mono',
                            log.statusCode >= 200 && log.statusCode < 300
                              ? 'bg-success/10 text-success'
                              : log.statusCode >= 400
                                ? 'bg-destructive/10 text-destructive'
                                : 'bg-warning/10 text-warning'
                          )}
                        >
                          {log.statusCode}
                        </span>
                      ) : (
                        <span className="text-muted-foreground">-</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

        {/* Log Detail Panel */}
        {selectedLog && (
          <div className="w-96 border-l bg-muted/30 overflow-auto">
            <div className="sticky top-0 bg-muted/30 border-b px-4 py-3 flex items-center justify-between">
              <h3 className="font-medium">Log Details</h3>
              <Button variant="ghost" size="icon" onClick={() => setSelectedLog(null)}>
                <X className="h-4 w-4" />
              </Button>
            </div>
            <div className="p-4 space-y-4">
              <div className="space-y-1">
                <div className="text-xs font-medium text-muted-foreground">ID</div>
                <div className="text-sm font-mono break-all">{selectedLog.id}</div>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div className="space-y-1">
                  <div className="text-xs font-medium text-muted-foreground">Category</div>
                  <span
                    className={cn(
                      'inline-block px-2 py-0.5 rounded-full text-xs border',
                      getCategoryBadge(selectedLog.category)
                    )}
                  >
                    {selectedLog.category}
                  </span>
                </div>
                <div className="space-y-1">
                  <div className="text-xs font-medium text-muted-foreground">Severity</div>
                  <span
                    className={cn(
                      'inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs border',
                      getSeverityBadge(selectedLog.severity)
                    )}
                  >
                    {getSeverityIcon(selectedLog.severity)}
                    {selectedLog.severity}
                  </span>
                </div>
              </div>

              <div className="space-y-1">
                <div className="text-xs font-medium text-muted-foreground">Action</div>
                <div className="text-sm font-mono">{selectedLog.action}</div>
              </div>

              <div className="space-y-1">
                <div className="text-xs font-medium text-muted-foreground">Timestamp</div>
                <div className="text-sm">{formatTimestamp(selectedLog.timestamp)}</div>
              </div>

              {selectedLog.actorId && (
                <div className="space-y-1">
                  <div className="text-xs font-medium text-muted-foreground">Actor</div>
                  <div className="text-sm">
                    <div className="font-mono">{selectedLog.actorId}</div>
                    {selectedLog.actorType && (
                      <div className="text-muted-foreground">Type: {selectedLog.actorType}</div>
                    )}
                  </div>
                </div>
              )}

              {selectedLog.resourceType && (
                <div className="space-y-1">
                  <div className="text-xs font-medium text-muted-foreground">Resource</div>
                  <div className="text-sm font-mono">
                    {selectedLog.resourceType}
                    {selectedLog.resourceId && `: ${selectedLog.resourceId}`}
                  </div>
                </div>
              )}

              {selectedLog.httpMethod && (
                <div className="grid grid-cols-2 gap-4">
                  <div className="space-y-1">
                    <div className="text-xs font-medium text-muted-foreground">HTTP Method</div>
                    <div className="text-sm font-mono">{selectedLog.httpMethod}</div>
                  </div>
                  {selectedLog.statusCode && (
                    <div className="space-y-1">
                      <div className="text-xs font-medium text-muted-foreground">Status Code</div>
                      <div className="text-sm font-mono">{selectedLog.statusCode}</div>
                    </div>
                  )}
                </div>
              )}

              {selectedLog.requestPath && (
                <div className="space-y-1">
                  <div className="text-xs font-medium text-muted-foreground">Request Path</div>
                  <div className="text-sm font-mono break-all">{selectedLog.requestPath}</div>
                </div>
              )}

              {selectedLog.durationMs !== undefined && (
                <div className="space-y-1">
                  <div className="text-xs font-medium text-muted-foreground">Duration</div>
                  <div className="text-sm">{formatDuration(selectedLog.durationMs)}</div>
                </div>
              )}

              {selectedLog.ipAddress && (
                <div className="space-y-1">
                  <div className="text-xs font-medium text-muted-foreground">IP Address</div>
                  <div className="text-sm font-mono">{selectedLog.ipAddress}</div>
                </div>
              )}

              {selectedLog.userAgent && (
                <div className="space-y-1">
                  <div className="text-xs font-medium text-muted-foreground">User Agent</div>
                  <div className="text-sm text-muted-foreground break-all">
                    {selectedLog.userAgent}
                  </div>
                </div>
              )}

              {selectedLog.errorMessage && (
                <div className="space-y-1">
                  <div className="text-xs font-medium text-destructive">Error Message</div>
                  <div className="text-sm p-2 rounded bg-destructive/10 text-destructive">
                    {selectedLog.errorMessage}
                  </div>
                </div>
              )}

              {selectedLog.metadata && Object.keys(selectedLog.metadata).length > 0 && (
                <div className="space-y-1">
                  <div className="text-xs font-medium text-muted-foreground">Metadata</div>
                  <pre className="text-xs p-2 rounded bg-muted overflow-auto max-h-48">
                    {JSON.stringify(selectedLog.metadata, null, 2)}
                  </pre>
                </div>
              )}
            </div>
          </div>
        )}
      </div>

      {/* Pagination */}
      {auditPage && auditPage.totalPages > 1 && (
        <div className="border-t px-6 py-3 flex items-center justify-between">
          <div className="text-sm text-muted-foreground">
            Showing {(auditPage.page - 1) * auditPage.pageSize + 1} -{' '}
            {Math.min(auditPage.page * auditPage.pageSize, auditPage.totalCount)} of{' '}
            {auditPage.totalCount.toLocaleString()} logs
          </div>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={auditPage.page <= 1}
              onClick={() => handlePageChange(auditPage.page - 1)}
            >
              <ChevronLeft className="h-4 w-4" />
              Previous
            </Button>
            <span className="text-sm">
              Page {auditPage.page} of {auditPage.totalPages}
            </span>
            <Button
              variant="outline"
              size="sm"
              disabled={!auditPage.hasMore}
              onClick={() => handlePageChange(auditPage.page + 1)}
            >
              Next
              <ChevronRight className="h-4 w-4" />
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}

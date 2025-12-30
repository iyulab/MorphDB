import { useState, type ReactElement } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  Gauge,
  RefreshCw,
  Clock,
  HardDrive,
  Activity,
  Upload,
  Download,
  Zap,
  TrendingUp,
  AlertCircle
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import {
  MorphDBClient,
  type QuotaSummaryApiResponse,
  type ProjectApiResponse
} from '@/lib/api'
import { useConnectionStore } from '@/stores/connectionStore'
import { cn } from '@/lib/utils'

function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i]}`
}

function formatNumber(num: number): string {
  if (num >= 1_000_000_000) return `${(num / 1_000_000_000).toFixed(1)}B`
  if (num >= 1_000_000) return `${(num / 1_000_000).toFixed(1)}M`
  if (num >= 1_000) return `${(num / 1_000).toFixed(1)}K`
  return num.toLocaleString()
}

function formatDuration(seconds: number): string {
  if (seconds < 60) return `${Math.round(seconds)}s`
  if (seconds < 3600) return `${Math.round(seconds / 60)}m`
  return `${Math.round(seconds / 3600)}h`
}

interface UsageBarProps {
  used: number
  limit: number
  label: string
  icon: ReactElement
  format?: 'bytes' | 'number'
}

function UsageBar({ used, limit, label, icon, format = 'number' }: UsageBarProps): ReactElement {
  const percentage = limit > 0 ? Math.min((used / limit) * 100, 100) : 0
  const isWarning = percentage > 75
  const isCritical = percentage > 90

  const formatValue = format === 'bytes' ? formatBytes : formatNumber

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          {icon}
          <span className="text-sm font-medium">{label}</span>
        </div>
        <span className="text-sm text-muted-foreground">
          {formatValue(used)} / {formatValue(limit)}
        </span>
      </div>
      <div className="relative h-3 rounded-full bg-muted overflow-hidden">
        <div
          className={cn(
            'absolute inset-y-0 left-0 rounded-full transition-all',
            isCritical
              ? 'bg-destructive'
              : isWarning
                ? 'bg-warning'
                : 'bg-primary'
          )}
          style={{ width: `${percentage}%` }}
        />
      </div>
      <div className="flex justify-between text-xs text-muted-foreground">
        <span>{percentage.toFixed(1)}% used</span>
        <span>{formatValue(limit - used)} remaining</span>
      </div>
    </div>
  )
}

export function QuotaPage(): ReactElement {
  const { activeConnection, getApiKey } = useConnectionStore()
  const [selectedProjectId, setSelectedProjectId] = useState<string>('')

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

  // Fetch quota summary
  const {
    data: summary,
    isLoading,
    refetch
  } = useQuery<QuotaSummaryApiResponse>({
    queryKey: ['quota-summary', activeConnection?.id, selectedProjectId],
    queryFn: async () => {
      const client = await createClient()
      if (!client) throw new Error('Not connected')
      return client.getQuotaSummary(selectedProjectId)
    },
    enabled: !!activeConnection && !!selectedProjectId,
    refetchInterval: 30000 // Poll every 30 seconds
  })

  if (!activeConnection) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="text-center">
          <Gauge className="mx-auto h-12 w-12 text-muted-foreground" />
          <h2 className="mt-4 text-lg font-semibold">No Connection</h2>
          <p className="mt-2 text-sm text-muted-foreground">
            Connect to a MorphDB server to view quotas.
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
          <Gauge className="h-6 w-6" />
          <div>
            <h1 className="text-xl font-semibold">Quota & Rate Limits</h1>
            <p className="text-sm text-muted-foreground">
              Monitor usage, quotas, and rate limiting status
            </p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="ghost" size="sm" onClick={() => refetch()}>
            <RefreshCw className="h-4 w-4" />
          </Button>
        </div>
      </div>

      {/* Project Selector */}
      <div className="border-b px-6 py-3">
        <div className="flex items-center gap-2">
          <label className="text-sm font-medium">Project:</label>
          <select
            value={selectedProjectId}
            onChange={(e) => setSelectedProjectId(e.target.value)}
            className="h-9 rounded-md border border-input bg-background px-3 text-sm min-w-[200px]"
          >
            <option value="">Select a project</option>
            {projects.map((project: ProjectApiResponse) => (
              <option key={project.projectId} value={project.projectId}>
                {project.name}
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto p-6">
        {!selectedProjectId ? (
          <div className="flex h-full items-center justify-center">
            <div className="text-center text-muted-foreground">
              <Gauge className="mx-auto h-12 w-12 mb-4" />
              <p>Select a project to view quota information</p>
            </div>
          </div>
        ) : isLoading ? (
          <div className="flex h-full items-center justify-center">
            <RefreshCw className="h-8 w-8 animate-spin text-muted-foreground" />
          </div>
        ) : summary ? (
          <div className="space-y-8 max-w-5xl">
            {/* Summary Cards */}
            <div className="grid grid-cols-4 gap-4">
              <div className="rounded-lg border bg-card p-4">
                <div className="flex items-center gap-2 text-muted-foreground mb-2">
                  <TrendingUp className="h-4 w-4" />
                  <span className="text-xs font-medium">Period</span>
                </div>
                <div className="text-2xl font-bold">{summary.usage.period}</div>
                <div className="text-xs text-muted-foreground mt-1">Current billing period</div>
              </div>

              <div className="rounded-lg border bg-card p-4">
                <div className="flex items-center gap-2 text-muted-foreground mb-2">
                  <Zap className="h-4 w-4" />
                  <span className="text-xs font-medium">Tier</span>
                </div>
                <div className="text-2xl font-bold capitalize">{summary.limits.tier}</div>
                <div className="text-xs text-muted-foreground mt-1">Current plan</div>
              </div>

              <div className="rounded-lg border bg-card p-4">
                <div className="flex items-center gap-2 text-muted-foreground mb-2">
                  <Activity className="h-4 w-4" />
                  <span className="text-xs font-medium">Rate Limit</span>
                </div>
                <div className="text-2xl font-bold">
                  {summary.rateLimit.available}/{summary.rateLimit.limit}
                </div>
                <div className="text-xs text-muted-foreground mt-1">
                  Resets in {formatDuration(summary.rateLimit.windowSeconds)}
                </div>
              </div>

              <div className="rounded-lg border bg-card p-4">
                <div className="flex items-center gap-2 text-muted-foreground mb-2">
                  <Clock className="h-4 w-4" />
                  <span className="text-xs font-medium">Last Updated</span>
                </div>
                <div className="text-2xl font-bold">
                  {new Date(summary.usage.lastUpdated).toLocaleTimeString()}
                </div>
                <div className="text-xs text-muted-foreground mt-1">
                  {new Date(summary.usage.lastUpdated).toLocaleDateString()}
                </div>
              </div>
            </div>

            {/* Usage Bars */}
            <div className="rounded-lg border bg-card">
              <div className="border-b px-6 py-4">
                <h2 className="text-lg font-semibold">Usage Overview</h2>
                <p className="text-sm text-muted-foreground">
                  Current period resource consumption vs limits
                </p>
              </div>
              <div className="p-6 space-y-6">
                <UsageBar
                  used={summary.usage.apiRequests}
                  limit={summary.limits.maxApiRequests}
                  label="API Requests"
                  icon={<Activity className="h-4 w-4 text-primary" />}
                />

                <UsageBar
                  used={summary.usage.dataReads}
                  limit={summary.limits.maxDataReads}
                  label="Data Reads"
                  icon={<Download className="h-4 w-4 text-green-500" />}
                />

                <UsageBar
                  used={summary.usage.dataWrites}
                  limit={summary.limits.maxDataWrites}
                  label="Data Writes"
                  icon={<Upload className="h-4 w-4 text-orange-500" />}
                />

                <UsageBar
                  used={summary.usage.storageBytes}
                  limit={summary.limits.maxStorageBytes}
                  label="Storage"
                  icon={<HardDrive className="h-4 w-4 text-purple-500" />}
                  format="bytes"
                />

                <UsageBar
                  used={summary.usage.bandwidthBytes}
                  limit={summary.limits.maxBandwidthBytes}
                  label="Bandwidth"
                  icon={<Zap className="h-4 w-4 text-yellow-500" />}
                  format="bytes"
                />
              </div>
            </div>

            {/* Rate Limit Details */}
            <div className="rounded-lg border bg-card">
              <div className="border-b px-6 py-4">
                <h2 className="text-lg font-semibold">Rate Limit Status</h2>
                <p className="text-sm text-muted-foreground">
                  Current rate limiting configuration and status
                </p>
              </div>
              <div className="p-6">
                <div className="grid grid-cols-3 gap-6">
                  <div className="space-y-1">
                    <div className="text-xs font-medium text-muted-foreground">
                      Available Requests
                    </div>
                    <div className="text-3xl font-bold">
                      {summary.rateLimit.available}
                    </div>
                    <div className="text-sm text-muted-foreground">
                      out of {summary.rateLimit.limit} max
                    </div>
                  </div>

                  <div className="space-y-1">
                    <div className="text-xs font-medium text-muted-foreground">
                      Window Duration
                    </div>
                    <div className="text-3xl font-bold">
                      {formatDuration(summary.rateLimit.windowSeconds)}
                    </div>
                    <div className="text-sm text-muted-foreground">rate limit window</div>
                  </div>

                  <div className="space-y-1">
                    <div className="text-xs font-medium text-muted-foreground">Resets At</div>
                    <div className="text-3xl font-bold">
                      {new Date(summary.rateLimit.resetAt).toLocaleTimeString()}
                    </div>
                    <div className="text-sm text-muted-foreground">
                      {new Date(summary.rateLimit.resetAt).toLocaleDateString()}
                    </div>
                  </div>
                </div>

                {summary.rateLimit.available < summary.rateLimit.limit * 0.1 && (
                  <div className="mt-4 rounded-md bg-warning/10 border border-warning/30 p-4">
                    <div className="flex items-center gap-2 text-warning">
                      <AlertCircle className="h-4 w-4" />
                      <span className="font-medium">Low Rate Limit</span>
                    </div>
                    <p className="mt-1 text-sm text-muted-foreground">
                      You have less than 10% of your rate limit remaining. Requests may be
                      throttled until the window resets.
                    </p>
                  </div>
                )}

                {/* Rate Limit Gauge */}
                <div className="mt-6">
                  <div className="relative h-4 rounded-full bg-muted overflow-hidden">
                    <div
                      className={cn(
                        'absolute inset-y-0 left-0 rounded-full transition-all',
                        summary.rateLimit.available < summary.rateLimit.limit * 0.1
                          ? 'bg-destructive'
                          : summary.rateLimit.available < summary.rateLimit.limit * 0.25
                            ? 'bg-warning'
                            : 'bg-success'
                      )}
                      style={{
                        width: `${(summary.rateLimit.available / summary.rateLimit.limit) * 100}%`
                      }}
                    />
                  </div>
                  <div className="flex justify-between mt-2 text-xs text-muted-foreground">
                    <span>0</span>
                    <span>
                      {(
                        (summary.rateLimit.available / summary.rateLimit.limit) *
                        100
                      ).toFixed(0)}
                      % available
                    </span>
                    <span>{summary.rateLimit.limit}</span>
                  </div>
                </div>
              </div>
            </div>

            {/* Limits Table */}
            <div className="rounded-lg border bg-card">
              <div className="border-b px-6 py-4">
                <h2 className="text-lg font-semibold">Plan Limits</h2>
                <p className="text-sm text-muted-foreground">
                  Your current plan&apos;s resource limits
                </p>
              </div>
              <div className="overflow-hidden">
                <table className="w-full">
                  <thead className="bg-muted/50">
                    <tr className="text-left text-xs text-muted-foreground">
                      <th className="px-6 py-3 font-medium">Resource</th>
                      <th className="px-6 py-3 font-medium text-right">Limit</th>
                      <th className="px-6 py-3 font-medium text-right">Used</th>
                      <th className="px-6 py-3 font-medium text-right">Remaining</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y">
                    <tr>
                      <td className="px-6 py-3 text-sm">API Requests</td>
                      <td className="px-6 py-3 text-sm text-right font-mono">
                        {formatNumber(summary.limits.maxApiRequests)}
                      </td>
                      <td className="px-6 py-3 text-sm text-right font-mono">
                        {formatNumber(summary.usage.apiRequests)}
                      </td>
                      <td className="px-6 py-3 text-sm text-right font-mono">
                        {formatNumber(summary.limits.maxApiRequests - summary.usage.apiRequests)}
                      </td>
                    </tr>
                    <tr>
                      <td className="px-6 py-3 text-sm">Data Reads</td>
                      <td className="px-6 py-3 text-sm text-right font-mono">
                        {formatNumber(summary.limits.maxDataReads)}
                      </td>
                      <td className="px-6 py-3 text-sm text-right font-mono">
                        {formatNumber(summary.usage.dataReads)}
                      </td>
                      <td className="px-6 py-3 text-sm text-right font-mono">
                        {formatNumber(summary.limits.maxDataReads - summary.usage.dataReads)}
                      </td>
                    </tr>
                    <tr>
                      <td className="px-6 py-3 text-sm">Data Writes</td>
                      <td className="px-6 py-3 text-sm text-right font-mono">
                        {formatNumber(summary.limits.maxDataWrites)}
                      </td>
                      <td className="px-6 py-3 text-sm text-right font-mono">
                        {formatNumber(summary.usage.dataWrites)}
                      </td>
                      <td className="px-6 py-3 text-sm text-right font-mono">
                        {formatNumber(summary.limits.maxDataWrites - summary.usage.dataWrites)}
                      </td>
                    </tr>
                    <tr>
                      <td className="px-6 py-3 text-sm">Storage</td>
                      <td className="px-6 py-3 text-sm text-right font-mono">
                        {formatBytes(summary.limits.maxStorageBytes)}
                      </td>
                      <td className="px-6 py-3 text-sm text-right font-mono">
                        {formatBytes(summary.usage.storageBytes)}
                      </td>
                      <td className="px-6 py-3 text-sm text-right font-mono">
                        {formatBytes(
                          summary.limits.maxStorageBytes - summary.usage.storageBytes
                        )}
                      </td>
                    </tr>
                    <tr>
                      <td className="px-6 py-3 text-sm">Bandwidth</td>
                      <td className="px-6 py-3 text-sm text-right font-mono">
                        {formatBytes(summary.limits.maxBandwidthBytes)}
                      </td>
                      <td className="px-6 py-3 text-sm text-right font-mono">
                        {formatBytes(summary.usage.bandwidthBytes)}
                      </td>
                      <td className="px-6 py-3 text-sm text-right font-mono">
                        {formatBytes(
                          summary.limits.maxBandwidthBytes - summary.usage.bandwidthBytes
                        )}
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        ) : null}
      </div>
    </div>
  )
}

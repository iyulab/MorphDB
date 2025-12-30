import { useState, type ReactElement } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Plus,
  Trash2,
  RotateCcw,
  Download,
  HardDrive,
  Clock,
  CheckCircle,
  XCircle,
  Loader2,
  AlertCircle,
  Archive
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { useConnectionStore } from '@/stores/connectionStore'
import {
  MorphDBClient,
  type BackupApiResponse,
  type ProjectApiResponse,
  type CreateBackupApiRequest,
  type RestoreBackupApiRequest
} from '@/lib/api'
import { cn } from '@/lib/utils'
import { BackupDialog } from '@/components/dialogs/BackupDialog'
import { RestoreDialog } from '@/components/dialogs/RestoreDialog'

export function BackupsPage(): ReactElement {
  const { activeConnection, getApiKey } = useConnectionStore()
  const queryClient = useQueryClient()

  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null)
  const [selectedBackup, setSelectedBackup] = useState<BackupApiResponse | null>(null)
  const [showBackupDialog, setShowBackupDialog] = useState(false)
  const [showRestoreDialog, setShowRestoreDialog] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null)

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
  const { data: projects = [], isLoading: projectsLoading } = useQuery<ProjectApiResponse[]>({
    queryKey: ['projects', activeConnection?.id],
    queryFn: async () => {
      const client = await createClient()
      if (!client) return []
      return client.listProjects()
    },
    enabled: !!activeConnection
  })

  // Fetch backups for selected project
  const { data: backups = [], isLoading: backupsLoading } = useQuery<BackupApiResponse[]>({
    queryKey: ['backups', activeConnection?.id, selectedProjectId],
    queryFn: async () => {
      if (!selectedProjectId) return []
      const client = await createClient()
      if (!client) return []
      return client.listBackups(selectedProjectId)
    },
    enabled: !!activeConnection && !!selectedProjectId,
    refetchInterval: 5000 // Poll for status updates
  })

  // Create backup mutation
  const createBackupMutation = useMutation({
    mutationFn: async (data: CreateBackupApiRequest) => {
      if (!selectedProjectId) throw new Error('No project selected')
      const client = await createClient()
      if (!client) throw new Error('Not connected')
      return client.createBackup(selectedProjectId, data)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['backups'] })
    }
  })

  // Restore backup mutation
  const restoreBackupMutation = useMutation({
    mutationFn: async ({
      backupId,
      data
    }: {
      backupId: string
      data: RestoreBackupApiRequest
    }) => {
      if (!selectedProjectId) throw new Error('No project selected')
      const client = await createClient()
      if (!client) throw new Error('Not connected')
      return client.restoreBackup(selectedProjectId, backupId, data)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['backups'] })
    }
  })

  // Delete backup mutation
  const deleteBackupMutation = useMutation({
    mutationFn: async (backupId: string) => {
      if (!selectedProjectId) throw new Error('No project selected')
      const client = await createClient()
      if (!client) throw new Error('Not connected')
      return client.deleteBackup(selectedProjectId, backupId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['backups'] })
      setConfirmDelete(null)
    }
  })

  const handleDownload = async (backup: BackupApiResponse): Promise<void> => {
    if (!selectedProjectId) return
    const client = await createClient()
    if (!client) return
    const url = client.getBackupDownloadUrl(selectedProjectId, backup.backupId)
    window.open(url, '_blank')
  }

  const handleDelete = async (backupId: string): Promise<void> => {
    if (confirmDelete !== backupId) {
      setConfirmDelete(backupId)
      return
    }
    await deleteBackupMutation.mutateAsync(backupId)
  }

  const formatBytes = (bytes: number): string => {
    if (bytes === 0) return '0 B'
    const k = 1024
    const sizes = ['B', 'KB', 'MB', 'GB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`
  }

  const formatDate = (date: string): string => {
    return new Date(date).toLocaleString()
  }

  const formatDuration = (start: string, end?: string): string => {
    if (!end) return 'In progress...'
    const duration = new Date(end).getTime() - new Date(start).getTime()
    if (duration < 1000) return `${duration}ms`
    if (duration < 60000) return `${(duration / 1000).toFixed(1)}s`
    return `${(duration / 60000).toFixed(1)}min`
  }

  const getStatusIcon = (status: BackupApiResponse['status']): ReactElement => {
    switch (status) {
      case 'Pending':
        return <Clock className="h-4 w-4 text-muted-foreground" />
      case 'InProgress':
        return <Loader2 className="h-4 w-4 text-primary animate-spin" />
      case 'Completed':
        return <CheckCircle className="h-4 w-4 text-success" />
      case 'Failed':
        return <XCircle className="h-4 w-4 text-destructive" />
      case 'Cancelled':
        return <AlertCircle className="h-4 w-4 text-warning" />
      case 'Expired':
        return <Archive className="h-4 w-4 text-muted-foreground" />
      default:
        return <HardDrive className="h-4 w-4" />
    }
  }

  const getStatusColor = (status: BackupApiResponse['status']): string => {
    switch (status) {
      case 'Pending':
        return 'bg-muted text-muted-foreground'
      case 'InProgress':
        return 'bg-primary/10 text-primary'
      case 'Completed':
        return 'bg-success/10 text-success'
      case 'Failed':
        return 'bg-destructive/10 text-destructive'
      case 'Cancelled':
        return 'bg-warning/10 text-warning'
      case 'Expired':
        return 'bg-muted text-muted-foreground'
      default:
        return 'bg-muted'
    }
  }

  if (!activeConnection) {
    return (
      <div className="flex h-full items-center justify-center">
        <p className="text-muted-foreground">Connect to a server to manage backups</p>
      </div>
    )
  }

  return (
    <div className="flex h-full flex-col">
      {/* Header */}
      <div className="flex items-center justify-between border-b px-4 py-3">
        <div className="flex items-center gap-4">
          <h1 className="text-lg font-semibold">Backups</h1>

          {/* Project Selector */}
          <select
            value={selectedProjectId ?? ''}
            onChange={(e) => setSelectedProjectId(e.target.value || null)}
            disabled={projectsLoading}
            className="h-9 rounded-md border border-input bg-background px-3 text-sm min-w-[200px]"
          >
            <option value="">Select a project...</option>
            {projects.map((project) => (
              <option key={project.id} value={project.id}>
                {project.name}
              </option>
            ))}
          </select>
        </div>

        <Button
          onClick={() => setShowBackupDialog(true)}
          disabled={!selectedProjectId}
        >
          <Plus className="h-4 w-4 mr-2" />
          Create Backup
        </Button>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto p-4">
        {!selectedProjectId ? (
          <div className="flex flex-col items-center justify-center h-full text-muted-foreground">
            <HardDrive className="h-12 w-12 mb-4" />
            <p>Select a project to view its backups</p>
          </div>
        ) : backupsLoading ? (
          <div className="flex items-center justify-center h-full">
            <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
          </div>
        ) : backups.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-full text-muted-foreground">
            <HardDrive className="h-12 w-12 mb-4" />
            <p className="mb-4">No backups yet</p>
            <Button onClick={() => setShowBackupDialog(true)}>
              <Plus className="h-4 w-4 mr-2" />
              Create First Backup
            </Button>
          </div>
        ) : (
          <div className="space-y-3">
            {backups.map((backup) => (
              <div
                key={backup.backupId}
                className="border rounded-lg p-4 hover:bg-muted/50 transition-colors"
              >
                <div className="flex items-start justify-between">
                  <div className="flex items-start gap-3">
                    {getStatusIcon(backup.status)}
                    <div>
                      <div className="font-medium">{backup.name}</div>
                      {backup.description && (
                        <div className="text-sm text-muted-foreground mt-1">
                          {backup.description}
                        </div>
                      )}
                      <div className="flex items-center gap-3 mt-2 text-sm text-muted-foreground">
                        <span
                          className={cn('px-2 py-0.5 rounded-full text-xs', getStatusColor(backup.status))}
                        >
                          {backup.status}
                        </span>
                        <span>{backup.type}</span>
                        {backup.sizeBytes > 0 && <span>{formatBytes(backup.sizeBytes)}</span>}
                        <span>{formatDate(backup.startedAt)}</span>
                        {backup.completedAt && (
                          <span>({formatDuration(backup.startedAt, backup.completedAt)})</span>
                        )}
                      </div>
                      {backup.metadata && (
                        <div className="text-xs text-muted-foreground mt-1">
                          {backup.metadata.tableCount} tables • ~
                          {backup.metadata.estimatedRowCount.toLocaleString()} rows
                        </div>
                      )}
                      {backup.errorMessage && (
                        <div className="text-sm text-destructive mt-2">
                          Error: {backup.errorMessage}
                        </div>
                      )}
                      {backup.expiresAt && (
                        <div className="text-xs text-muted-foreground mt-1">
                          Expires: {formatDate(backup.expiresAt)}
                        </div>
                      )}
                    </div>
                  </div>

                  <div className="flex items-center gap-1">
                    {backup.status === 'Completed' && (
                      <>
                        <Button
                          variant="ghost"
                          size="icon"
                          onClick={() => {
                            setSelectedBackup(backup)
                            setShowRestoreDialog(true)
                          }}
                          title="Restore"
                        >
                          <RotateCcw className="h-4 w-4" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon"
                          onClick={() => handleDownload(backup)}
                          title="Download"
                        >
                          <Download className="h-4 w-4" />
                        </Button>
                      </>
                    )}
                    <Button
                      variant="ghost"
                      size="icon"
                      onClick={() => handleDelete(backup.backupId)}
                      disabled={deleteBackupMutation.isPending}
                      className={cn(
                        confirmDelete === backup.backupId && 'text-destructive hover:text-destructive'
                      )}
                      title={confirmDelete === backup.backupId ? 'Click again to confirm' : 'Delete'}
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Dialogs */}
      <BackupDialog
        open={showBackupDialog}
        onClose={() => setShowBackupDialog(false)}
        onSubmit={(data) => createBackupMutation.mutateAsync(data)}
      />

      <RestoreDialog
        open={showRestoreDialog}
        onClose={() => {
          setShowRestoreDialog(false)
          setSelectedBackup(null)
        }}
        onSubmit={(data) =>
          restoreBackupMutation.mutateAsync({
            backupId: selectedBackup!.backupId,
            data
          })
        }
        backup={selectedBackup}
        projects={projects}
        currentProjectId={selectedProjectId ?? ''}
      />
    </div>
  )
}

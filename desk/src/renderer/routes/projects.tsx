import { useState, type ReactElement } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  FolderKanban,
  Plus,
  Loader2,
  AlertCircle,
  RefreshCw,
  Activity,
  CheckCircle,
  XCircle,
  AlertTriangle
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { ProjectCard } from '@/components/projects/ProjectCard'
import { ProjectDialog } from '@/components/dialogs/ProjectDialog'
import { DeleteConfirmationDialog } from '@/components/dialogs/DeleteConfirmationDialog'
import {
  MorphDBClient,
  type ProjectApiResponse,
  type CreateProjectRequest,
  type UpdateProjectRequest,
  type SchemaHealthResponse
} from '@/lib/api'
import { useConnectionStore } from '@/stores/connectionStore'
import { cn } from '@/lib/utils'

interface DialogState {
  createProject: boolean
  editProject: { open: boolean; project: ProjectApiResponse | null }
  deleteProject: { open: boolean; project: ProjectApiResponse | null }
  healthReport: { open: boolean; report: SchemaHealthResponse | null; projectName: string }
}

const initialDialogState: DialogState = {
  createProject: false,
  editProject: { open: false, project: null },
  deleteProject: { open: false, project: null },
  healthReport: { open: false, report: null, projectName: '' }
}

export function ProjectsPage(): ReactElement {
  const { activeConnection, getApiKey } = useConnectionStore()
  const queryClient = useQueryClient()

  const [dialogs, setDialogs] = useState<DialogState>(initialDialogState)
  const [statusFilter, setStatusFilter] = useState<'all' | 'Active' | 'Suspended' | 'Archived'>('all')

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
  const {
    data: projects,
    isLoading,
    error,
    refetch
  } = useQuery({
    queryKey: ['projects', activeConnection?.id],
    queryFn: async () => {
      const client = await createClient()
      if (!client) return []
      return client.listProjects()
    },
    enabled: !!activeConnection && activeConnection.status === 'connected'
  })

  // Create project mutation
  const createProjectMutation = useMutation({
    mutationFn: async (data: CreateProjectRequest) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.createProject(data)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['projects'] })
    }
  })

  // Update project mutation
  const updateProjectMutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateProjectRequest }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.updateProject(id, data)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['projects'] })
    }
  })

  // Delete project mutation
  const deleteProjectMutation = useMutation({
    mutationFn: async (id: string) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.deleteProject(id)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['projects'] })
    }
  })

  // Suspend project mutation
  const suspendProjectMutation = useMutation({
    mutationFn: async (id: string) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.suspendProject(id)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['projects'] })
    }
  })

  // Reactivate project mutation
  const reactivateProjectMutation = useMutation({
    mutationFn: async (id: string) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.reactivateProject(id)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['projects'] })
    }
  })

  // Archive project mutation
  const archiveProjectMutation = useMutation({
    mutationFn: async (id: string) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.archiveProject(id)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['projects'] })
    }
  })

  // Get project health mutation
  const getHealthMutation = useMutation({
    mutationFn: async (id: string) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.validateProjectHealth(id)
    }
  })

  // Handlers
  const handleCreateProject = async (data: CreateProjectRequest | UpdateProjectRequest): Promise<void> => {
    await createProjectMutation.mutateAsync(data as CreateProjectRequest)
  }

  const handleUpdateProject = async (data: CreateProjectRequest | UpdateProjectRequest): Promise<void> => {
    if (!dialogs.editProject.project) return
    await updateProjectMutation.mutateAsync({
      id: dialogs.editProject.project.projectId,
      data: data as UpdateProjectRequest
    })
  }

  const handleDeleteProject = async (): Promise<void> => {
    if (!dialogs.deleteProject.project) return
    await deleteProjectMutation.mutateAsync(dialogs.deleteProject.project.projectId)
  }

  const handleViewHealth = async (project: ProjectApiResponse): Promise<void> => {
    try {
      const report = await getHealthMutation.mutateAsync(project.projectId)
      setDialogs(prev => ({
        ...prev,
        healthReport: { open: true, report, projectName: project.name }
      }))
    } catch (err) {
      console.error('Failed to fetch health report:', err)
    }
  }

  // Filter projects
  const filteredProjects = projects?.filter(p =>
    statusFilter === 'all' || p.status === statusFilter
  ) || []

  // Stats
  const stats = {
    total: projects?.length || 0,
    active: projects?.filter(p => p.status === 'Active').length || 0,
    suspended: projects?.filter(p => p.status === 'Suspended').length || 0,
    archived: projects?.filter(p => p.status === 'Archived').length || 0
  }

  if (!activeConnection) {
    return (
      <div className="flex flex-col h-full bg-background items-center justify-center">
        <FolderKanban className="h-16 w-16 text-muted-foreground/50 mb-4" />
        <h2 className="text-xl font-semibold">Project Management</h2>
        <p className="mt-2 text-muted-foreground">
          Select a connection to view projects
        </p>
      </div>
    )
  }

  if (activeConnection.status !== 'connected') {
    return (
      <div className="flex flex-col h-full bg-background items-center justify-center">
        <AlertCircle className="h-16 w-16 text-muted-foreground/50 mb-4" />
        <h2 className="text-xl font-semibold">Not Connected</h2>
        <p className="mt-2 text-muted-foreground">
          Connect to the server to manage projects
        </p>
      </div>
    )
  }

  return (
    <div className="flex flex-col h-full bg-background">
      {/* Header */}
      <div className="flex h-10 items-center justify-between border-b border-border px-4">
        <div className="flex items-center">
          <FolderKanban className="h-4 w-4 mr-2" />
          <span className="font-medium text-sm">Projects</span>
          <span className="ml-2 text-xs text-muted-foreground">
            ({stats.total} total)
          </span>
        </div>
        <div className="flex items-center gap-2">
          <Button
            variant="ghost"
            size="icon"
            className="h-7 w-7"
            onClick={() => refetch()}
            disabled={isLoading}
          >
            <RefreshCw className={cn('h-4 w-4', isLoading && 'animate-spin')} />
          </Button>
          <Button
            variant="default"
            size="sm"
            className="gap-1"
            onClick={() => setDialogs(prev => ({ ...prev, createProject: true }))}
          >
            <Plus className="h-4 w-4" />
            New Project
          </Button>
        </div>
      </div>

      {/* Stats Bar */}
      <div className="flex items-center gap-4 px-4 py-2 border-b border-border bg-muted/30">
        <button
          onClick={() => setStatusFilter('all')}
          className={cn(
            'text-xs px-2 py-1 rounded',
            statusFilter === 'all' ? 'bg-primary text-primary-foreground' : 'hover:bg-accent'
          )}
        >
          All ({stats.total})
        </button>
        <button
          onClick={() => setStatusFilter('Active')}
          className={cn(
            'flex items-center gap-1 text-xs px-2 py-1 rounded',
            statusFilter === 'Active' ? 'bg-success/20 text-success' : 'hover:bg-accent'
          )}
        >
          <CheckCircle className="h-3 w-3" />
          Active ({stats.active})
        </button>
        <button
          onClick={() => setStatusFilter('Suspended')}
          className={cn(
            'flex items-center gap-1 text-xs px-2 py-1 rounded',
            statusFilter === 'Suspended' ? 'bg-warning/20 text-warning' : 'hover:bg-accent'
          )}
        >
          <AlertTriangle className="h-3 w-3" />
          Suspended ({stats.suspended})
        </button>
        <button
          onClick={() => setStatusFilter('Archived')}
          className={cn(
            'flex items-center gap-1 text-xs px-2 py-1 rounded',
            statusFilter === 'Archived' ? 'bg-muted text-muted-foreground' : 'hover:bg-accent'
          )}
        >
          <XCircle className="h-3 w-3" />
          Archived ({stats.archived})
        </button>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto p-4">
        {isLoading ? (
          <div className="flex items-center justify-center h-full">
            <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
          </div>
        ) : error ? (
          <div className="flex flex-col items-center justify-center h-full">
            <AlertCircle className="h-12 w-12 text-destructive mb-4" />
            <p className="text-destructive">{(error as Error).message}</p>
            <Button variant="outline" className="mt-4" onClick={() => refetch()}>
              Retry
            </Button>
          </div>
        ) : filteredProjects.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-full text-center">
            <FolderKanban className="h-16 w-16 text-muted-foreground/50 mb-4" />
            <h2 className="text-xl font-semibold">
              {statusFilter === 'all' ? 'No Projects Yet' : `No ${statusFilter} Projects`}
            </h2>
            <p className="mt-2 text-muted-foreground max-w-md">
              {statusFilter === 'all'
                ? 'Create your first project to get started with MorphDB.'
                : `No projects with status "${statusFilter}" found.`}
            </p>
            {statusFilter === 'all' && (
              <Button
                className="mt-6"
                onClick={() => setDialogs(prev => ({ ...prev, createProject: true }))}
              >
                <Plus className="h-4 w-4 mr-2" />
                Create Project
              </Button>
            )}
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {filteredProjects.map(project => (
              <ProjectCard
                key={project.projectId}
                project={project}
                onEdit={() => setDialogs(prev => ({
                  ...prev,
                  editProject: { open: true, project }
                }))}
                onSuspend={() => suspendProjectMutation.mutate(project.projectId)}
                onReactivate={() => reactivateProjectMutation.mutate(project.projectId)}
                onArchive={() => archiveProjectMutation.mutate(project.projectId)}
                onDelete={() => setDialogs(prev => ({
                  ...prev,
                  deleteProject: { open: true, project }
                }))}
                onViewHealth={() => handleViewHealth(project)}
              />
            ))}
          </div>
        )}
      </div>

      {/* Dialogs */}
      <ProjectDialog
        open={dialogs.createProject}
        onOpenChange={(open) => setDialogs(prev => ({ ...prev, createProject: open }))}
        onSubmit={handleCreateProject}
      />

      <ProjectDialog
        open={dialogs.editProject.open}
        onOpenChange={(open) => setDialogs(prev => ({
          ...prev,
          editProject: { ...prev.editProject, open }
        }))}
        project={dialogs.editProject.project}
        onSubmit={handleUpdateProject}
      />

      <DeleteConfirmationDialog
        open={dialogs.deleteProject.open}
        onOpenChange={(open) => setDialogs(prev => ({
          ...prev,
          deleteProject: { ...prev.deleteProject, open }
        }))}
        title="Delete Project"
        description="This will permanently delete the project and all its data. This action cannot be undone."
        itemName={dialogs.deleteProject.project?.name || ''}
        requireTypedConfirmation={true}
        onConfirm={handleDeleteProject}
      />

      {/* Health Report Dialog */}
      {dialogs.healthReport.open && dialogs.healthReport.report && (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div
            className="fixed inset-0 bg-black/50"
            onClick={() => setDialogs(prev => ({
              ...prev,
              healthReport: { open: false, report: null, projectName: '' }
            }))}
          />
          <div className="relative z-50 w-full max-w-lg rounded-lg border bg-background p-6 shadow-lg">
            <div className="flex items-center justify-between mb-4">
              <div className="flex items-center gap-2">
                <Activity className="h-5 w-5" />
                <h2 className="text-lg font-semibold">
                  Health Report: {dialogs.healthReport.projectName}
                </h2>
              </div>
              {dialogs.healthReport.report.isHealthy ? (
                <span className="flex items-center gap-1 text-success text-sm">
                  <CheckCircle className="h-4 w-4" />
                  Healthy
                </span>
              ) : (
                <span className="flex items-center gap-1 text-destructive text-sm">
                  <XCircle className="h-4 w-4" />
                  Issues Found
                </span>
              )}
            </div>

            <div className="space-y-3">
              {dialogs.healthReport.report.issues.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                  No issues found. The project schema is healthy.
                </p>
              ) : (
                dialogs.healthReport.report.issues.map((issue, idx) => (
                  <div
                    key={idx}
                    className={cn(
                      'p-3 rounded-md border',
                      issue.severity === 'Critical' && 'border-destructive bg-destructive/10',
                      issue.severity === 'Error' && 'border-orange-500 bg-orange-500/10',
                      issue.severity === 'Warning' && 'border-warning bg-warning/10'
                    )}
                  >
                    <div className="flex items-center gap-2 text-sm font-medium">
                      <span className={cn(
                        'px-1.5 py-0.5 rounded text-xs',
                        issue.severity === 'Critical' && 'bg-destructive text-destructive-foreground',
                        issue.severity === 'Error' && 'bg-orange-500 text-white',
                        issue.severity === 'Warning' && 'bg-warning text-warning-foreground'
                      )}>
                        {issue.severity}
                      </span>
                      <span>{issue.category}</span>
                    </div>
                    <p className="mt-1 text-sm">{issue.message}</p>
                    {(issue.tableName || issue.columnName) && (
                      <p className="mt-1 text-xs text-muted-foreground">
                        {issue.tableName && `Table: ${issue.tableName}`}
                        {issue.columnName && ` / Column: ${issue.columnName}`}
                      </p>
                    )}
                  </div>
                ))
              )}
            </div>

            <div className="mt-4 pt-4 border-t border-border text-xs text-muted-foreground">
              Checked at: {new Date(dialogs.healthReport.report.checkedAt).toLocaleString()}
            </div>

            <div className="flex justify-end mt-4">
              <Button
                variant="outline"
                onClick={() => setDialogs(prev => ({
                  ...prev,
                  healthReport: { open: false, report: null, projectName: '' }
                }))}
              >
                Close
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

import { useState, type ReactElement } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Building2,
  Plus,
  MoreVertical,
  Pencil,
  Trash2,
  Users,
  Mail,
  UserPlus,
  Shield,
  Loader2,
  X,
  Crown,
  UserCheck,
  Clock
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { useConnectionStore } from '@/stores/connectionStore'
import { cn } from '@/lib/utils'
import { OrganizationDialog } from '@/components/dialogs/OrganizationDialog'
import {
  MorphDBClient,
  type OrganizationApiResponse,
  type CreateOrganizationApiRequest,
  type UpdateOrganizationApiRequest,
  type OrganizationMemberApiResponse,
  type InvitationApiResponse,
  type OrganizationRole
} from '@/lib/api'

type TabType = 'members' | 'invitations'

const ROLE_OPTIONS: { value: OrganizationRole; label: string }[] = [
  { value: 'Owner', label: 'Owner' },
  { value: 'Admin', label: 'Admin' },
  { value: 'Member', label: 'Member' },
  { value: 'Viewer', label: 'Viewer' }
]

export function OrganizationsPage(): ReactElement {
  const { activeConnection, getApiKey } = useConnectionStore()
  const queryClient = useQueryClient()

  const [selectedOrg, setSelectedOrg] = useState<OrganizationApiResponse | null>(null)
  const [activeTab, setActiveTab] = useState<TabType>('members')
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editingOrg, setEditingOrg] = useState<OrganizationApiResponse | null>(null)
  const [inviteDialogOpen, setInviteDialogOpen] = useState(false)
  const [inviteEmail, setInviteEmail] = useState('')
  const [inviteRole, setInviteRole] = useState<OrganizationRole>('Member')
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null)
  const [contextMenu, setContextMenu] = useState<{ id: string; x: number; y: number } | null>(null)

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

  // Fetch organizations
  const { data: organizations = [], isLoading: orgsLoading } = useQuery<OrganizationApiResponse[]>({
    queryKey: ['organizations', activeConnection?.id],
    queryFn: async () => {
      const client = await createClient()
      if (!client) return []
      return client.listOrganizations()
    },
    enabled: !!activeConnection
  })

  // Fetch members for selected org
  const { data: members = [], isLoading: membersLoading } = useQuery<OrganizationMemberApiResponse[]>({
    queryKey: ['org-members', selectedOrg?.organizationId],
    queryFn: async () => {
      const client = await createClient()
      if (!client || !selectedOrg) return []
      return client.listOrganizationMembers(selectedOrg.organizationId)
    },
    enabled: !!activeConnection && !!selectedOrg
  })

  // Fetch invitations for selected org
  const { data: invitations = [], isLoading: invitationsLoading } = useQuery<InvitationApiResponse[]>({
    queryKey: ['org-invitations', selectedOrg?.organizationId],
    queryFn: async () => {
      const client = await createClient()
      if (!client || !selectedOrg) return []
      return client.listInvitations(selectedOrg.organizationId)
    },
    enabled: !!activeConnection && !!selectedOrg && activeTab === 'invitations'
  })

  // Create organization mutation
  const createOrgMutation = useMutation({
    mutationFn: async (data: CreateOrganizationApiRequest) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.createOrganization(data)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['organizations'] })
    }
  })

  // Update organization mutation
  const updateOrgMutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateOrganizationApiRequest }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.updateOrganization(id, data)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['organizations'] })
    }
  })

  // Delete organization mutation
  const deleteOrgMutation = useMutation({
    mutationFn: async (id: string) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.deleteOrganization(id)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['organizations'] })
      setSelectedOrg(null)
      setConfirmDelete(null)
    }
  })

  // Update member role mutation
  const updateMemberMutation = useMutation({
    mutationFn: async ({
      orgId,
      memberId,
      role
    }: {
      orgId: string
      memberId: string
      role: OrganizationRole
    }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.updateOrganizationMember(orgId, memberId, { role })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['org-members'] })
    }
  })

  // Remove member mutation
  const removeMemberMutation = useMutation({
    mutationFn: async ({ orgId, memberId }: { orgId: string; memberId: string }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.removeOrganizationMember(orgId, memberId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['org-members'] })
    }
  })

  // Create invitation mutation
  const createInvitationMutation = useMutation({
    mutationFn: async ({ orgId, email, role }: { orgId: string; email: string; role: OrganizationRole }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.createInvitation(orgId, { email, role })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['org-invitations'] })
      setInviteDialogOpen(false)
      setInviteEmail('')
      setInviteRole('Member')
    }
  })

  // Revoke invitation mutation
  const revokeInvitationMutation = useMutation({
    mutationFn: async ({ orgId, invitationId }: { orgId: string; invitationId: string }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.revokeInvitation(orgId, invitationId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['org-invitations'] })
    }
  })

  const handleCreate = (): void => {
    setEditingOrg(null)
    setDialogOpen(true)
  }

  const handleEdit = (org: OrganizationApiResponse): void => {
    setEditingOrg(org)
    setDialogOpen(true)
    setContextMenu(null)
  }

  const handleSubmit = async (
    data: CreateOrganizationApiRequest | UpdateOrganizationApiRequest
  ): Promise<void> => {
    if (editingOrg) {
      await updateOrgMutation.mutateAsync({
        id: editingOrg.organizationId,
        data: data as UpdateOrganizationApiRequest
      })
    } else {
      await createOrgMutation.mutateAsync(data as CreateOrganizationApiRequest)
    }
  }

  const handleDelete = async (id: string): Promise<void> => {
    if (confirmDelete !== id) {
      setConfirmDelete(id)
      return
    }
    await deleteOrgMutation.mutateAsync(id)
    setContextMenu(null)
  }

  const handleInvite = async (): Promise<void> => {
    if (!selectedOrg || !inviteEmail) return
    await createInvitationMutation.mutateAsync({
      orgId: selectedOrg.organizationId,
      email: inviteEmail,
      role: inviteRole
    })
  }

  const getRoleIcon = (role: string): ReactElement => {
    switch (role) {
      case 'Owner':
        return <Crown className="h-4 w-4 text-warning" />
      case 'Admin':
        return <Shield className="h-4 w-4 text-primary" />
      default:
        return <UserCheck className="h-4 w-4 text-muted-foreground" />
    }
  }

  if (!activeConnection) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="text-center text-muted-foreground">
          <Building2 className="h-12 w-12 mx-auto mb-4 opacity-50" />
          <p>Select a connection to manage organizations</p>
        </div>
      </div>
    )
  }

  return (
    <div className="h-full flex">
      {/* Organizations List */}
      <div className="w-80 border-r flex flex-col">
        <div className="flex items-center justify-between p-4 border-b">
          <h1 className="text-lg font-semibold">Organizations</h1>
          <Button size="sm" onClick={handleCreate}>
            <Plus className="h-4 w-4 mr-1" />
            New
          </Button>
        </div>

        <div className="flex-1 overflow-auto p-2">
          {orgsLoading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
            </div>
          ) : organizations.length === 0 ? (
            <div className="text-center py-8 text-muted-foreground">
              <Building2 className="h-10 w-10 mx-auto mb-2 opacity-50" />
              <p className="text-sm">No organizations yet</p>
            </div>
          ) : (
            <div className="space-y-1">
              {organizations.map((org) => (
                <div
                  key={org.organizationId}
                  className={cn(
                    'group flex items-center justify-between rounded-md px-3 py-2 cursor-pointer',
                    'hover:bg-muted transition-colors',
                    selectedOrg?.organizationId === org.organizationId && 'bg-muted'
                  )}
                  onClick={() => setSelectedOrg(org)}
                  onContextMenu={(e) => {
                    e.preventDefault()
                    setContextMenu({ id: org.organizationId, x: e.clientX, y: e.clientY })
                  }}
                >
                  <div className="flex items-center gap-2 min-w-0">
                    <Building2 className="h-4 w-4 flex-shrink-0" />
                    <div className="min-w-0">
                      <div className="font-medium truncate">{org.name}</div>
                      <div className="text-xs text-muted-foreground truncate">{org.slug}</div>
                    </div>
                  </div>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-6 w-6 opacity-0 group-hover:opacity-100"
                    onClick={(e) => {
                      e.stopPropagation()
                      setContextMenu({ id: org.organizationId, x: e.clientX, y: e.clientY })
                    }}
                  >
                    <MoreVertical className="h-3 w-3" />
                  </Button>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Detail Panel */}
      <div className="flex-1 flex flex-col">
        {selectedOrg ? (
          <>
            {/* Header */}
            <div className="flex items-center justify-between p-4 border-b">
              <div>
                <h2 className="text-lg font-semibold">{selectedOrg.name}</h2>
                <p className="text-sm text-muted-foreground">{selectedOrg.description || 'No description'}</p>
              </div>
              <div className="flex gap-2">
                {activeTab === 'members' && (
                  <Button variant="outline" onClick={() => setInviteDialogOpen(true)}>
                    <UserPlus className="h-4 w-4 mr-2" />
                    Invite Member
                  </Button>
                )}
              </div>
            </div>

            {/* Tabs */}
            <div className="flex gap-1 p-2 border-b bg-muted/30">
              <button
                onClick={() => setActiveTab('members')}
                className={cn(
                  'flex items-center gap-2 px-3 py-1.5 text-sm rounded-md transition-colors',
                  activeTab === 'members'
                    ? 'bg-background shadow-sm'
                    : 'text-muted-foreground hover:text-foreground'
                )}
              >
                <Users className="h-4 w-4" />
                Members
                <span className="text-xs bg-muted-foreground/20 px-1.5 py-0.5 rounded">
                  {members.length}
                </span>
              </button>
              <button
                onClick={() => setActiveTab('invitations')}
                className={cn(
                  'flex items-center gap-2 px-3 py-1.5 text-sm rounded-md transition-colors',
                  activeTab === 'invitations'
                    ? 'bg-background shadow-sm'
                    : 'text-muted-foreground hover:text-foreground'
                )}
              >
                <Mail className="h-4 w-4" />
                Invitations
                <span className="text-xs bg-muted-foreground/20 px-1.5 py-0.5 rounded">
                  {invitations.length}
                </span>
              </button>
            </div>

            {/* Content */}
            <div className="flex-1 overflow-auto p-4">
              {activeTab === 'members' && (
                <>
                  {membersLoading ? (
                    <div className="flex items-center justify-center py-8">
                      <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
                    </div>
                  ) : members.length === 0 ? (
                    <div className="text-center py-8 text-muted-foreground">
                      <Users className="h-10 w-10 mx-auto mb-2 opacity-50" />
                      <p>No members yet</p>
                    </div>
                  ) : (
                    <div className="space-y-2">
                      {members.map((member) => (
                        <div
                          key={member.memberId}
                          className="flex items-center justify-between rounded-lg border p-3"
                        >
                          <div className="flex items-center gap-3">
                            <div className="h-10 w-10 rounded-full bg-muted flex items-center justify-center">
                              {getRoleIcon(member.role)}
                            </div>
                            <div>
                              <div className="font-medium">
                                {member.displayName || member.email}
                              </div>
                              <div className="text-sm text-muted-foreground">{member.email}</div>
                            </div>
                          </div>
                          <div className="flex items-center gap-2">
                            <select
                              value={member.role}
                              onChange={(e) =>
                                updateMemberMutation.mutate({
                                  orgId: selectedOrg.organizationId,
                                  memberId: member.memberId,
                                  role: e.target.value as OrganizationRole
                                })
                              }
                              disabled={member.role === 'Owner'}
                              className="h-8 rounded-md border border-input bg-background px-2 text-sm"
                            >
                              {ROLE_OPTIONS.map((opt) => (
                                <option key={opt.value} value={opt.value}>
                                  {opt.label}
                                </option>
                              ))}
                            </select>
                            {member.role !== 'Owner' && (
                              <Button
                                variant="ghost"
                                size="icon"
                                className="h-8 w-8 text-destructive hover:text-destructive"
                                onClick={() =>
                                  removeMemberMutation.mutate({
                                    orgId: selectedOrg.organizationId,
                                    memberId: member.memberId
                                  })
                                }
                              >
                                <Trash2 className="h-4 w-4" />
                              </Button>
                            )}
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </>
              )}

              {activeTab === 'invitations' && (
                <>
                  {invitationsLoading ? (
                    <div className="flex items-center justify-center py-8">
                      <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
                    </div>
                  ) : invitations.length === 0 ? (
                    <div className="text-center py-8 text-muted-foreground">
                      <Mail className="h-10 w-10 mx-auto mb-2 opacity-50" />
                      <p>No pending invitations</p>
                    </div>
                  ) : (
                    <div className="space-y-2">
                      {invitations.map((invitation) => (
                        <div
                          key={invitation.invitationId}
                          className="flex items-center justify-between rounded-lg border p-3"
                        >
                          <div className="flex items-center gap-3">
                            <div className="h-10 w-10 rounded-full bg-muted flex items-center justify-center">
                              <Clock className="h-4 w-4 text-warning" />
                            </div>
                            <div>
                              <div className="font-medium">{invitation.email}</div>
                              <div className="text-sm text-muted-foreground">
                                Invited as {invitation.role} by {invitation.invitedBy}
                              </div>
                              <div className="text-xs text-muted-foreground">
                                Expires: {new Date(invitation.expiresAt).toLocaleDateString()}
                              </div>
                            </div>
                          </div>
                          <Button
                            variant="ghost"
                            size="sm"
                            className="text-destructive hover:text-destructive"
                            onClick={() =>
                              revokeInvitationMutation.mutate({
                                orgId: selectedOrg.organizationId,
                                invitationId: invitation.invitationId
                              })
                            }
                          >
                            Revoke
                          </Button>
                        </div>
                      ))}
                    </div>
                  )}
                </>
              )}
            </div>
          </>
        ) : (
          <div className="flex-1 flex items-center justify-center text-muted-foreground">
            <div className="text-center">
              <Building2 className="h-12 w-12 mx-auto mb-4 opacity-50" />
              <p>Select an organization to view details</p>
            </div>
          </div>
        )}
      </div>

      {/* Context Menu */}
      {contextMenu && (
        <>
          <div className="fixed inset-0 z-40" onClick={() => setContextMenu(null)} />
          <div
            className="fixed z-50 min-w-[160px] rounded-md border bg-popover p-1 shadow-md"
            style={{
              left: Math.min(contextMenu.x, window.innerWidth - 180),
              top: Math.min(contextMenu.y, window.innerHeight - 120)
            }}
          >
            <button
              onClick={() => {
                const org = organizations.find((o) => o.organizationId === contextMenu.id)
                if (org) handleEdit(org)
              }}
              className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
            >
              <Pencil className="h-4 w-4" />
              Edit
            </button>
            <button
              onClick={() => handleDelete(contextMenu.id)}
              className={cn(
                'flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent',
                confirmDelete === contextMenu.id && 'text-destructive hover:bg-destructive/10'
              )}
            >
              <Trash2 className="h-4 w-4" />
              {confirmDelete === contextMenu.id ? 'Click again to confirm' : 'Delete'}
            </button>
          </div>
        </>
      )}

      {/* Invite Dialog */}
      {inviteDialogOpen && selectedOrg && (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div className="fixed inset-0 bg-black/50" onClick={() => setInviteDialogOpen(false)} />
          <div className="relative z-10 w-full max-w-sm rounded-lg border bg-background p-6 shadow-lg">
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-lg font-semibold">Invite Member</h2>
              <Button variant="ghost" size="icon" onClick={() => setInviteDialogOpen(false)}>
                <X className="h-4 w-4" />
              </Button>
            </div>

            <div className="space-y-4">
              <div className="space-y-2">
                <label className="text-sm font-medium">Email</label>
                <Input
                  type="email"
                  value={inviteEmail}
                  onChange={(e) => setInviteEmail(e.target.value)}
                  placeholder="user@example.com"
                />
              </div>

              <div className="space-y-2">
                <label className="text-sm font-medium">Role</label>
                <select
                  value={inviteRole}
                  onChange={(e) => setInviteRole(e.target.value as OrganizationRole)}
                  className="w-full h-9 rounded-md border border-input bg-background px-3 text-sm"
                >
                  {ROLE_OPTIONS.filter((r) => r.value !== 'Owner').map((opt) => (
                    <option key={opt.value} value={opt.value}>
                      {opt.label}
                    </option>
                  ))}
                </select>
              </div>

              <div className="flex justify-end gap-2 pt-4">
                <Button variant="ghost" onClick={() => setInviteDialogOpen(false)}>
                  Cancel
                </Button>
                <Button
                  onClick={handleInvite}
                  disabled={!inviteEmail || createInvitationMutation.isPending}
                >
                  {createInvitationMutation.isPending ? 'Sending...' : 'Send Invitation'}
                </Button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Organization Dialog */}
      <OrganizationDialog
        open={dialogOpen}
        onClose={() => setDialogOpen(false)}
        onSubmit={handleSubmit}
        organization={editingOrg}
      />
    </div>
  )
}

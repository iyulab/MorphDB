import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { Connection, ConnectionFormData, ConnectionStatus } from '@/types/connection'

interface ConnectionState {
  connections: Connection[]
  activeConnectionId: string | null
  activeConnection: Connection | null

  // Actions
  addConnection: (data: ConnectionFormData) => Promise<Connection>
  updateConnection: (id: string, data: Partial<ConnectionFormData>) => Promise<void>
  removeConnection: (id: string) => Promise<void>
  setActiveConnection: (id: string | null) => void
  touchConnection: (id: string) => void
  setConnectionStatus: (id: string, status: ConnectionStatus, errorMessage?: string) => void
  testConnection: (id: string) => Promise<boolean>
  connectToServer: (id: string) => Promise<boolean>
  disconnectFromServer: (id: string) => void
  getRecentConnections: () => Connection[]
}

export const useConnectionStore = create<ConnectionState>()(
  persist(
    (set, get) => ({
      connections: [],
      activeConnectionId: null,
      activeConnection: null,

      addConnection: async (data) => {
        const newConnection: Connection = {
          id: crypto.randomUUID(),
          name: data.name,
          url: data.url,
          createdAt: new Date().toISOString(),
          status: 'disconnected'
        }

        set((state) => ({
          connections: [...state.connections, newConnection]
        }))

        return newConnection
      },

      updateConnection: async (id, data) => {
        set((state) => ({
          connections: state.connections.map((conn) =>
            conn.id === id
              ? {
                  ...conn,
                  name: data.name ?? conn.name,
                  url: data.url ?? conn.url
                }
              : conn
          ),
          activeConnection:
            state.activeConnectionId === id
              ? {
                  ...state.activeConnection!,
                  name: data.name ?? state.activeConnection!.name,
                  url: data.url ?? state.activeConnection!.url
                }
              : state.activeConnection
        }))
      },

      removeConnection: async (id) => {
        set((state) => ({
          connections: state.connections.filter((conn) => conn.id !== id),
          activeConnectionId: state.activeConnectionId === id ? null : state.activeConnectionId,
          activeConnection: state.activeConnectionId === id ? null : state.activeConnection
        }))
      },

      setActiveConnection: (id) => {
        const connection = id ? get().connections.find((c) => c.id === id) : null
        set({
          activeConnectionId: id,
          activeConnection: connection ?? null
        })

        if (id) {
          get().touchConnection(id)
        }
      },

      touchConnection: (id) => {
        set((state) => ({
          connections: state.connections.map((conn) =>
            conn.id === id ? { ...conn, lastUsedAt: new Date().toISOString() } : conn
          )
        }))
      },

      setConnectionStatus: (id, status, errorMessage) => {
        set((state) => ({
          connections: state.connections.map((conn) =>
            conn.id === id ? { ...conn, status, errorMessage } : conn
          ),
          activeConnection:
            state.activeConnectionId === id && state.activeConnection
              ? { ...state.activeConnection, status, errorMessage }
              : state.activeConnection
        }))
      },

      testConnection: async (id) => {
        const connection = get().connections.find((c) => c.id === id)
        if (!connection) return false

        get().setConnectionStatus(id, 'connecting')

        const result = await window.api.testConnection(connection.url)

        if (result.success) {
          get().setConnectionStatus(id, 'connected')
          return true
        } else {
          get().setConnectionStatus(id, 'error', result.error)
          return false
        }
      },

      connectToServer: async (id) => {
        const success = await get().testConnection(id)
        if (success) {
          get().touchConnection(id)
        }
        return success
      },

      disconnectFromServer: (id) => {
        get().setConnectionStatus(id, 'disconnected')
      },

      getRecentConnections: () => {
        return [...get().connections].sort((a, b) => {
          const aTime = a.lastUsedAt ? new Date(a.lastUsedAt).getTime() : 0
          const bTime = b.lastUsedAt ? new Date(b.lastUsedAt).getTime() : 0
          return bTime - aTime
        })
      }
    }),
    {
      name: 'morphdb-connections',
      partialize: (state) => ({
        connections: state.connections,
        activeConnectionId: state.activeConnectionId
      })
    }
  )
)

import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { Connection, ConnectionFormData } from '@/types/connection'

interface ConnectionState {
  connections: Connection[]
  activeConnectionId: string | null
  activeConnection: Connection | null

  // Actions
  addConnection: (data: ConnectionFormData) => Connection
  updateConnection: (id: string, data: Partial<ConnectionFormData>) => void
  removeConnection: (id: string) => void
  setActiveConnection: (id: string | null) => void
  touchConnection: (id: string) => void
}

export const useConnectionStore = create<ConnectionState>()(
  persist(
    (set, get) => ({
      connections: [],
      activeConnectionId: null,
      activeConnection: null,

      addConnection: (data) => {
        const newConnection: Connection = {
          id: crypto.randomUUID(),
          ...data,
          createdAt: new Date().toISOString()
        }

        set((state) => ({
          connections: [...state.connections, newConnection]
        }))

        return newConnection
      },

      updateConnection: (id, data) => {
        set((state) => ({
          connections: state.connections.map((conn) =>
            conn.id === id ? { ...conn, ...data } : conn
          )
        }))
      },

      removeConnection: (id) => {
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
      }
    }),
    {
      name: 'morphdb-connections',
      partialize: (state) => ({
        connections: state.connections.map(({ apiKey, ...rest }) => ({
          ...rest,
          apiKey: '***' // Don't persist API keys in plain text
        }))
      })
    }
  )
)

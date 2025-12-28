import { create } from 'zustand'
import type { TableApiResponse } from '@/lib/api'

export interface ExplorerState {
  selectedTable: string | null
  expandedNodes: Set<string>
  isLoading: boolean
  error: string | null
  tables: TableApiResponse[]

  // Actions
  setSelectedTable: (tableName: string | null) => void
  toggleNode: (nodeId: string) => void
  expandNode: (nodeId: string) => void
  collapseNode: (nodeId: string) => void
  setTables: (tables: TableApiResponse[]) => void
  setLoading: (loading: boolean) => void
  setError: (error: string | null) => void
  reset: () => void
}

export const useExplorerStore = create<ExplorerState>((set) => ({
  selectedTable: null,
  expandedNodes: new Set<string>(),
  isLoading: false,
  error: null,
  tables: [],

  setSelectedTable: (tableName) => {
    set({ selectedTable: tableName })
  },

  toggleNode: (nodeId) => {
    set((state) => {
      const expanded = new Set(state.expandedNodes)
      if (expanded.has(nodeId)) {
        expanded.delete(nodeId)
      } else {
        expanded.add(nodeId)
      }
      return { expandedNodes: expanded }
    })
  },

  expandNode: (nodeId) => {
    set((state) => {
      const expanded = new Set(state.expandedNodes)
      expanded.add(nodeId)
      return { expandedNodes: expanded }
    })
  },

  collapseNode: (nodeId) => {
    set((state) => {
      const expanded = new Set(state.expandedNodes)
      expanded.delete(nodeId)
      return { expandedNodes: expanded }
    })
  },

  setTables: (tables) => {
    set({ tables })
  },

  setLoading: (loading) => {
    set({ isLoading: loading })
  },

  setError: (error) => {
    set({ error })
  },

  reset: () => {
    set({
      selectedTable: null,
      expandedNodes: new Set<string>(),
      isLoading: false,
      error: null,
      tables: []
    })
  }
}))

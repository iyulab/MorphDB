import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface LayoutState {
  sidebarCollapsed: boolean
  sidebarWidth: number
  toggleSidebar: () => void
  setSidebarCollapsed: (collapsed: boolean) => void
  setSidebarWidth: (width: number) => void
}

const DEFAULT_SIDEBAR_WIDTH = 224 // 14rem = 224px (w-56)
const COLLAPSED_SIDEBAR_WIDTH = 56 // Icon-only width

export const useLayoutStore = create<LayoutState>()(
  persist(
    (set) => ({
      sidebarCollapsed: false,
      sidebarWidth: DEFAULT_SIDEBAR_WIDTH,

      toggleSidebar: () =>
        set((state) => ({
          sidebarCollapsed: !state.sidebarCollapsed,
          sidebarWidth: state.sidebarCollapsed ? DEFAULT_SIDEBAR_WIDTH : COLLAPSED_SIDEBAR_WIDTH
        })),

      setSidebarCollapsed: (collapsed) =>
        set({
          sidebarCollapsed: collapsed,
          sidebarWidth: collapsed ? COLLAPSED_SIDEBAR_WIDTH : DEFAULT_SIDEBAR_WIDTH
        }),

      setSidebarWidth: (width) => set({ sidebarWidth: width })
    }),
    {
      name: 'morphdb-layout',
      partialize: (state) => ({
        sidebarCollapsed: state.sidebarCollapsed,
        sidebarWidth: state.sidebarWidth
      })
    }
  )
)

/** @type {import('tailwindcss').Config} */
export default {
  content: ['./src/renderer/**/*.{js,ts,jsx,tsx,html}'],
  theme: {
    extend: {
      colors: {
        background: 'oklch(0.145 0 0)',
        foreground: 'oklch(0.985 0 0)',
        card: {
          DEFAULT: 'oklch(0.175 0 0)',
          foreground: 'oklch(0.985 0 0)'
        },
        popover: {
          DEFAULT: 'oklch(0.175 0 0)',
          foreground: 'oklch(0.985 0 0)'
        },
        primary: {
          DEFAULT: 'oklch(0.65 0.18 250)',
          foreground: 'oklch(0.985 0 0)'
        },
        secondary: {
          DEFAULT: 'oklch(0.27 0 0)',
          foreground: 'oklch(0.985 0 0)'
        },
        muted: {
          DEFAULT: 'oklch(0.27 0 0)',
          foreground: 'oklch(0.65 0 0)'
        },
        accent: {
          DEFAULT: 'oklch(0.27 0 0)',
          foreground: 'oklch(0.985 0 0)'
        },
        destructive: {
          DEFAULT: 'oklch(0.55 0.2 25)',
          foreground: 'oklch(0.985 0 0)'
        },
        border: 'oklch(0.3 0 0)',
        input: 'oklch(0.3 0 0)',
        ring: 'oklch(0.65 0.18 250)',
        sidebar: {
          DEFAULT: 'oklch(0.12 0 0)',
          foreground: 'oklch(0.85 0 0)',
          border: 'oklch(0.25 0 0)',
          hover: 'oklch(0.2 0 0)',
          active: 'oklch(0.25 0.05 250)'
        },
        success: 'oklch(0.65 0.18 145)',
        warning: 'oklch(0.75 0.15 85)',
        error: 'oklch(0.55 0.2 25)',
        info: 'oklch(0.65 0.18 250)'
      },
      borderRadius: {
        sm: '0.25rem',
        DEFAULT: '0.375rem',
        md: '0.375rem',
        lg: '0.5rem',
        xl: '0.75rem'
      }
    }
  },
  plugins: []
}

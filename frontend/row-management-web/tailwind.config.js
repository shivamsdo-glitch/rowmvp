/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  darkMode: 'class',
  theme: {
    container: {
      center: true,
      padding: '1.5rem',
      screens: { '2xl': '1400px' },
    },
    extend: {
      colors: {
        // Tokens — driven by CSS vars in index.css.
        border:           'hsl(var(--border))',
        'border-strong':  'hsl(var(--border-strong))',
        input:            'hsl(var(--input))',
        ring:             'hsl(var(--ring))',
        background:       'hsl(var(--background))',
        foreground:       'hsl(var(--foreground))',
        subtle:           'hsl(var(--subtle))',
        primary: {
          DEFAULT:     'hsl(var(--primary))',
          foreground:  'hsl(var(--primary-foreground))',
          soft:        'hsl(var(--primary-soft))',
        },
        secondary: {
          DEFAULT:     'hsl(var(--secondary))',
          foreground:  'hsl(var(--secondary-foreground))',
        },
        destructive: {
          DEFAULT:     'hsl(var(--destructive))',
          foreground:  'hsl(var(--destructive-foreground))',
        },
        muted: {
          DEFAULT:     'hsl(var(--muted))',
          foreground:  'hsl(var(--muted-foreground))',
        },
        accent: {
          DEFAULT:     'hsl(var(--accent))',
          foreground:  'hsl(var(--accent-foreground))',
          soft:        'hsl(var(--accent-soft))',
        },
        card: {
          DEFAULT:     'hsl(var(--card))',
          foreground:  'hsl(var(--card-foreground))',
        },
        // Status palette — single source for condition / RUL / risk badges and charts.
        ok:     'hsl(var(--ok))',
        warn:   'hsl(var(--warn))',
        alert:  'hsl(var(--alert))',
        danger: 'hsl(var(--danger))',
        info:   'hsl(var(--info))',
        // Sidebar tokens
        sidebar: {
          DEFAULT:     'hsl(var(--sidebar))',
          foreground:  'hsl(var(--sidebar-foreground))',
          hover:       'hsl(var(--sidebar-hover))',
          active:      'hsl(var(--sidebar-active))',
          border:      'hsl(var(--sidebar-border))',
        },
        // Aurigo brand reference. `red` is the shared brand red (notification badges,
        // required-field marks). `sidebar`/`teal` re-pointed to the Masterworks navy/blue.
        aurigo: {
          red:    '#e11d2e',
          teal:   '#1668dc',
          sidebar: '#141b2d',
          'sidebar-hover': '#1f2942',
        },
        grade: {
          excellent: 'hsl(152 60% 38%)',
          good:      'hsl(112 55% 40%)',
          fair:      'hsl(38 92% 50%)',
          poor:      'hsl(22 92% 52%)',
          critical:  'hsl(354 75% 49%)',
        },
      },
      fontFamily: {
        sans:  ['Roboto', 'ui-sans-serif', 'system-ui', 'sans-serif'],
        serif: ['Roboto', 'ui-sans-serif', 'system-ui', 'sans-serif'],
        mono:  ['Roboto Mono', 'source-code-pro', 'Menlo', 'Monaco', 'Consolas', 'Courier New', 'monospace'],
      },
      borderRadius: {
        lg: 'var(--radius-lg)',
        md: 'var(--radius)',
        sm: 'var(--radius-sm)',
      },
      boxShadow: {
        'aurigo-card':  '0 1px 2px rgb(15 23 42 / 0.04), 0 1px 4px rgb(15 23 42 / 0.06)',
        'aurigo-pop':   '0 1px 2px rgb(15 23 42 / 0.04), 0 6px 16px -10px rgb(15 23 42 / 0.10)',
        'aurigo-modal': '0 2px 4px rgb(15 23 42 / 0.05), 0 24px 48px -24px rgb(15 23 42 / 0.22)',
        'aurigo-focus': '0 0 0 3px hsl(var(--ring) / 0.18)',
      },
      transitionTimingFunction: {
        'out-quint': 'cubic-bezier(0.22, 1, 0.36, 1)',
      },
      keyframes: {
        'fade-in':         { '0%': { opacity: '0' },                              '100%': { opacity: '1' } },
        'rise-in':         { '0%': { opacity: '0', transform: 'translateY(4px)' }, '100%': { opacity: '1', transform: 'translateY(0)' } },
        'sheen':           { '0%': { backgroundPosition: '-200% 0' },             '100%': { backgroundPosition: '200% 0' } },
        'slide-in-right':  { '0%': { transform: 'translateX(100%)' },             '100%': { transform: 'translateX(0)' } },
      },
      animation: {
        'fade-in':        'fade-in 240ms cubic-bezier(0.22, 1, 0.36, 1)',
        'rise-in':        'rise-in 320ms cubic-bezier(0.22, 1, 0.36, 1) both',
        'sheen':          'sheen 1400ms linear infinite',
        'slide-in-right': 'slide-in-right 280ms cubic-bezier(0.22, 1, 0.36, 1)',
      },
    },
  },
  plugins: [],
}

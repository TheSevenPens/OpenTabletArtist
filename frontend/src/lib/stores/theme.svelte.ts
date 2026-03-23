type Theme = 'dark' | 'light';

const STORAGE_KEY = 'otd-ux-theme';

function getInitialTheme(): Theme {
  if (typeof window === 'undefined') return 'light';
  const stored = localStorage.getItem(STORAGE_KEY);
  if (stored === 'dark' || stored === 'light') return stored;
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

function applyTheme(t: Theme) {
  document.documentElement.dataset.theme = t;
  localStorage.setItem(STORAGE_KEY, t);
}

export const themeStore = createThemeStore();

function createThemeStore() {
  let theme = $state<Theme>(getInitialTheme());

  // Apply immediately on creation
  applyTheme(theme);

  return {
    get current() { return theme; },
    get isDark() { return theme === 'dark'; },
    toggle() {
      theme = theme === 'dark' ? 'light' : 'dark';
      applyTheme(theme);
    },
    set(t: Theme) {
      theme = t;
      applyTheme(theme);
    }
  };
}

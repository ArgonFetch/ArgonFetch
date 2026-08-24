import DefaultTheme from 'vitepress/theme'
import type { Theme } from 'vitepress'
import { theme, useOpenapi } from 'vitepress-openapi/client'
import 'vitepress-openapi/dist/style.css'
import './custom.css'

import spec from '../../public/openapi.json' with { type: 'json' }

// The schema is regenerated straight from Swagger (see docs/dev-setup.md), and Swagger writes no
// `servers` block. Filling one in here rather than in the file means a refresh cannot drop it.
// The hosted instance goes first so the playground works without anything running locally;
// `allowCustomServer` lets a self-hoster point it at their own instance instead.
const specWithServers = {
  ...spec,
  servers: (spec as { servers?: unknown[] }).servers?.length
    ? (spec as { servers?: unknown[] }).servers
    : [
        { url: 'https://app.argonfetch.dev', description: 'The hosted instance' },
        { url: 'http://localhost:8080', description: 'A local ArgonFetch instance' },
      ],
}

// vitepress-openapi 0.2.4 seeds its "use a custom server" flag from `allowCustomServer`
// (OAPlaygroundParameters.vue), so switching custom servers on also starts the playground *in*
// custom mode: the free-text input renders over the server dropdown and its clear-X lands on the
// dropdown's chevron. The server-rendered markup has no localStorage and picks the other state,
// which is the hydration mismatch the console reports. Seed the flag to the dropdown instead -
// only when unset, so a reader who has chosen "Custom Server" keeps their choice.
function preferServerDropdown() {
  try {
    if (typeof localStorage === 'undefined') {
      return
    }
    if (localStorage.getItem('--oa-use-custom-server') === null) {
      localStorage.setItem('--oa-use-custom-server', 'false')
    }
  } catch {
    // Private windows and blocked site data throw on access; the default is harmless either way.
  }
}

export default {
  extends: DefaultTheme,
  async enhanceApp({ app }) {
    preferServerDropdown()

    useOpenapi({
      spec: specWithServers,
      config: {
        server: {
          allowCustomServer: true,
        },
      },
    })

    theme.enhanceApp({ app })
  },
} satisfies Theme

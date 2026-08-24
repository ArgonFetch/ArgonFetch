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

export default {
  extends: DefaultTheme,
  async enhanceApp({ app }) {
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

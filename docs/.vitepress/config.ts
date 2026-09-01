import { defineConfig } from 'vitepress'
import { minifyHtml, useSidebar } from 'vitepress-openapi'
import spec from '../public/openapi.json' with { type: 'json' }

// The API reference is generated from the schema rather than written by hand: one page per
// operation under /operations/, each with its own request playground.
const openapiSidebar = useSidebar({
  spec,
  linkPrefix: '/operations/',
  // Swagger emits no `summary` for these operations, so the default label falls back to
  // "GET /api/App" and repeats the method the badge beside it already shows. Path only.
  sidebarItemTemplate: ({ method, path }: { method: string, path: string }) => minifyHtml(`
    <span class="OASidebarItem group/oaOperationLink">
      <span class="OASidebarItem-badge OAMethodBadge--${method.toLowerCase()}">${method.toUpperCase()}</span>
      <span class="OASidebarItem-text text">${path}</span>
    </span>
  `),
})

// Docs site for ArgonFetch, built from the markdown in this folder. Served at the root of
// docs.argonfetch.dev, so no `base` is needed. Build: `vitepress build` (output: .vitepress/dist).
export default defineConfig({
  title: 'ArgonFetch',
  description: 'Self-hosted media downloader. Paste a link, get the file - no account, no API keys.',
  lastUpdated: true,
  cleanUrls: true,
  // The same files are read on GitHub, where links are written as "docs/*.md".
  ignoreDeadLinks: true,
  head: [
    ['link', { rel: 'icon', href: '/favicon.svg' }],
  ],
  // Frontmatter cannot read a dynamic route's params, so the generated operation pages get their
  // browser title here instead - otherwise all eight share the site title.
  transformPageData(pageData) {
    if (pageData.params?.pageTitle) {
      pageData.title = pageData.params.pageTitle
    }
  },
  themeConfig: {
    logo: '/logo.svg',
    nav: [
      { text: 'Intro', link: '/intro' },
      { text: 'Setup', link: '/self-host' },
      { text: 'Usage', link: '/usage' },
      { text: 'API', link: '/api' },
      { text: 'MCP', link: '/mcp' },
      { text: 'Development', link: '/dev-setup' },
      { text: 'Live app', link: 'https://app.argonfetch.dev' },
    ],
    sidebar: [
      { text: 'What is ArgonFetch?', link: '/intro' },
      {
        text: 'Setup',
        collapsed: false,
        items: [
          { text: 'Self-hosting', link: '/self-host' },
          { text: 'Configuration', link: '/configuration' },
        ],
      },
      {
        text: 'Guides',
        collapsed: false,
        items: [
          { text: 'Usage', link: '/usage' },
          { text: 'Supported platforms', link: '/platforms' },
          { text: 'MCP for AI assistants', link: '/mcp' },
        ],
      },
      {
        text: 'API',
        collapsed: false,
        items: [
          { text: 'Overview', link: '/api' },
          ...openapiSidebar.generateSidebarGroups({ linkPrefix: '/operations/' }),
        ],
      },
      {
        text: 'Development',
        collapsed: false,
        items: [
          { text: 'Developer setup', link: '/dev-setup' },
        ],
      },
    ],
    socialLinks: [
      { icon: 'github', link: 'https://github.com/ArgonFetch/ArgonFetch' },
    ],
    search: { provider: 'local' },
    editLink: {
      pattern: 'https://github.com/ArgonFetch/ArgonFetch/edit/main/docs/:path',
      text: 'Edit this page on GitHub',
    },
    footer: {
      message: 'Released under the GPL-3.0 License.',
      copyright: 'ArgonFetch',
    },
  },
})

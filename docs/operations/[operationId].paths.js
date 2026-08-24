import { usePaths } from 'vitepress-openapi'
import spec from '../public/openapi.json' with { type: 'json' }

// One page per operation in the schema. Adding an endpoint to the API and refreshing
// docs/public/openapi.json is all it takes for its page to appear.
export default {
  paths() {
    return usePaths({ spec })
      .getPathsByVerbs()
      .map(({ operationId, summary }) => ({
        params: {
          operationId,
          pageTitle: summary || operationId,
        },
      }))
  },
}

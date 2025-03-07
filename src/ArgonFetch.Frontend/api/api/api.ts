export * from './corsProxy.service';
import { CorsProxyService } from './corsProxy.service';
export * from './fetch.service';
import { FetchService } from './fetch.service';
export const APIS = [CorsProxyService, FetchService];

export * from './app.service';
import { AppService } from './app.service';
export * from './fetch.service';
import { FetchService } from './fetch.service';
export * from './proxy.service';
import { ProxyService } from './proxy.service';
export const APIS = [AppService, FetchService, ProxyService];

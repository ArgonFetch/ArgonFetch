export * from './app.service';
import { AppService } from './app.service';
export * from './fetch.service';
import { FetchService } from './fetch.service';
export * from './proxy.service';
import { ProxyService } from './proxy.service';
export * from './stream.service';
import { StreamService } from './stream.service';
export const APIS = [AppService, FetchService, ProxyService, StreamService];

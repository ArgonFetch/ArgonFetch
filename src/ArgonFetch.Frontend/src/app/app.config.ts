import { ApplicationConfig } from '@angular/core';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { provideHttpClient } from '@angular/common/http';
import { Configuration } from '../../api';

export const appConfig: ApplicationConfig = {
  providers: [
    provideHttpClient(),
    {
      provide: Configuration,
      useFactory: () => new Configuration({ basePath: 'http://localhost:5114' }),  // Using the API base URL
    },
    provideRouter(routes)
  ]
};

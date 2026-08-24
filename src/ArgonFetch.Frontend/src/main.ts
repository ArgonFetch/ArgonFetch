import { provideZonelessChangeDetection } from "@angular/core";
import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';

// Zoneless: the app no longer ships zone.js, and change detection runs off signals,
// template events and the async pipe rather than off every patched timer and XHR.
bootstrapApplication(AppComponent, {...appConfig, providers: [provideZonelessChangeDetection(), ...appConfig.providers]})
  .catch((err) => console.error(err));

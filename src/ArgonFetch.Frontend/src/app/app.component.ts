import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterModule } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faSun, faMoon } from '@fortawesome/free-solid-svg-icons';
import { faGithub } from '@fortawesome/free-brands-svg-icons';
import { ThemeService } from './services/theme.service';
import { AppService } from '../app/api';
import { catchError, firstValueFrom, of } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NotificationService } from './notifications/notification.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss'],
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [RouterModule, CommonModule, FontAwesomeModule]
})
export class AppComponent {
  faSun = faSun;
  faMoon = faMoon;
  faGithub = faGithub;
  isDarkTheme$;
  version = 'unknown';
  environment = 'unknown';

  constructor(
    private themeService: ThemeService,
    private appService: AppService,
    private notifications: NotificationService
  ) {
    this.initializeApp();
    this.isDarkTheme$ = this.themeService.isDarkTheme$;
  }

  async initializeApp() {
    const appInfo = await firstValueFrom(
      this.appService.getAppInfo().pipe(
        takeUntilDestroyed(),
        catchError(() => {
          this.notifications.show({
            title: 'Backend unreachable',
            message: 'Could not reach the ArgonFetch API. Downloads will not work until it is back.',
            tone: 'error',
            // Stays until dismissed: nothing works while the backend is down.
            durationMs: 0
          });
          return of({ version: 'unknown', environment: 'unknown' });
        })
      )
    );

    this.version = appInfo.version!;
    this.environment = appInfo.environment!;
  }

  toggleTheme() {
    this.themeService.toggleTheme();
  }
}

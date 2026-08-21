import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterModule } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faSun, faMoon, faGear } from '@fortawesome/free-solid-svg-icons';
import { faGithub } from '@fortawesome/free-brands-svg-icons';
import { ThemeService } from './services/theme.service';
import { AppService } from '../app/api';
import { catchError, firstValueFrom, of } from 'rxjs';
import { DestroyRef, inject } from '@angular/core';
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
  faGear = faGear;
  isDarkTheme$;
  version = 'unknown';
  environment = 'unknown';
  maintenance: string | null = null;

  // Polled rather than pushed: the only maintenance is a yt-dlp update that lasts seconds, so
  // one small request a minute is cheaper than any kind of live connection. While it is running
  // the screen has to clear promptly, so the interval tightens.
  private static readonly IDLE_POLL_MS = 60_000;
  private static readonly MAINTENANCE_POLL_MS = 5_000;
  private pollTimer?: ReturnType<typeof setTimeout>;
  private destroyRef = inject(DestroyRef);

  constructor(
    private themeService: ThemeService,
    private appService: AppService,
    private notifications: NotificationService
  ) {
    this.initializeApp();
    this.isDarkTheme$ = this.themeService.isDarkTheme$;
    this.destroyRef.onDestroy(() => clearTimeout(this.pollTimer));
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
          return of({ version: 'unknown', environment: 'unknown', maintenance: null });
        })
      )
    );

    this.version = appInfo.version!;
    this.environment = appInfo.environment!;
    this.maintenance = appInfo.maintenance ?? null;
    this.scheduleMaintenanceCheck();
  }

  private scheduleMaintenanceCheck() {
    clearTimeout(this.pollTimer);

    this.pollTimer = setTimeout(
      () => this.checkMaintenance(),
      this.maintenance ? AppComponent.MAINTENANCE_POLL_MS : AppComponent.IDLE_POLL_MS
    );
  }

  private async checkMaintenance() {
    // A failed check says nothing about maintenance - the backend notification already covers
    // being unreachable - so the previous state stands and the next check decides.
    const appInfo = await firstValueFrom(
      this.appService.getAppInfo().pipe(catchError(() => of(null)))
    );

    if (appInfo) {
      this.maintenance = appInfo.maintenance ?? null;
    }

    this.scheduleMaintenanceCheck();
  }

  toggleTheme() {
    this.themeService.toggleTheme();
  }
}

import { Component } from '@angular/core';
import { RouterModule } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faSun, faMoon } from '@fortawesome/free-solid-svg-icons';
import { faGithub } from '@fortawesome/free-brands-svg-icons';
import { ThemeService } from './services/theme.service';
import { ConfirmationModalComponent } from "./confirmation-modal/confirmation-modal.component";
import { AppService } from '../../api';
import { catchError, firstValueFrom, of } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ModalService } from './services/modal.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss'],
  standalone: true,
  imports: [RouterModule, CommonModule, FontAwesomeModule, ConfirmationModalComponent]
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
    private modalService: ModalService
  ) {
    this.initializeApp();
    this.isDarkTheme$ = this.themeService.isDarkTheme$;
  }

  async initializeApp() {
    const appInfo = await firstValueFrom(
      this.appService.getAppInfo().pipe(
        takeUntilDestroyed(),
        catchError(() => {
          this.modalService.open({
            title: 'Noorr! Backend is Missing in Action',
            confirmationText: 'Houston, we have a problem! The backend server seems to be on vacation. Perhaps it\'s sipping cocktails on a digital beach somewhere?',
            showCancelButton: false,
            showConfirmButton: false,
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

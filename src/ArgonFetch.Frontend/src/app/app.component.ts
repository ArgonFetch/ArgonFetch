import { Component } from '@angular/core';
import { RouterModule } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faSun, faMoon } from '@fortawesome/free-solid-svg-icons';
import { faGithub } from '@fortawesome/free-brands-svg-icons';
import { ThemeService } from './services/theme.service';
import { ConfirmationModalComponent } from "./confirmation-modal/confirmation-modal.component";

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
  version = '1.0.0';

  constructor(private themeService: ThemeService) {
    this.isDarkTheme$ = this.themeService.isDarkTheme$;
  }

  toggleTheme() {
    this.themeService.toggleTheme();
  }
}

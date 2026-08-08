import { Component, Input, ChangeDetectionStrategy } from '@angular/core';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faCircleExclamation, faCircleInfo, faXmark } from '@fortawesome/free-solid-svg-icons';

export type NotificationTone = 'error' | 'info';

@Component({
  selector: 'app-notification',
  standalone: true,
  imports: [FontAwesomeModule],
  changeDetection: ChangeDetectionStrategy.Eager,
  templateUrl: './notification.component.html',
  styleUrl: './notification.component.scss'
})
export class NotificationComponent {
  @Input() title = '';
  @Input() message = '';
  @Input() tone: NotificationTone = 'info';

  /** Set by the service so the close button can dismiss the overlay. */
  @Input() dismiss: () => void = () => { };

  faXmark = faXmark;

  get icon() {
    return this.tone === 'error' ? faCircleExclamation : faCircleInfo;
  }
}

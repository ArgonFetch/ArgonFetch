import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy } from '@angular/core';
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

  /** Raised by the close button; the stack that owns this notification removes it. */
  @Output() dismissed = new EventEmitter<void>();

  faXmark = faXmark;

  get icon() {
    return this.tone === 'error' ? faCircleExclamation : faCircleInfo;
  }
}

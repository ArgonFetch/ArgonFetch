import { CircleAlert, Info, X } from 'lucide';
import { IconComponent } from '../icon/icon.component';
import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy } from '@angular/core';

export type NotificationTone = 'error' | 'info';

@Component({
  selector: 'app-notification',
  standalone: true,
  imports: [IconComponent],
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

  closeIcon = X;

  get icon() {
    return this.tone === 'error' ? CircleAlert : Info;
  }
}

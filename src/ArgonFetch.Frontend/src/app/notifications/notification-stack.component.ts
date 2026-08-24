import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { NotificationComponent } from './notification.component';
import { NotificationService } from './notification.service';

/**
 * Holds every open notification in one overlay.
 * <p>
 * The alternative - an overlay each, offset by hand - has to guess how tall a notification
 * is, and a two-line message then overlaps the one below it. Here the browser stacks them,
 * so any height works and dismissing one closes the gap on its own.
 */
@Component({
  selector: 'app-notification-stack',
  standalone: true,
  imports: [NotificationComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="notification-stack">
      @for (notification of notifications(); track notification.id) {
        <app-notification
          animate.enter="notification-enter"
          animate.leave="notification-leave"
          [title]="notification.title"
          [message]="notification.message"
          [tone]="notification.tone"
          (dismissed)="dismiss(notification.id)" />
      }
    </div>
  `,
  styles: `
    .notification-stack {
      display: flex;
      flex-direction: column;
      /* Newest at the bottom, nearest the corner it grew from. */
      gap: 0.75rem;
      pointer-events: none;
    }

    .notification-stack > * {
      pointer-events: auto;
    }

    /* Slides in from the edge it appears at, and back out on the way away, so a toast
       arriving is noticed without the eye having to hunt for what changed. */
    .notification-enter {
      animation: notification-in 220ms cubic-bezier(0.16, 1, 0.3, 1);
    }

    .notification-leave {
      animation: notification-out 160ms ease-in forwards;
    }

    @keyframes notification-in {
      from {
        opacity: 0;
        transform: translateX(1.5rem) scale(0.97);
      }
    }

    @keyframes notification-out {
      to {
        opacity: 0;
        transform: translateX(1.5rem);
      }
    }

    /* Respect a stated preference for less motion: the toast still appears and leaves,
       it just stops sliding to do it. */
    @media (prefers-reduced-motion: reduce) {
      .notification-enter {
        animation: notification-fade-in 120ms ease-out;
      }

      .notification-leave {
        animation: notification-fade-out 120ms ease-in forwards;
      }

      @keyframes notification-fade-in {
        from { opacity: 0; }
      }

      @keyframes notification-fade-out {
        to { opacity: 0; }
      }
    }
  `
})
export class NotificationStackComponent {
  private readonly notificationService = inject(NotificationService);

  notifications = this.notificationService.notifications;

  dismiss(id: number) {
    this.notificationService.dismiss(id);
  }
}

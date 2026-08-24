import { Injectable, inject, signal } from '@angular/core';
import { Overlay, OverlayRef } from '@angular/cdk/overlay';
import { ComponentPortal } from '@angular/cdk/portal';
import { NotificationTone } from './notification.component';
// The stack injects this service in turn; the reference here is only read when a
// notification is actually shown, by which point both modules have finished loading.
import { NotificationStackComponent } from './notification-stack.component';

export interface NotificationConfig {
  title?: string;
  message: string;
  tone?: NotificationTone;
  /** Milliseconds before auto-dismiss. Pass 0 to require a manual dismiss. */
  durationMs?: number;
}

export interface Notification {
  id: number;
  title: string;
  message: string;
  tone: NotificationTone;
}

/**
 * Transient notifications, built on the CDK overlay.
 * <p>
 * These replace the confirmation modal, which was only ever used to deliver messages -
 * every call site disabled the cancel button, and one disabled confirm too. A blocking
 * dialog for "you forgot to enter a URL" made the user dismiss something they never asked
 * for; a toast says the same thing without taking over the page.
 * <p>
 * One overlay holds all of them, rather than one overlay each: stacking them by hand meant
 * assuming a fixed height, and anything that wrapped to two lines overlapped its neighbour.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly overlay = inject(Overlay);
  private readonly items = signal<Notification[]>([]);

  private overlayRef?: OverlayRef;
  private nextId = 0;

  /** Everything currently on screen, oldest first. Read by the stack component. */
  readonly notifications = this.items.asReadonly();

  show(config: NotificationConfig): void {
    this.ensureAttached();

    const id = this.nextId++;

    this.items.update(current => [...current, {
      id,
      title: config.title ?? '',
      message: config.message,
      tone: config.tone ?? 'info'
    }]);

    const duration = config.durationMs ?? 6000;
    if (duration > 0) {
      setTimeout(() => this.dismiss(id), duration);
    }
  }

  dismiss(id: number): void {
    this.items.update(current => current.filter(notification => notification.id !== id));

    // The overlay is torn down once empty rather than left attached, so it cannot sit over
    // the page swallowing anything once there is nothing to show.
    if (this.items().length === 0) {
      this.overlayRef?.dispose();
      this.overlayRef = undefined;
    }
  }

  private ensureAttached(): void {
    if (this.overlayRef) {
      return;
    }

    this.overlayRef = this.overlay.create({
      positionStrategy: this.overlay.position()
        .global()
        .bottom('1.5rem')
        .right('1.5rem'),
      scrollStrategy: this.overlay.scrollStrategies.noop(),
      // No backdrop: a notification must not block the page behind it.
      hasBackdrop: false
    });

    this.overlayRef.attach(new ComponentPortal(NotificationStackComponent));
  }
}

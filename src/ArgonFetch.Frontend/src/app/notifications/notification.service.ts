import { Injectable, inject } from '@angular/core';
import { Overlay, OverlayRef } from '@angular/cdk/overlay';
import { ComponentPortal } from '@angular/cdk/portal';
import { NotificationComponent, NotificationTone } from './notification.component';

export interface NotificationConfig {
  title?: string;
  message: string;
  tone?: NotificationTone;
  /** Milliseconds before auto-dismiss. Pass 0 to require a manual dismiss. */
  durationMs?: number;
}

/**
 * Transient notifications, built on the CDK overlay.
 * <p>
 * These replace the confirmation modal, which was only ever used to deliver messages -
 * every call site disabled the cancel button, and one disabled confirm too. A blocking
 * dialog for "you forgot to enter a URL" made the user dismiss something they never asked
 * for; a toast says the same thing without taking over the page.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly overlay = inject(Overlay);
  private readonly open: OverlayRef[] = [];

  show(config: NotificationConfig): void {
    const overlayRef = this.overlay.create({
      positionStrategy: this.overlay.position()
        .global()
        .bottom('1.5rem')
        .right('1.5rem'),
      scrollStrategy: this.overlay.scrollStrategies.noop(),
      // No backdrop: a notification must not block the page behind it.
      hasBackdrop: false
    });

    // Stack rather than overlap when several arrive at once.
    overlayRef.overlayElement.style.marginBottom = `${this.open.length * 4.5}rem`;
    this.open.push(overlayRef);

    const instance = overlayRef.attach(new ComponentPortal(NotificationComponent)).instance;
    instance.title = config.title ?? '';
    instance.message = config.message;
    instance.tone = config.tone ?? 'info';

    const dismiss = () => this.dismiss(overlayRef);
    instance.dismiss = dismiss;

    const duration = config.durationMs ?? 6000;
    if (duration > 0) {
      setTimeout(dismiss, duration);
    }
  }

  private dismiss(overlayRef: OverlayRef): void {
    const index = this.open.indexOf(overlayRef);
    if (index === -1) {
      // Already dismissed - the timer and the close button can both fire.
      return;
    }

    this.open.splice(index, 1);
    overlayRef.dispose();

    // Close the gap left behind so the remaining notifications stay stacked.
    this.open.forEach((ref, i) => {
      ref.overlayElement.style.marginBottom = `${i * 4.5}rem`;
    });
  }
}

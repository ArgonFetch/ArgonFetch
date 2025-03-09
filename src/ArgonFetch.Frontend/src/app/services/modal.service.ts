// modal.service.ts
import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';
import { filter, map, take } from 'rxjs/operators';

export interface ModalConfig {
  confirmationText: string;
  showCancelButton?: boolean;
  showConfirmButton?: boolean;
  title?: string;
}

export interface ModalState extends ModalConfig {
  isOpen: boolean;
  showConfirmButton?: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class ModalService {
  // Track the current state of the modal
  private modalState = new BehaviorSubject<ModalState>({
    isOpen: false,
    confirmationText: '',
    title: 'Confirmation'
  });

  // Result of the modal interaction (true = confirm, false = cancel)
  private modalResult = new BehaviorSubject<boolean | null>(null);

  // Expose the modal state as an observable
  public modalState$ = this.modalState.asObservable();

  /**
   * Open a confirmation modal with the provided configuration
   * @param config Modal configuration object
   * @returns Observable that resolves to true (confirm) or false (cancel)
   */
  open(config: ModalConfig): Observable<boolean> {
    // Reset the result
    this.modalResult.next(null);
    
    // Update the modal state to open with the provided config
    this.modalState.next({
      isOpen: true,
      confirmationText: config.confirmationText,
      showCancelButton: config.showCancelButton ?? true,
      showConfirmButton: config.showConfirmButton ?? true,
      title: config.title ?? 'Confirmation'
    });

    // Return an observable that will emit once when the modal is closed
    return this.modalResult.asObservable().pipe(
      // Filter out null values (we only want true/false)
      filter(result => result !== null),
      // Cast to boolean since we filtered out nulls
      map(result => result as boolean),
      // Complete the observable after the first emission
      take(1)
    );
  }

  /**
   * Confirm the modal dialog
   */
  confirm(): void {
    this.modalResult.next(true);
    this.close();
  }

  /**
   * Cancel the modal dialog
   */
  cancel(): void {
    this.modalResult.next(false);
    this.close();
  }

  /**
   * Close the modal without affecting the result
   */
  close(): void {
    this.modalState.next({
      ...this.modalState.value,
      isOpen: false
    });
  }
}
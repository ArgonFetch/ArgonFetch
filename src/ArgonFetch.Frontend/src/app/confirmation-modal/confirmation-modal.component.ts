// confirmation-modal.component.ts
import { Component, OnInit, OnDestroy } from '@angular/core';
import { NgIf } from '@angular/common';
import { Subscription } from 'rxjs';
import { ModalService } from '../services/modal.service';

@Component({
  selector: 'app-confirmation-modal',
  standalone: true,
  imports: [NgIf],
  templateUrl: './confirmation-modal.component.html',
  styleUrls: ['./confirmation-modal.component.scss']
})
export class ConfirmationModalComponent implements OnInit, OnDestroy {
  isOpen: boolean = false;
  confirmationText: string = '';
  showCancelButton: boolean = true;
  showConfirmButton: boolean = true;
  title: string = 'Confirmation';
  
  private subscription = new Subscription();

  constructor(private modalService: ModalService) {}

  ngOnInit(): void {
    this.subscription.add(
      this.modalService.modalState$.subscribe(state => {
        this.isOpen = state.isOpen;
        this.confirmationText = state.confirmationText;
        this.showCancelButton = state.showCancelButton ?? true;
        this.showConfirmButton = state.showConfirmButton ?? true;
        this.title = state.title ?? 'Confirmation';
      })
    );
  }

  onConfirmClick(): void {
    this.modalService.confirm();
  }

  onCancelClick(): void {
    this.modalService.cancel();
  }

  ngOnDestroy(): void {
    this.subscription.unsubscribe();
  }
}
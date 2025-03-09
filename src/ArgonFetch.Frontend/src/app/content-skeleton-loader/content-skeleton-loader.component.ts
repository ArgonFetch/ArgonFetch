import { NgFor, NgIf } from '@angular/common';
import { Component, Input, OnInit } from '@angular/core';

@Component({
  selector: 'app-content-skeleton-loader',
  standalone: true,
  imports: [NgIf, NgFor],
  templateUrl: './content-skeleton-loader.component.html',
  styleUrl: './content-skeleton-loader.component.scss'
})
export class ContentSkeletonLoaderComponent implements OnInit {
  @Input() type: 'single-song' | 'playlist' | 'unknown' = 'unknown';
  @Input() animate: boolean = true;
  
  ngOnInit() {
    // Ensure animation starts after component is rendered
    setTimeout(() => {
      this.animate = true;
    }, 10);
  }
}
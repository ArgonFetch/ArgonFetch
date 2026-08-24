
import { Component, Input, ChangeDetectionStrategy } from '@angular/core';

@Component({
  selector: 'app-content-skeleton-loader',
  standalone: true,
  imports: [],
  templateUrl: './content-skeleton-loader.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './content-skeleton-loader.component.scss'
})
export class ContentSkeletonLoaderComponent {
  @Input() type: 'single-song' | 'playlist' | 'unknown' = 'unknown';
  // Set by the caller, which always passes true. The timer that used to re-set it here
  // did nothing the binding had not already done, and would have stopped running silently
  // once the app went zoneless.
  @Input() animate: boolean = true;
}
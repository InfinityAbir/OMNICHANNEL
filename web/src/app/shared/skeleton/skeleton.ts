import { Component, input } from '@angular/core';

@Component({
  selector: 'app-skeleton',
  template: `<div class="skeleton" [style.width]="width()" [style.height]="height()" [style.border-radius]="radius()"></div>`,
  styles: [
    `
      .skeleton {
        background: linear-gradient(90deg, var(--gray-100) 25%, var(--gray-200) 37%, var(--gray-100) 63%);
        background-size: 400% 100%;
        animation: shimmer 1.4s ease infinite;
      }

      @keyframes shimmer {
        0% {
          background-position: 100% 50%;
        }
        100% {
          background-position: 0 50%;
        }
      }

      @media (prefers-reduced-motion: reduce) {
        .skeleton {
          animation: none;
          background: var(--gray-100);
        }
      }
    `,
  ],
})
export class SkeletonComponent {
  readonly width = input('100%');
  readonly height = input('1rem');
  readonly radius = input('var(--radius-sm)');
}

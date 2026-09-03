import { Component, input } from '@angular/core';

@Component({
  selector: 'app-empty-state',
  template: `
    <div class="empty" role="status">
      <p class="title">{{ title() }}</p>
      @if (message()) {
        <p class="message">{{ message() }}</p>
      }
    </div>
  `,
  styles: [
    `
      .empty {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        text-align: center;
        padding: 3rem 1.5rem;
        color: var(--text-muted);
      }

      .title {
        margin: 0 0 0.25rem;
        font-size: 0.95rem;
        font-weight: 600;
        color: var(--text);
      }

      .message {
        margin: 0;
        font-size: 0.85rem;
        max-width: 28rem;
      }
    `,
  ],
})
export class EmptyStateComponent {
  readonly title = input.required<string>();
  readonly message = input<string>('');
}

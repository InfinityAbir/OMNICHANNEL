import { Component, computed, input } from '@angular/core';

const LABELS: Record<string, string> = {
  Manual: 'Manual',
  WebsiteChat: 'Website chat',
  WhatsApp: 'WhatsApp',
  Instagram: 'Instagram',
  Messenger: 'Messenger',
  Telegram: 'Telegram',
  Email: 'Email',
};

/** Small monochrome per-channel glyph (currentColor) — a logo-only source indicator, not a text label, per the app's monochromatic design direction. Title/aria-label carry the channel name for accessibility. */
@Component({
  selector: 'app-channel-icon',
  template: `
    <span class="channel-icon" [class]="'ch-' + channelType()" [attr.title]="label()" [attr.aria-label]="label()">
      @switch (channelType()) {
        @case ('WhatsApp') {
          <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <path
              d="M12 2a10 10 0 0 0-8.6 15L2 22l5.2-1.4A10 10 0 1 0 12 2Zm0 18a8 8 0 0 1-4.1-1.1l-.3-.2-3 .8.8-2.9-.2-.3A8 8 0 1 1 12 20Zm4.4-5.9c-.2-.1-1.4-.7-1.7-.8-.2-.1-.4-.1-.6.1s-.6.8-.8 1c-.1.2-.3.2-.5.1-.2-.1-1-.4-2-1.2-.7-.6-1.2-1.4-1.4-1.6-.1-.2 0-.4.1-.5l.4-.4c.1-.1.2-.3.2-.4.1-.2 0-.3 0-.5s-.6-1.5-.9-2c-.2-.5-.4-.4-.6-.4h-.5c-.2 0-.5.1-.7.3-.2.2-.9.9-.9 2.2s1 2.6 1.1 2.8c.1.2 2 3 4.7 4.2.7.3 1.2.5 1.6.6.7.2 1.3.2 1.8.1.5-.1 1.4-.6 1.6-1.1.2-.5.2-1 .1-1.1-.1-.1-.2-.2-.4-.3Z"
            />
          </svg>
        }
        @case ('Instagram') {
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
            <rect x="3" y="3" width="18" height="18" rx="5" />
            <circle cx="12" cy="12" r="4" />
            <circle cx="17.2" cy="6.8" r="1" fill="currentColor" stroke="none" />
          </svg>
        }
        @case ('Messenger') {
          <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <path
              d="M12 2C6.5 2 2 6.1 2 11.2c0 2.9 1.5 5.5 3.8 7.2V22l3.5-1.9c.9.3 1.8.4 2.7.4 5.5 0 10-4.1 10-9.3S17.5 2 12 2Zm1 12.5-2.6-2.8-5 2.8 5.5-5.9L13.5 12l4.9-2.8L13 14.5Z"
            />
          </svg>
        }
        @case ('WebsiteChat') {
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
            <path d="M4 5h16v11H8l-4 4V5Z" />
          </svg>
        }
        @case ('Telegram') {
          <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <path
              d="M21.5 3.5 2.7 10.9c-1.1.4-1.1 1.6.1 1.9l4.7 1.5 1.8 5.6c.3.8 1.3 1 1.9.4l2.6-2.5 4.8 3.6c.9.6 2 .1 2.3-1L23.9 4.9c.3-1.3-.9-2.1-2.4-1.4ZM8.2 14l9.6-6.7c.4-.3.8.2.4.5l-8.1 7.5-.3 3.2-1.6-4.5Z"
            />
          </svg>
        }
        @case ('Email') {
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
            <rect x="3" y="5" width="18" height="14" rx="2" />
            <path d="m4 6.5 8 6.5 8-6.5" />
          </svg>
        }
        @default {
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
            <circle cx="12" cy="8" r="3.2" />
            <path d="M5 20c1.2-3.5 4-5.5 7-5.5s5.8 2 7 5.5" />
          </svg>
        }
      }
    </span>
  `,
  styles: [
    `
      .channel-icon {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 1.1rem;
        height: 1.1rem;
        color: var(--text-muted);
      }

      svg {
        width: 100%;
        height: 100%;
      }
    `,
  ],
})
export class ChannelIconComponent {
  readonly channelType = input.required<string>();
  readonly label = computed(() => LABELS[this.channelType()] ?? this.channelType());
}

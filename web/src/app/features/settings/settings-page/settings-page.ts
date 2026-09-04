import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

interface SettingsNavItem {
  path: string;
  label: string;
  /** Any permission in this list grants visibility — dynamic from the current user's own JWT
   * permissions (CurrentUserResponse.permissions), never a hardcoded role-name check. A role
   * gains or loses a nav item purely by what permissions the backend actually issued it. */
  anyPermission: string[];
}

const NAV_ITEMS: SettingsNavItem[] = [
  { path: 'channels', label: 'Channels', anyPermission: ['channels.read', 'channels.manage'] },
  { path: 'knowledge', label: 'Knowledge Base', anyPermission: ['knowledge.read', 'knowledge.manage'] },
  { path: 'ai', label: 'AI Auto-Reply', anyPermission: ['ai.read', 'ai.configure'] },
  { path: 'ai-provider', label: 'AI Provider', anyPermission: ['ai.read', 'ai.configure'] },
  { path: 'automation', label: 'Automation Rules', anyPermission: ['tenant.read', 'tenant.update'] },
  { path: 'business-hours', label: 'Business Hours', anyPermission: ['tenant.read', 'tenant.update'] },
  { path: 'saved-replies', label: 'Saved Replies', anyPermission: ['conversations.read', 'conversations.reply'] },
  { path: 'email', label: 'Email (SMTP)', anyPermission: ['tenant.read', 'tenant.update'] },
  { path: 'analytics', label: 'Analytics', anyPermission: ['analytics.read'] },
];

@Component({
  selector: 'app-settings-page',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './settings-page.html',
  styleUrl: './settings-page.scss',
})
export class SettingsPageComponent {
  private readonly auth = inject(AuthService);

  readonly items = computed(() => {
    const permissions = this.auth.currentUser()?.permissions ?? [];
    return NAV_ITEMS.filter((item) => item.anyPermission.some((p) => permissions.includes(p)));
  });
}

import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AutomationService } from '../../../core/services/automation.service';
import { ToastService } from '../../../core/services/toast.service';
import { SavedReplyResponse } from '../../../core/models/automation.models';
import { SkeletonComponent } from '../../../shared/skeleton/skeleton';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state';

@Component({
  selector: 'app-saved-replies-settings',
  standalone: true,
  imports: [FormsModule, SkeletonComponent, EmptyStateComponent],
  templateUrl: './saved-replies-settings.html',
  styleUrls: ['../../../shared/settings-common.scss'],
})
export class SavedRepliesSettingsComponent implements OnInit {
  private readonly automation = inject(AutomationService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly replies = signal<SavedReplyResponse[]>([]);

  readonly showForm = signal(false);
  readonly editingId = signal<string | null>(null);
  readonly titleDraft = signal('');
  readonly textDraft = signal('');
  readonly saving = signal(false);

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.automation.listSavedReplies().subscribe({
      next: (replies) => {
        this.replies.set(replies);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.showError(err, 'Could not load saved replies.');
      },
    });
  }

  openCreate(): void {
    this.editingId.set(null);
    this.titleDraft.set('');
    this.textDraft.set('');
    this.showForm.set(true);
  }

  openEdit(reply: SavedReplyResponse): void {
    this.editingId.set(reply.id);
    this.titleDraft.set(reply.title);
    this.textDraft.set(reply.text);
    this.showForm.set(true);
  }

  closeForm(): void {
    this.showForm.set(false);
  }

  save(): void {
    const title = this.titleDraft().trim();
    const text = this.textDraft().trim();
    if (!title || !text) return;

    this.saving.set(true);
    const editingId = this.editingId();
    const request$ = editingId
      ? this.automation.updateSavedReply(editingId, { title, text })
      : this.automation.createSavedReply({ title, text });

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.showForm.set(false);
        this.toast.show(editingId ? 'Saved reply updated.' : 'Saved reply created.', 'success');
        this.load();
      },
      error: (err) => {
        this.saving.set(false);
        this.toast.showError(err, 'Could not save.');
      },
    });
  }

  delete(reply: SavedReplyResponse): void {
    this.automation.deleteSavedReply(reply.id).subscribe({
      next: () => {
        this.toast.show('Saved reply deleted.', 'success');
        this.load();
      },
      error: (err) => this.toast.showError(err, 'Could not delete the saved reply.'),
    });
  }
}

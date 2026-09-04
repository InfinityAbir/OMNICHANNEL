import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { KnowledgeService } from '../../../core/services/knowledge.service';
import { ToastService } from '../../../core/services/toast.service';
import { KnowledgeDocumentResponse, KnowledgeSearchResultResponse } from '../../../core/models/knowledge.models';
import { SkeletonComponent } from '../../../shared/skeleton/skeleton';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state';

@Component({
  selector: 'app-knowledge-settings',
  standalone: true,
  imports: [FormsModule, DatePipe, SkeletonComponent, EmptyStateComponent],
  templateUrl: './knowledge-settings.html',
  styleUrls: ['../../../shared/settings-common.scss', './knowledge-settings.scss'],
})
export class KnowledgeSettingsComponent implements OnInit {
  private readonly knowledge = inject(KnowledgeService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly documents = signal<KnowledgeDocumentResponse[]>([]);

  readonly showForm = signal(false);
  readonly editingId = signal<string | null>(null);
  readonly titleDraft = signal('');
  readonly contentDraft = signal('');
  readonly saving = signal(false);

  readonly searchQuery = signal('');
  readonly searchResults = signal<KnowledgeSearchResultResponse[] | null>(null);
  readonly searching = signal(false);

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.knowledge.list().subscribe({
      next: (docs) => {
        this.documents.set(docs);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.showError(err, 'Could not load knowledge documents.');
      },
    });
  }

  openCreate(): void {
    this.editingId.set(null);
    this.titleDraft.set('');
    this.contentDraft.set('');
    this.showForm.set(true);
  }

  openEdit(doc: KnowledgeDocumentResponse): void {
    this.editingId.set(doc.id);
    this.titleDraft.set(doc.title);
    this.contentDraft.set(''); // content isn't returned by the list endpoint — revise requires re-typing it
    this.showForm.set(true);
  }

  closeForm(): void {
    this.showForm.set(false);
  }

  save(): void {
    const title = this.titleDraft().trim();
    const content = this.contentDraft().trim();
    if (!title || !content) return;

    this.saving.set(true);
    const editingId = this.editingId();
    const request$ = editingId ? this.knowledge.revise(editingId, title, content) : this.knowledge.create(title, content);

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.showForm.set(false);
        this.toast.show(editingId ? 'Document updated.' : 'Document created.', 'success');
        this.load();
      },
      error: (err) => {
        this.saving.set(false);
        this.toast.showError(err, 'Could not save the document.');
      },
    });
  }

  archive(doc: KnowledgeDocumentResponse): void {
    this.knowledge.archive(doc.id).subscribe({
      next: () => {
        this.toast.show('Document archived.', 'success');
        this.load();
      },
      error: (err) => this.toast.showError(err, 'Could not archive the document.'),
    });
  }

  runSearch(): void {
    const query = this.searchQuery().trim();
    if (!query) return;

    this.searching.set(true);
    this.knowledge.search(query).subscribe({
      next: (results) => {
        this.searchResults.set(results);
        this.searching.set(false);
      },
      error: (err) => {
        this.searching.set(false);
        this.toast.showError(err, 'Search failed.');
      },
    });
  }
}

import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api, DocumentInfo, errorText } from './api';
import { Explain } from './explain';

@Component({
  selector: 'app-documents',
  imports: [FormsModule, DatePipe, DecimalPipe, RouterLink, Explain],
  template: `
    <h2>Documents</h2>
    <app-explain>
      <p>Files are split across <b>two stores</b> by <code>documents-api</code> (<code>src/documents-api/Program.cs</code>):</p>
      <ol>
        <li>the <b>bytes</b> go to <b>MinIO</b> (S3-compatible object storage), bucket <code>documents</code>,
            under the <b>object key</b> shown below. Browse them in the <a [href]="minio" target="_blank">MinIO console (:9001)</a>.</li>
        <li>a small <b>row</b> pointing to that key goes to SQL Server, <code>DocumentsDb.dbo.Documents</code>.</li>
      </ol>
      <p>Databases are bad at storing big files; object storage is cheap and fast for them. But the two stores must stay
         <b>in sync</b>: delete a file in the MinIO console, then try to download it here and see what happens.</p>
      <p>The S3 code is in <code>FileStorage.cs</code>. It uses the Amazon S3 SDK, so it would work against AWS S3 too.</p>
    </app-explain>

    <form class="row" (ngSubmit)="upload()">
      <label>File <input type="file" (change)="pick($event)" /></label>
      <label>Order id (optional) <input name="orderId" type="number" [(ngModel)]="orderId" class="narrow" /></label>
      <button type="submit" [disabled]="!file()">Upload</button>
    </form>
    @if (error()) {
      <p class="error">{{ error() }}</p>
    }

    <table>
      <thead>
        <tr><th>Id</th><th>File</th><th>Order</th><th>Size</th><th>Object key in MinIO</th><th>Uploaded</th><th></th></tr>
      </thead>
      <tbody>
        @for (d of documents(); track d.id) {
          <tr>
            <td>{{ d.id }}</td>
            <td><a [href]="api.downloadUrl(d.id)">{{ d.fileName }}</a></td>
            <td>
              @if (d.orderId) { <a [routerLink]="['/orders', d.orderId]">{{ d.orderId }}</a> } @else { - }
            </td>
            <td>{{ d.sizeBytes / 1024 | number: '1.0-1' }} KB</td>
            <td><code>{{ d.bucket }}/{{ d.objectKey }}</code></td>
            <td>{{ d.uploadedAt | date: 'short' }}</td>
            <td><button class="link" (click)="remove(d.id)">Delete</button></td>
          </tr>
        } @empty {
          <tr><td colspan="7" class="hint">No documents yet. Upload any file above.</td></tr>
        }
      </tbody>
    </table>
  `,
})
export class DocumentsPage {
  protected readonly api = inject(Api);

  protected readonly minio = `http://${location.hostname}:9001`;
  protected readonly documents = signal<DocumentInfo[]>([]);
  protected readonly error = signal('');
  protected readonly file = signal<File | null>(null);
  protected readonly orderId = signal<number | null>(null);

  constructor() {
    this.load();
  }

  private load() {
    this.api.documents().subscribe({
      next: (docs) => this.documents.set(docs),
      error: (e) => this.error.set(errorText(e)),
    });
  }

  protected pick(event: Event) {
    this.file.set((event.target as HTMLInputElement).files?.[0] ?? null);
  }

  protected upload() {
    const file = this.file();
    if (!file) return;
    this.error.set('');
    this.api.uploadDocument(file, this.orderId()).subscribe({
      next: () => this.load(),
      error: (e) => this.error.set(errorText(e)),
    });
  }

  protected remove(id: number) {
    this.api.deleteDocument(id).subscribe({
      next: () => this.load(),
      error: (e) => this.error.set(errorText(e)),
    });
  }
}

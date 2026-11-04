import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import type { IFilterAngularComp } from 'ag-grid-angular';
import type { IDoesFilterPassParams, IFilterParams } from 'ag-grid-community';
import type { Observable, Subscription } from 'rxjs';
import type { GridValuesPage } from './grid-request.types';
import type { SetFilterModel } from './to-grid-request';

/** What the server grid puts on `gridOptions.context` for this filter. */
export interface SetFilterContext {
  loadValues(field: string): Observable<GridValuesPage>;
}

interface SetFilterParams extends IFilterParams {
  kind: 'set' | 'boolean';
  field: string;
}

const BLANK = '(blank)';

/**
 * Checkbox-list filter for `set` and `boolean` columns. Set values come from the server
 * (`/grid/values`, scoped like the rows); boolean columns list true / false / (blank).
 * A failed list says so and offers "Try again" - an empty list never reads as "no values".
 */
@Component({
  selector: 'gk-set-filter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="gk-set-filter">
      @if (kind() === 'set') {
        <input
          type="search"
          class="gk-set-filter__search"
          placeholder="Find in list"
          aria-label="Find in list"
          [value]="find()"
          (input)="find.set($any($event.target).value)"
        />
      }
      @if (loading()) {
        <p class="gk-set-filter__note" role="status">Loading values...</p>
      } @else if (failed()) {
        <div class="gk-set-filter__error" role="alert">
          <p>The values could not be loaded.</p>
          <button type="button" (click)="load()">Try again</button>
        </div>
      } @else {
        <label class="gk-set-filter__item gk-set-filter__all">
          <input
            type="checkbox"
            [checked]="allSelected()"
            (change)="toggleAll($any($event.target).checked)"
          />
          (Select all)
        </label>
        <ul class="gk-set-filter__list" role="list">
          @for (option of visible(); track option) {
            <li>
              <label class="gk-set-filter__item">
                <input
                  type="checkbox"
                  [checked]="selected().has(option)"
                  (change)="toggle(option)"
                />
                {{ display(option) }}
              </label>
            </li>
          } @empty {
            <li class="gk-set-filter__note">No values</li>
          }
        </ul>
        @if (truncated()) {
          <p class="gk-set-filter__note" aria-live="polite">
            Lists only the first {{ options().length }} values. Narrow the grid with other filters
            to see the rest.
          </p>
        }
      }
    </div>
  `,
  styles: `
    .gk-set-filter {
      display: grid;
      gap: 0.5rem;
      padding: 0.75rem;
      min-width: 14rem;
      max-width: 20rem;
    }
    .gk-set-filter__search {
      padding: 0.375rem 0.5rem;
      font: inherit;
    }
    .gk-set-filter__list {
      list-style: none;
      margin: 0;
      padding: 0;
      max-height: 14rem;
      overflow-y: auto;
    }
    .gk-set-filter__item {
      display: flex;
      gap: 0.5rem;
      align-items: center;
      min-height: 1.75rem;
      cursor: pointer;
    }
    .gk-set-filter__all {
      font-weight: 600;
    }
    .gk-set-filter__note {
      margin: 0;
      font-size: 0.875em;
      opacity: 0.8;
    }
    .gk-set-filter__error p {
      margin: 0 0 0.5rem;
    }
  `,
})
export class SetFilterComponent implements IFilterAngularComp {
  protected readonly kind = signal<'set' | 'boolean'>('set');
  protected readonly options = signal<string[]>([]);
  protected readonly selected = signal<Set<string>>(new Set());
  protected readonly find = signal('');
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly truncated = signal(false);
  /** `null` = no filter (everything passes). */
  private model: SetFilterModel | null = null;
  private params!: SetFilterParams;
  private subscription?: Subscription;

  protected readonly visible = computed(() => {
    const term = this.find().trim().toLowerCase();
    return term ? this.options().filter((o) => o.toLowerCase().includes(term)) : this.options();
  });
  protected readonly allSelected = computed(
    () => this.options().length > 0 && this.options().every((o) => this.selected().has(o)),
  );

  agInit(params: IFilterParams): void {
    this.params = params as SetFilterParams;
    this.kind.set(this.params.kind);
    if (this.params.kind === 'boolean') {
      this.options.set(['true', 'false', BLANK]);
      this.syncSelection();
    }
  }

  afterGuiAttached(): void {
    if (this.kind() === 'set') this.load();
  }

  load(): void {
    const context = this.params.context as SetFilterContext | undefined;
    if (!context) return;
    this.subscription?.unsubscribe();
    this.loading.set(true);
    this.failed.set(false);
    this.subscription = context.loadValues(this.params.field).subscribe({
      next: (page) => {
        this.loading.set(false);
        this.truncated.set(page.truncated);
        // Keep selected values visible even if the current scope no longer lists them.
        const kept = this.model ? this.model.values.filter((v) => !page.values.includes(v)) : [];
        this.options.set([...page.values, ...kept]);
        this.syncSelection();
      },
      error: () => {
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  isFilterActive(): boolean {
    return this.model !== null;
  }

  /** Filtering happens on the server; the grid never filters rows in the browser. */
  doesFilterPass(_params: IDoesFilterPassParams): boolean {
    return true;
  }

  getModel(): SetFilterModel | null {
    return this.model;
  }

  setModel(model: SetFilterModel | null): void {
    this.model = model;
    this.syncSelection();
  }

  destroy(): void {
    this.subscription?.unsubscribe();
  }

  protected display(option: string): string {
    if (this.kind() !== 'boolean') return option;
    return option === 'true' ? 'Yes' : option === 'false' ? 'No' : BLANK;
  }

  protected toggle(option: string): void {
    const next = new Set(this.selected());
    if (next.has(option)) next.delete(option);
    else next.add(option);
    this.apply(next);
  }

  protected toggleAll(checked: boolean): void {
    this.apply(checked ? new Set(this.options()) : new Set());
  }

  private apply(selection: Set<string>): void {
    this.selected.set(selection);
    const everything = this.options().every((o) => selection.has(o)) && !this.truncated();
    if (everything) {
      this.model = null;
    } else if (this.kind() === 'boolean') {
      this.model = {
        filterType: 'set',
        values: [...selection].filter((v) => v !== BLANK),
        includeBlank: selection.has(BLANK),
      };
    } else {
      this.model = { filterType: 'set', values: [...selection] };
    }
    this.params.filterChangedCallback();
  }

  private syncSelection(): void {
    if (!this.model) {
      this.selected.set(new Set(this.options()));
      return;
    }
    const values = new Set(this.model.values);
    if (this.model.includeBlank) values.add(BLANK);
    this.selected.set(values);
  }
}

import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { AgGridAngular } from 'ag-grid-angular';
import {
  colorSchemeVariable,
  type GridApi,
  type GridReadyEvent,
  type IDatasource,
  type IGetRowsParams,
  type SortModelItem,
  themeQuartz,
} from 'ag-grid-community';
import { debounce, distinctUntilChanged, of, type Subscription, timer } from 'rxjs';
import { type GridColumns, SET_FILTER_NAME, toColDefs } from './grid-columns';
import type { GridDataSource } from './grid-data-source';
import { GridRequestError, gridErrorMessage } from './grid-errors';
import { GRID_LIMITS, type GridCapabilities } from './grid-request.types';
import { SetFilterComponent, type SetFilterContext } from './set-filter.component';

/** Quick-search debounce, the same as the column filters. */
export const SEARCH_DEBOUNCE_MS = 400;

interface ActiveRequest {
  key: string;
  subscription: Subscription;
  fail: () => void;
}

/**
 * Server-side grid: AG Grid's infinite row model over a GridDataSource.
 * Paging, multi-sort (max 3), typed filters and quick search all run on the server.
 * A failure is an inline alert with "Try again", never an empty grid.
 */
@Component({
  selector: 'gk-server-grid',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AgGridAngular],
  templateUrl: './server-grid.component.html',
  styleUrl: './server-grid.component.scss',
})
export class ServerGridComponent<TRow> {
  readonly columns = input.required<GridColumns<TRow>>();
  readonly dataSource = input.required<GridDataSource<TRow>>();
  readonly searchText = input('');
  /** Change it (e.g. a new caller or scope) to reload from page 1 and recount. */
  readonly refreshKey = input<unknown>(undefined);
  readonly clearSearchRequested = output<void>();

  protected readonly theme = themeQuartz.withPart(colorSchemeVariable);
  protected readonly pageSize = GRID_LIMITS.defaultPageSize;
  protected readonly components = { [SET_FILTER_NAME]: SetFilterComponent };
  protected readonly timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

  protected readonly error = signal<GridRequestError | null>(null);
  protected readonly capabilities = signal<GridCapabilities>({ textFilters: 'enabled' });
  protected readonly total = signal<number | null>(null);
  protected readonly loading = signal(false);
  protected readonly sortHint = signal('');

  protected readonly guarded = computed(() => this.capabilities().textFilters === 'disabled');
  protected readonly colDefs = computed(() =>
    toColDefs(this.columns(), { guarded: this.guarded(), timeZone: this.timeZone }),
  );
  protected readonly errorMessage = computed(() => {
    const error = this.error();
    return error ? gridErrorMessage(error, (field) => this.headerOf(field)) : '';
  });
  protected readonly indexedColumns = computed(() =>
    this.columns()
      .columns.filter((c) => c.indexed && c.filterable)
      .map((c) => c.headerName),
  );
  protected readonly dateHints = computed(() =>
    this.columns()
      .columns.filter((c) => c.type === 'date' && c.filterable)
      .map(
        (c) =>
          `${c.headerName}: ${c.dateSemantics === 'utc' ? 'UTC days' : c.dateSemantics === 'local' ? `your time zone (${this.timeZone})` : 'calendar days'}`,
      ),
  );

  /** Search applied to the rows: debounced while typing, cleared at once. */
  private readonly activeSearch = toSignal(
    toObservable(this.searchText).pipe(
      debounce((text) => (text.trim() ? timer(SEARCH_DEBOUNCE_MS) : of(0))),
      distinctUntilChanged(),
    ),
    { initialValue: '' },
  );

  private api?: GridApi<TRow>;
  private active: ActiveRequest[] = [];
  private currentKey = '';

  protected readonly context: SetFilterContext = {
    loadValues: (field) =>
      this.dataSource().fetchValues(
        field,
        this.api?.getFilterModel() ?? {},
        this.activeSearch(),
        this.timeZone,
      ),
  };

  constructor() {
    // A new search reloads from page 1 and recounts.
    effect(() => {
      this.activeSearch();
      this.refreshKey();
      if (this.api) this.reload();
    });
    // A column that is no longer declared (e.g. a restricted one) must not keep a filter or sort.
    effect(() => {
      const columns = this.columns();
      const api = this.api;
      if (!api) return;
      const model = api.getFilterModel();
      const stale = Object.keys(model).filter((field) => !columns.byField.has(field));
      if (stale.length) {
        for (const field of stale) delete model[field];
        api.setFilterModel(model);
      }
    });
    inject(DestroyRef).onDestroy(() => this.cancel(() => true));
  }

  protected onGridReady(event: GridReadyEvent<TRow>): void {
    this.api = event.api;
    const defaultSort = this.columns().defaultSort;
    if (defaultSort.length) {
      event.api.applyColumnState({
        state: defaultSort.map((s, i) => ({ colId: s.field, sort: s.dir, sortIndex: i })),
        defaultState: { sort: null },
      });
    }
    event.api.setGridOption('datasource', this.datasource);
  }

  /** Keeps at most three sorted columns; the newest extra sort is undone with a visible hint. */
  protected onSortChanged(): void {
    if (!this.api) return;
    const sorted = this.api
      .getColumnState()
      .filter((s) => s.sort)
      .sort((a, b) => (a.sortIndex ?? 0) - (b.sortIndex ?? 0));
    if (sorted.length > GRID_LIMITS.maxSorts) {
      const extra = sorted.slice(GRID_LIMITS.maxSorts).map((s) => ({ colId: s.colId, sort: null }));
      this.api.applyColumnState({ state: extra });
      this.sortHint.set(
        `Up to ${GRID_LIMITS.maxSorts} columns can be sorted at once. Remove a sort to add another.`,
      );
    } else if (sorted.length < GRID_LIMITS.maxSorts) {
      this.sortHint.set('');
    }
  }

  protected retry(): void {
    this.error.set(null);
    this.reload();
  }

  /** After `grid-filter-too-broad`: clears exactly the refused filters (and the search). */
  protected clearRefused(): void {
    const error = this.error();
    if (!error || !this.api) return;
    for (const field of error.details.fields) {
      if (field === 'search') this.clearSearchRequested.emit();
      else void this.api.setColumnFilterModel(field, null);
    }
    this.error.set(null);
    this.api.onFilterChanged();
  }

  private readonly datasource: IDatasource = {
    getRows: (params) => this.getRows(params),
  };

  private getRows(params: IGetRowsParams): void {
    const sortModel = params.sortModel.slice(0, GRID_LIMITS.maxSorts) as SortModelItem[];
    const search = this.activeSearch();
    const key = JSON.stringify({ sortModel, filterModel: params.filterModel, search });
    if (key !== this.currentKey) {
      // Superseded: abort the older requests (unsubscribing aborts the HTTP call) and release
      // their blocks with fail() so AG Grid does not keep them loading.
      this.cancel((r) => r.key !== key);
      this.currentKey = key;
      this.total.set(null);
    }
    const includeTotal = params.startRow === 0 || this.total() === null;
    this.loading.set(true);

    const request: ActiveRequest = {
      key,
      subscription: undefined as unknown as Subscription,
      fail: params.failCallback,
    };
    request.subscription = this.dataSource()
      .fetchPage({
        startRow: params.startRow,
        pageSize: this.pageSize,
        sortModel,
        filterModel: params.filterModel,
        search,
        includeTotal,
        timeZone: this.timeZone,
      })
      .subscribe({
        next: (page) => {
          this.done(request);
          this.error.set(null);
          this.capabilities.set(page.capabilities);
          // null = not computed: keep the cached total, never show 0.
          if (page.totalCount !== null) this.total.set(page.totalCount);
          const total = this.total();
          const lastRow =
            total ?? (page.rows.length < this.pageSize ? params.startRow + page.rows.length : -1);
          params.successCallback(page.rows, lastRow);
        },
        error: (err: unknown) => {
          this.done(request);
          this.error.set(err instanceof GridRequestError ? err : new GridRequestError('http'));
          params.failCallback();
        },
      });
    this.active.push(request);
  }

  private done(request: ActiveRequest): void {
    this.active = this.active.filter((r) => r !== request);
    this.loading.set(this.active.length > 0);
  }

  private cancel(predicate: (r: ActiveRequest) => boolean): void {
    const cancelled = this.active.filter(predicate);
    this.active = this.active.filter((r) => !predicate(r));
    for (const r of cancelled) {
      r.subscription?.unsubscribe();
      r.fail();
    }
  }

  private reload(): void {
    this.cancel(() => true);
    this.currentKey = '';
    this.api?.purgeInfiniteCache();
  }

  private headerOf(field: string): string {
    return this.columns().byField.get(field)?.headerName ?? field;
  }
}

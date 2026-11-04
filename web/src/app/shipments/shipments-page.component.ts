import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DemoSession } from '../demo/demo-session';
import { createGridDataSource } from '../grid/grid-data-source';
import { ServerGridComponent } from '../grid/server-grid.component';
import { shipmentsColumns } from './shipments.grid';
import { ShipmentRowSchema } from './shipment-row.schema';

@Component({
  selector: 'gk-shipments-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ServerGridComponent],
  templateUrl: './shipments-page.component.html',
  styleUrl: './shipments-page.component.scss',
})
export class ShipmentsPageComponent {
  protected readonly session = inject(DemoSession);
  protected readonly search = signal('');
  protected readonly columns = computed(() => shipmentsColumns(this.session.finance()));
  /** A new caller reloads the grid from page 1, like a fresh sign-in would. */
  protected readonly sessionKey = computed(
    () => `${this.session.tenant()}-${this.session.finance()}`,
  );

  protected readonly grid = createGridDataSource({
    url: '/api/shipments/grid',
    rowSchema: ShipmentRowSchema,
    columns: shipmentsColumns(true),
  });

  protected setTenant(value: string): void {
    this.session.tenant.set(Number(value) as 1 | 2 | 3);
  }
}

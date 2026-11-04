import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ShipmentsPageComponent } from './shipments/shipments-page.component';

@Component({
  selector: 'gk-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ShipmentsPageComponent],
  template: `<main><gk-shipments-page /></main>`,
})
export class App {}

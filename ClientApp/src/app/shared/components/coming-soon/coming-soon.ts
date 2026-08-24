import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-coming-soon',
  template: `<h2>{{ label }}</h2><p>This area is built out in a later Phase 7 sub-phase.</p>`,
})
export class ComingSoon {
  @Input() label = 'Coming soon';
}

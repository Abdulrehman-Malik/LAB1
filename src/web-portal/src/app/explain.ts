import { Component, input } from '@angular/core';

/**
 * A collapsible "How this page works" box. Put the explanation between the tags:
 *   <app-explain> ...what happens, which services, which files... </app-explain>
 */
@Component({
  selector: 'app-explain',
  template: `
    <details class="explain" [open]="open()">
      <summary>{{ title() }}</summary>
      <div class="explain-body"><ng-content /></div>
    </details>
  `,
})
export class Explain {
  readonly title = input('How this page works');
  readonly open = input(false);
}

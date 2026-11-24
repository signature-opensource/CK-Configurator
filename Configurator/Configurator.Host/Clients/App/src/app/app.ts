import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { CKGenAppModule } from '@local/ck-gen/CK/Angular/CKGenAppModule';
import { CatalogSource, CatalogSourceSelector, DesktopLogLinks } from '@local/ck-gen';

/**
 * The shell of the configurator: its header holds the two pages (the editor, the catalog), the opened catalog (its
 * "Edit" button opens the picker in a dialog) and the links of the desktop (the logs); the page is below.
 */
@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, CKGenAppModule, CatalogSourceSelector, DesktopLogLinks],
  selector: 'app-root',
  styleUrl: './app.less',
  templateUrl: './app.html',
})
export class App {
  constructor() {
    // The shell shows the catalog (and its picker): the pages don't show their own.
    inject( CatalogSource ).pickerInShell.set( true );
  }
}

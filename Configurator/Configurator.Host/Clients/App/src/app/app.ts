import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { CKGenAppModule } from '@local/ck-gen/CK/Angular/CKGenAppModule';
import { CatalogSource, CatalogSourcePicker, DesktopLogLinks } from '@local/ck-gen';

/**
 * The shell of the configurator: its header holds the picker of the opened catalog, the two pages (the catalog, the
 * editor) and the links of the desktop (the logs); the page is below.
 */
@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, CKGenAppModule, CatalogSourcePicker, DesktopLogLinks],
  selector: 'app-root',
  styleUrl: './app.less',
  templateUrl: './app.html',
})
export class App {
  constructor() {
    // The shell shows the picker: the catalog page doesn't show its own.
    inject( CatalogSource ).pickerInShell.set( true );
  }
}

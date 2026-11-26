import { Component, inject, signal, Type } from '@angular/core';
import { NgComponentOutlet } from '@angular/common';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { CKGenAppModule } from '@local/ck-gen/CK/Angular/CKGenAppModule';
import { resolveNamedComponentTypeAsync } from '@local/ck-gen/CK/Angular/NamedComponentsResolver';
import { CatalogSource, CatalogSourceSelector } from '@local/ck-gen';

/**
 * The shell of the configurator: its header holds the two pages (the editor, the catalog), the opened catalog (its
 * "Edit" button opens the picker in a dialog) and, in the desktop application, the links of the desktop (the logs); the
 * page is below.
 * The links of the desktop are the named component "desktop-log-links" of CK.Ng.AppHost.DesktopNgApp: the deployment
 * that packages the configurator as a desktop application adds the package, in Development (a browser) it is absent.
 */
@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, NgComponentOutlet, CKGenAppModule, CatalogSourceSelector],
  selector: 'app-root',
  styleUrl: './app.less',
  templateUrl: './app.html',
})
export class App {
  readonly desktopLinks = signal<Type<unknown> | undefined>( undefined );

  constructor() {
    // The shell shows the catalog (and its picker): the pages don't show their own.
    inject( CatalogSource ).pickerInShell.set( true );
    resolveNamedComponentTypeAsync( 'desktop-log-links' )?.then( t => this.desktopLinks.set( t ) );
  }
}

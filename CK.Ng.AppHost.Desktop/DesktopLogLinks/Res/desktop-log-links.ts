import { Component, inject, OnInit, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { GetDesktopInfoQCommand, HttpCrisEndpoint, OpenLastLogCommand, OpenLogFolderCommand } from '@local/ck-gen';

/**
 * The "Last log" and "Open folder" links of a desktop application: nothing is shown when the application has no window
 * or no text log (a server, a deployment without the window).
 */
@Component( {
    selector: 'ck-desktop-log-links',
    imports: [TranslatePipe],
    templateUrl: './desktop-log-links.html'
} )
export class DesktopLogLinks implements OnInit {
    // <PreDependencyInjection revert />
    readonly #cris = inject( HttpCrisEndpoint );
    // <PostDependencyInjection />

    // <PreLocalVariables revert />
    /** Whether the links are shown. */
    readonly visible = signal( false );
    /** The last error (shown as the title of the links). */
    readonly error = signal<string | null>( null );
    // <PostLocalVariables />

    async ngOnInit(): Promise<void> {
        try {
            const info = await this.#cris.sendOrThrowAsync( new GetDesktopInfoQCommand() );
            this.visible.set( !!info?.hasWindow && info.hasTextLog );
        } catch {
            // Not a desktop application (or its backend doesn't handle the command): nothing is shown.
            this.visible.set( false );
        }
    }

    async openLastLogAsync(): Promise<void> {
        await this.#sendAsync( new OpenLastLogCommand() );
    }

    async openFolderAsync(): Promise<void> {
        await this.#sendAsync( new OpenLogFolderCommand() );
    }

    async #sendAsync( c: OpenLastLogCommand | OpenLogFolderCommand ): Promise<void> {
        this.error.set( null );
        try {
            await this.#cris.sendOrThrowAsync( c );
        } catch( e ) {
            this.error.set( String( e ) );
        }
    }
}

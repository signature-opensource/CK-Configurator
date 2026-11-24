import { Component, computed, effect, inject, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { CatalogSource, CatalogSourcePicker } from '@local/ck-gen';

/**
 * The opened catalog of the {@link CatalogSource} on one line: "[Stack url - World - profile]" (or "<no catalog>"), and
 * an "Edit" button that opens the picker in a dialog. The dialog closes once a catalog is opened.
 */
@Component( {
    selector: 'ck-catalog-source-selector',
    imports: [
        TranslatePipe,
        NzButtonModule,
        NzModalModule,
        CatalogSourcePicker
    ],
    templateUrl: './catalog-source-selector.html'
} )
export class CatalogSourceSelector {
    // <PreDependencyInjection revert />
    readonly source = inject( CatalogSource );
    // <PostDependencyInjection />

    /** Whether the dialog is opened. */
    readonly editing = signal( false );

    /** The Stack url of the opened World (without its "/tree/main/@{LTS}" or its "@{LTS}" folder). */
    readonly stackUrl = computed( () => {
        const o = this.source.opened();
        return o ? o.world.replace( /([\/]tree[\/]main)?[\/]@[^\/]+$/, '' ) : undefined;
    } );

    constructor() {
        // A catalog has been opened (its info is set last): the dialog closes.
        let opening = false;
        effect( () => {
            const isOpening = this.source.opening();
            if( opening && !isOpening && this.source.info() ) this.editing.set( false );
            opening = isOpening;
        } );
    }
}

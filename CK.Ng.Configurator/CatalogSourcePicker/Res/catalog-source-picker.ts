import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { CatalogSource } from '@local/ck-gen';

/**
 * The picker of the source of the opened catalog: a World (a Stack url or a local folder, then its default or LTS
 * World), one of its branches, one of its profiles, then "Open" opens the catalog of the {@link CatalogSource}.
 */
@Component( {
    selector: 'ck-catalog-source-picker',
    imports: [
        FormsModule,
        TranslatePipe,
        NzButtonModule,
        NzCheckboxModule,
        NzInputModule,
        NzSelectModule
    ],
    templateUrl: './catalog-source-picker.html'
} )
export class CatalogSourcePicker {
    // <PreDependencyInjection revert />
    readonly source = inject( CatalogSource );
    // <PostDependencyInjection />
}

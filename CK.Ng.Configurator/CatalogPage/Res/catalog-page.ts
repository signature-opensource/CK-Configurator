import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTabsModule } from 'ng-zorro-antd/tabs';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { CatalogInfo, CommandInfo, ConfigurableTypeInfo, GetClosureQCommand, GetCommandsQCommand, GetConfigurableTypesQCommand, HttpCrisEndpoint, OpenCatalogCommand } from '@local/ck-gen';

/** A configurable type and its depth in the list: an option is shown under its owner. */
export interface CatalogRow {
    readonly type: ConfigurableTypeInfo;
    readonly depth: number;
}

/**
 * The catalog page: a source (a Stack, a branch, its CI builds) is opened, its configurable types are listed (the
 * options under their owners), the selected ones show what they bring in (their closure) and the commands are listed.
 */
@Component( {
    selector: 'ck-catalog-page',
    imports: [
        FormsModule,
        TranslatePipe,
        NzAlertModule,
        NzButtonModule,
        NzCheckboxModule,
        NzInputModule,
        NzTableModule,
        NzTabsModule,
        NzTagModule
    ],
    templateUrl: './catalog-page.html'
} )
export class CatalogPage {
    // <PreDependencyInjection revert />
    readonly #cris = inject( HttpCrisEndpoint );
    // <PostDependencyInjection />

    // <PreLocalVariables revert />
    /** The source. */
    stack = 'https://github.com/signature-opensource/CK-Stack';
    branch = 'explo/uc';
    isCI = true;

    readonly loading = signal( false );
    readonly error = signal<string | null>( null );
    /** The opened catalog (undefined before the first opening). */
    readonly info = signal<CatalogInfo | undefined>( undefined );
    readonly types = signal<ReadonlyArray<ConfigurableTypeInfo>>( [] );
    readonly commands = signal<ReadonlyArray<CommandInfo>>( [] );
    /** The type shown in the detail panel. */
    readonly current = signal<ConfigurableTypeInfo | undefined>( undefined );
    /** The selected types (their full names): the roots of the closure. */
    readonly selection = signal<ReadonlySet<string>>( new Set() );
    /** The closure of the selection (undefined before it is computed). */
    readonly closure = signal<ReadonlyArray<string> | undefined>( undefined );

    /** The configurable types, each option under its owner (an option of an unknown owner is at the root). */
    readonly rows = computed( () => {
        const types = this.types();
        const byTypeName = new Map( types.map( t => [t.typeName, t] ) );
        const options = new Map<string, ConfigurableTypeInfo[]>();
        const roots: ConfigurableTypeInfo[] = [];
        for( const t of types ) {
            const owner = t.optionOwners.find( o => byTypeName.has( o ) );
            if( owner ) {
                let list = options.get( owner );
                if( !list ) options.set( owner, list = [] );
                list.push( t );
            } else {
                roots.push( t );
            }
        }
        const rows: CatalogRow[] = [];
        const add = ( t: ConfigurableTypeInfo, depth: number ) => {
            rows.push( { type: t, depth } );
            for( const o of options.get( t.typeName ) ?? [] ) add( o, depth + 1 );
        };
        for( const t of roots ) add( t, 0 );
        return rows;
    } );
    // <PostLocalVariables />

    async openAsync(): Promise<void> {
        this.loading.set( true );
        this.error.set( null );
        this.closure.set( undefined );
        this.selection.set( new Set() );
        this.current.set( undefined );
        try {
            this.info.set( await this.#cris.sendOrThrowAsync( this.#source( new OpenCatalogCommand() ) ) );
            this.types.set( await this.#cris.sendOrThrowAsync( this.#source( new GetConfigurableTypesQCommand() ) ) ?? [] );
            this.commands.set( await this.#cris.sendOrThrowAsync( this.#source( new GetCommandsQCommand() ) ) ?? [] );
        } catch( e ) {
            this.error.set( String( e ) );
        } finally {
            this.loading.set( false );
        }
    }

    toggle( type: ConfigurableTypeInfo, selected: boolean ): void {
        const s = new Set( this.selection() );
        if( selected ) s.add( type.typeName ); else s.delete( type.typeName );
        this.selection.set( s );
        this.closure.set( undefined );
    }

    /** Computes what the selection brings in, among the packages of the configurable types. */
    async closureAsync(): Promise<void> {
        this.loading.set( true );
        this.error.set( null );
        try {
            const c = this.#source( new GetClosureQCommand() );
            c.roots.push( ...this.selection() );
            c.packages.push( ...new Set( this.types().map( t => t.packageId ) ) );
            this.closure.set( await this.#cris.sendOrThrowAsync( c ) ?? [] );
        } catch( e ) {
            this.error.set( String( e ) );
        } finally {
            this.loading.set( false );
        }
    }

    /** The name of a configurable type when the full name is one, the full name otherwise. */
    displayName( typeName: string ): string {
        return this.types().find( t => t.typeName === typeName )?.name ?? typeName;
    }

    #source<T extends { stack: string, branch: string, isCI: boolean }>( c: T ): T {
        c.stack = this.stack;
        c.branch = this.branch;
        c.isCI = this.isCI;
        return c;
    }
}

import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTabsModule } from 'ng-zorro-antd/tabs';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { NzTooltipModule } from 'ng-zorro-antd/tooltip';
import { CatalogSource, CatalogSourcePicker, ConfigurableTypeInfo, GetClosureQCommand, GetPackageQCommand, HttpCrisEndpoint, MarkdownComponent, PackageInfo, schemaKeys, SchemaKey } from '@local/ck-gen';

/** A configurable type and its depth in the list: an option is shown under its owner. */
export interface CatalogRow {
    readonly type: ConfigurableTypeInfo;
    readonly depth: number;
}

/**
 * A level of the configuration: the AppContext aspects, the App aspects, the party features or the client features (an
 * option is in the level of its kind, under its owner), and "Other" for an unknown kind.
 */
export interface CatalogLevel {
    readonly kind: string;
    readonly rows: ReadonlyArray<CatalogRow>;
}

/** The levels, in the order of the configuration: the AppContext, an App, a party, a client application. */
const levelKinds = ['AppContextAspect', 'AppAspect', 'PartyFeature', 'PartyClientFeature'];

/** The level of a kind: an option ("PartyFeatureOption") is in the level of its owners ("PartyFeature"). */
function levelOf( kind: string ): string {
    const k = kind.endsWith( 'Option' ) ? kind.substring( 0, kind.length - 6 ) : kind;
    return levelKinds.includes( k ) ? k : 'Other';
}

/**
 * The catalog page: the opened catalog of the application ({@link CatalogSource}) lists its configurable types (the
 * options under their owners), the selected ones show what they bring in (their closure), its commands and its packages.
 * Without a picker in the shell of the application, the page shows its own.
 */
@Component( {
    selector: 'ck-catalog-page',
    imports: [
        FormsModule,
        TranslatePipe,
        NzAlertModule,
        NzButtonModule,
        NzInputModule,
        NzModalModule,
        MarkdownComponent,
        CatalogSourcePicker,
        NzTableModule,
        NzTabsModule,
        NzTagModule,
        NzTooltipModule
    ],
    templateUrl: './catalog-page.html'
} )
export class CatalogPage {
    // <PreDependencyInjection revert />
    readonly #cris = inject( HttpCrisEndpoint );
    /** The opened catalog and the choice of its source. */
    readonly source = inject( CatalogSource );
    // <PostDependencyInjection />

    // <PreLocalVariables revert />
    /** The package whose readme is shown. */
    readonly package = signal<PackageInfo | undefined>( undefined );
    /** True while the closure is computed. */
    readonly loading = signal( false );
    /** The last error of the page (the errors of the source are the source's ones). */
    readonly error = signal<string | null>( null );
    readonly types = this.source.types;
    readonly commands = this.source.commands;
    readonly packages = this.source.packages;
    /** The identifiers of the packages in the catalog (lower case): a dependency in it can be opened. */
    readonly packageIds = computed( () => new Set( this.packages().map( p => p.packageId.toLowerCase() ) ) );
    /** The filter of the packages (a part of their identifier). */
    readonly packageFilter = signal( '' );
    readonly filteredPackages = computed( () => {
        const f = this.packageFilter().trim().toLowerCase();
        return f ? this.packages().filter( p => p.packageId.toLowerCase().includes( f ) ) : this.packages();
    } );
    /** The opened packages (their identifiers, lower case). */
    readonly expandedPackages = signal<ReadonlySet<string>>( new Set() );
    /** The readmes of the opened packages (by identifier, lower case): undefined while it is read. */
    readonly readmes = signal<ReadonlyMap<string, string | undefined>>( new Map() );
    /** The selected tab: the configurable types, the commands, the packages. */
    readonly tab = signal( 0 );
    /** The command that is selected (its name). */
    readonly currentCommand = signal<string | undefined>( undefined );
    /** The type shown in the detail panel. */
    readonly current = signal<ConfigurableTypeInfo | undefined>( undefined );
    /** The selected types (their full names): the roots of the closure. */
    readonly selection = signal<ReadonlySet<string>>( new Set() );
    /** The closure of the selection (undefined before it is computed). */
    readonly closure = signal<ReadonlyArray<string> | undefined>( undefined );
    /** The keys of the configuration model of the current type (empty without a model). */
    readonly currentKeys = computed( () => {
        const schema = this.current()?.configurationSchema;
        return schema ? schemaKeys( schema ) : [];
    } );

    /**
     * The configurable types by level (the empty levels are omitted), each option under its owner (an option of an
     * unknown owner is at the root of its level).
     */
    readonly levels = computed( () => {
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
        const levels: CatalogLevel[] = [];
        for( const kind of [...levelKinds, 'Other'] ) {
            const levelRows = rows.filter( r => levelOf( r.type.kind ) === kind );
            if( levelRows.length > 0 ) levels.push( { kind, rows: levelRows } );
        }
        return levels;
    } );
    // <PostLocalVariables />

    constructor() {
        // Another catalog is opened: the state of the page is about the previous one.
        let previous = this.source.info();
        effect( () => {
            const info = this.source.info();
            if( info === previous ) return;
            previous = info;
            untracked( () => this.#reset() );
        } );
    }

    #reset(): void {
        this.error.set( null );
        this.closure.set( undefined );
        this.selection.set( new Set() );
        this.current.set( undefined );
        this.currentCommand.set( undefined );
        this.expandedPackages.set( new Set() );
        this.readmes.set( new Map() );
        this.package.set( undefined );
    }

    /** Shows the readme of a package of the opened catalog. */
    async openPackageAsync( packageId: string ): Promise<void> {
        this.error.set( null );
        try {
            const c = this.source.source( new GetPackageQCommand() );
            c.packageId = packageId;
            this.package.set( await this.#cris.sendOrThrowAsync( c ) );
        } catch( e ) {
            this.error.set( String( e ) );
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
            const c = this.source.source( new GetClosureQCommand() );
            c.roots.push( ...this.selection() );
            c.packages.push( ...new Set( this.types().map( t => t.packageId ) ) );
            this.closure.set( await this.#cris.sendOrThrowAsync( c ) ?? [] );
        } catch( e ) {
            this.error.set( String( e ) );
        } finally {
            this.loading.set( false );
        }
    }

    /** Opens or closes a package of the packages tab (an opened package shows its readme, read once). */
    togglePackage( packageId: string, expanded: boolean ): void {
        const key = packageId.toLowerCase();
        const s = new Set( this.expandedPackages() );
        if( expanded ) s.add( key ); else s.delete( key );
        this.expandedPackages.set( s );
        if( expanded && !this.readmes().has( key ) && this.packages().find( p => p.packageId.toLowerCase() === key )?.hasReadme ) {
            this.#loadReadmeAsync( packageId );
        }
    }

    async #loadReadmeAsync( packageId: string ): Promise<void> {
        const key = packageId.toLowerCase();
        this.readmes.set( new Map( this.readmes() ).set( key, undefined ) );
        try {
            const c = this.source.source( new GetPackageQCommand() );
            c.packageId = packageId;
            const p = await this.#cris.sendOrThrowAsync( c );
            this.readmes.set( new Map( this.readmes() ).set( key, p?.readme ?? '' ) );
        } catch( e ) {
            // Read again on the next opening.
            const m = new Map( this.readmes() );
            m.delete( key );
            this.readmes.set( m );
            this.error.set( String( e ) );
        }
    }

    /** Selects a configurable type in the configurable types tab. */
    selectType( typeName: string ): void {
        const t = this.types().find( t => t.typeName === typeName );
        if( !t ) return;
        this.current.set( t );
        this.#show( 0, 'ck-type-' + typeName );
    }

    /** Selects a command in the commands tab. */
    selectCommand( name: string ): void {
        this.currentCommand.set( name );
        this.#show( 1, 'ck-command-' + name );
    }

    /** Opens a package in the packages tab (its filter is cleared when it hides it). */
    selectPackage( packageId: string ): void {
        const f = this.packageFilter().trim().toLowerCase();
        if( f && !packageId.toLowerCase().includes( f ) ) this.packageFilter.set( '' );
        this.togglePackage( packageId, true );
        this.#show( 2, 'ck-package-' + packageId.toLowerCase() );
    }

    /** Selects a tab and scrolls to an element of it (once the tab is rendered). */
    #show( tab: number, elementId: string ): void {
        this.tab.set( tab );
        setTimeout( () => document.getElementById( elementId )?.scrollIntoView( { block: 'center', behavior: 'smooth' } ), 50 );
    }

    /** The name of a configurable type when the full name is one, the full name otherwise. */
    displayName( typeName: string ): string {
        return this.types().find( t => t.typeName === typeName )?.name ?? typeName;
    }
}

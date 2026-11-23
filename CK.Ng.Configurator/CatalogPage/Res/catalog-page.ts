import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTabsModule } from 'ng-zorro-antd/tabs';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { BranchInfo, CatalogInfo, CommandInfo, ConfigurableTypeInfo, GetBranchesQCommand, GetClosureQCommand, GetCommandsQCommand, GetConfigurableTypesQCommand, GetPackageQCommand, GetProfilesQCommand, GetWorldsQCommand, HttpCrisEndpoint, MarkdownComponent, OpenCatalogCommand, PackageInfo, ProfileInfo } from '@local/ck-gen';

/** A configurable type and its depth in the list: an option is shown under its owner. */
export interface CatalogRow {
    readonly type: ConfigurableTypeInfo;
    readonly depth: number;
}

/**
 * A level of the configuration: the Builder aspects, the App aspects, the party features or the client features (an
 * option is in the level of its kind, under its owner), and "Other" for an unknown kind.
 */
export interface CatalogLevel {
    readonly kind: string;
    readonly rows: ReadonlyArray<CatalogRow>;
}

/** A key of a configuration model, from its JSON Schema (the keys of the nested models follow their parent). */
export interface SchemaKey {
    /** The configuration path from the section of the type: "Passkey:RpId", "Schemes:{name}:ClientId". */
    readonly path: string;
    readonly depth: number;
    /** The type, in plain words: "string", "time span", "one of Off, Lax, Strict", "list of string", "section"... */
    readonly type: string;
    /** The schema of an opaque section (Kestrel), when it has one. */
    readonly schemaUri?: string;
    readonly required: boolean;
    readonly defaultValue?: string;
    /** The level when it is not Init, "secret", "per deployment". */
    readonly flags: ReadonlyArray<string>;
    readonly description?: string;
}

type JsonSchema = { [key: string]: any };

/**
 * The keys of a model from its JSON Schema (CK.ConfigurationModel's ConfigurationModelSchema): one row per property,
 * the properties of a nested model (a $ref to its $defs) under it, with their configuration path. A model that contains
 * itself is not expanded again.
 */
export function schemaKeys( schemaText: string ): ReadonlyArray<SchemaKey> {
    let root: JsonSchema;
    try {
        root = JSON.parse( schemaText );
    } catch {
        return [];
    }
    const defs: JsonSchema = root['$defs'] ?? {};
    const result: SchemaKey[] = [];
    const resolve = ( s: JsonSchema ): { schema: JsonSchema, def?: string } => {
        const ref: string | undefined = s['$ref'];
        if( ref?.startsWith( '#/$defs/' ) ) {
            const def = ref.substring( 8 );
            return { schema: defs[def] ?? {}, def };
        }
        return { schema: s };
    };
    const walk = ( model: JsonSchema, prefix: string, depth: number, visiting: ReadonlyArray<string> ) => {
        const properties: JsonSchema = model['properties'] ?? {};
        const required: ReadonlyArray<string> = model['required'] ?? [];
        for( const name of Object.keys( properties ) ) {
            const p: JsonSchema = properties[name];
            const flags: string[] = [];
            if( p['x-ck-level'] ) flags.push( p['x-ck-level'] );
            if( p['x-ck-secret'] ) flags.push( 'secret' );
            if( p['x-ck-perDeployment'] ) flags.push( 'per deployment' );
            const path = prefix + name;
            // The nested model (a section, the items of a list, the entries of a dictionary) and its path.
            let nested: { schema: JsonSchema, def?: string, path: string } | undefined;
            let type: string;
            let schemaUri: string | undefined;
            const r = resolve( p );
            if( r.def ) {
                type = 'section';
                nested = { ...r, path: path + ':' };
            } else if( p['$ref'] ) {
                type = 'section';
                schemaUri = p['$ref'];
            } else if( p['type'] === 'array' && p['items'] ) {
                const item = resolve( p['items'] );
                if( item.def ) {
                    type = 'list of sections';
                    nested = { ...item, path: path + ':{n}:' };
                } else {
                    type = 'list of ' + valueType( p['items'] );
                }
            } else if( p['anyOf'] ) {
                const array: JsonSchema | undefined = ( p['anyOf'] as JsonSchema[] ).find( a => a['type'] === 'array' );
                type = 'list of ' + ( array ? valueType( array['items'] ?? {} ) : 'values' ) + ' (or a comma separated string)';
            } else if( p['type'] === 'object' && p['additionalProperties'] ) {
                const entry = resolve( p['additionalProperties'] );
                type = 'sections by name';
                if( entry.def ) nested = { ...entry, path: path + ':{name}:' };
            } else if( Object.keys( p ).every( k => k === 'description' || k.startsWith( 'x-ck-' ) ) ) {
                type = 'section';
            } else {
                type = valueType( p );
            }
            result.push( {
                path,
                depth,
                type,
                schemaUri,
                required: required.includes( name ),
                defaultValue: p['default'] !== undefined ? String( p['default'] ) : undefined,
                flags,
                description: p['description']
            } );
            if( nested && !visiting.includes( nested.def! ) ) {
                walk( nested.schema, nested.path, depth + 1, [...visiting, nested.def!] );
            }
        }
    };
    walk( root, '', 0, [] );
    return result;
}

/** The type of a value in plain words. */
function valueType( s: JsonSchema ): string {
    if( Array.isArray( s['enum'] ) ) return 'one of ' + ( s['enum'] as string[] ).join( ', ' );
    if( s['format'] === 'uuid' ) return 'guid';
    if( s['format'] === 'uri' ) return 'uri';
    if( s['x-ck-format'] === 'timespan' ) return 'time span';
    return s['type'] ?? 'value';
}

/** The levels, in the order of the configuration: the builder, an App, a party, a client application. */
const levelKinds = ['AppBuilderAspect', 'AppAspect', 'PartyFeature', 'PartyClientFeature'];

/** The level of a kind: an option ("PartyFeatureOption") is in the level of its owners ("PartyFeature"). */
function levelOf( kind: string ): string {
    const k = kind.endsWith( 'Option' ) ? kind.substring( 0, kind.length - 6 ) : kind;
    return levelKinds.includes( k ) ? k : 'Other';
}

/**
 * The catalog page: a source (a World, then one of its branches, then one of its profiles) is opened, its configurable types are listed (the
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
        NzModalModule,
        MarkdownComponent,
        NzSelectModule,
        NzTableModule,
        NzTabsModule,
        NzTagModule
    ],
    templateUrl: './catalog-page.html'
} )
export class CatalogPage {
    // <PreDependencyInjection revert />
    readonly #cris = inject( HttpCrisEndpoint );
    readonly #translate = inject( TranslateService );
    // <PostDependencyInjection />

    // <PreLocalVariables revert />
    /** The World: a Stack url (its default World), followed by "/tree/main/@{LTS}" for an LTS World, or a local folder. */
    world = 'https://github.com/signature-opensource/CK-Stack';
    /** The selected profile version. */
    version: string | undefined;
    /** The Worlds of the Stack of the World input: its default World, then its LTS Worlds. */
    readonly worlds = signal<ReadonlyArray<string>>( [] );
    /** The World whose branches are listed (the input may have changed since). */
    readonly listedWorld = signal( '' );
    /** The package whose readme is shown. */
    readonly package = signal<PackageInfo | undefined>( undefined );

    /** Whether the deprecated profiles are listed. */
    readonly includeDeprecated = signal( false );
    /** The branches of the World (undefined before they are listed). */
    readonly branches = signal<ReadonlyArray<BranchInfo> | undefined>( undefined );
    /** The selected branch. */
    readonly branch = signal<string | undefined>( undefined );
    /** The profiles of the selected branch, the latest first. */
    readonly profiles = signal<ReadonlyArray<ProfileInfo>>( [] );
    /** True while the branches or the profiles are listed. */
    readonly listing = signal( false );
    /** The World and the version of the opened catalog. */
    readonly opened = signal<{ world: string, version: string } | undefined>( undefined );
    readonly openedVersion = computed( () => this.opened()?.version );

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

    /** "2026.11.23", the clash number if any ("2026.11.23 (1)"), then the CI and deprecated tags. */
    profileLabel( p: ProfileInfo ): string {
        let label = p.clashNumber > 0 ? `${p.date} (${p.clashNumber})` : p.date;
        if( p.isCI ) label += ' - CI';
        if( p.isDeprecated ) label += ` - ${this.#translate.instant( 'CK.Configurator.Catalog.Deprecated' )}`;
        if( !p.hasFeeds ) label += ` - ${this.#translate.instant( 'CK.Configurator.Catalog.NoFeeds' )}`;
        return label;
    }

    /** "CK" for the default World of a Stack, its LTS name ("@net8") otherwise. */
    worldLabel( world: string ): string {
        const lts = /[\\/](@[^\\/]+)$/.exec( world );
        return lts ? lts[1] : this.#translate.instant( 'CK.Configurator.Catalog.DefaultWorld' );
    }

    /** Lists the Worlds of the Stack of the World input, then the branches of the World input. */
    async loadBranchesAsync(): Promise<void> {
        this.listing.set( true );
        this.error.set( null );
        this.worlds.set( [] );
        try {
            const c = new GetWorldsQCommand();
            c.world = this.world;
            const worlds = await this.#cris.sendOrThrowAsync( c ) ?? [];
            this.worlds.set( worlds );
            // The World of the input (when it is one of them, in its canonical form), the default World otherwise.
            const input = this.world.trim().replace( /[\\/]+$/, '' ).toLowerCase();
            const selected = worlds.find( w => w.toLowerCase() === input ) ?? worlds.find( w => input.endsWith( this.worldLabel( w ).toLowerCase() ) ) ?? worlds[0];
            if( selected ) await this.selectWorldAsync( selected );
        } catch( e ) {
            this.error.set( String( e ) );
        } finally {
            this.listing.set( false );
        }
    }

    /** Lists the branches of a World and selects the first one. */
    async selectWorldAsync( world: string ): Promise<void> {
        this.listing.set( true );
        this.error.set( null );
        this.branches.set( undefined );
        this.branch.set( undefined );
        this.profiles.set( [] );
        this.version = undefined;
        try {
            this.listedWorld.set( world );
            const c = new GetBranchesQCommand();
            c.world = this.listedWorld();
            c.includeDeprecated = this.includeDeprecated();
            const branches = await this.#cris.sendOrThrowAsync( c ) ?? [];
            this.branches.set( branches );
            if( branches.length > 0 ) await this.selectBranchAsync( branches[0].name );
        } catch( e ) {
            this.error.set( String( e ) );
        } finally {
            this.listing.set( false );
        }
    }

    /** Lists the profiles of a branch and selects the latest one that can be opened. */
    async selectBranchAsync( branch: string ): Promise<void> {
        this.listing.set( true );
        this.error.set( null );
        this.branch.set( branch );
        this.profiles.set( [] );
        this.version = undefined;
        try {
            const c = new GetProfilesQCommand();
            c.world = this.listedWorld();
            c.branch = branch;
            c.includeDeprecated = this.includeDeprecated();
            const profiles = await this.#cris.sendOrThrowAsync( c ) ?? [];
            this.profiles.set( profiles );
            this.version = profiles.find( p => p.hasFeeds )?.version;
        } catch( e ) {
            this.error.set( String( e ) );
        } finally {
            this.listing.set( false );
        }
    }

    async setIncludeDeprecated( include: boolean ): Promise<void> {
        this.includeDeprecated.set( include );
        if( this.branches() ) await this.selectWorldAsync( this.listedWorld() );
    }

    /** Shows the readme of a package of the opened catalog. */
    async openPackageAsync( packageId: string ): Promise<void> {
        this.error.set( null );
        try {
            const c = this.#source( new GetPackageQCommand() );
            c.packageId = packageId;
            this.package.set( await this.#cris.sendOrThrowAsync( c ) );
        } catch( e ) {
            this.error.set( String( e ) );
        }
    }

    async openAsync(): Promise<void> {
        if( !this.version ) return;
        this.opened.set( { world: this.listedWorld(), version: this.version } );
        this.info.set( undefined );
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

    /** Sets the source of the opened catalog (not the one being chosen). */
    #source<T extends { world: string, version: string }>( c: T ): T {
        const o = this.opened()!;
        c.world = o.world;
        c.version = o.version;
        return c;
    }
}

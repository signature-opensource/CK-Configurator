import { Component, computed, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzBadgeModule } from 'ng-zorro-antd/badge';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSwitchModule } from 'ng-zorro-antd/switch';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { NzTooltipModule } from 'ng-zorro-antd/tooltip';
import {
    AddPropertyCommand, CatalogSource, CatalogSourcePicker, CheckSolutionCommand, CloseSolutionCommand, ConfigurableTypeInfo,
    CreateDeploymentCommand, EditorDocument, EditorNode, EditorProblem, EditorState, GetEditorStateQCommand, HttpCrisEndpoint,
    MarkdownComponent, OpenSolutionCommand, PickFolderCommand, RedoCommand, ReloadCommand, RemovePropertyCommand, RevertPropertyCommand,
    SaveCommand, SetCatalogSourceCommand, SetValueCommand, UndoCommand
} from '@local/ck-gen';

/** A row of the tree: a property and its problems (its own and the ones below it). */
export interface EditorRow {
    readonly node: EditorNode;
    readonly errors: number;
    readonly warnings: number;
}

/** What the palette offers at the selected property: a configurable type that can be added. */
export interface PaletteItem {
    readonly type: ConfigurableTypeInfo;
    /** Whether the key is already in the document. */
    readonly present: boolean;
}

const lastFolderKey = 'ck-configurator-editor-folder';
/** The polling of the state while the builders check the configuration. */
const checkPollingMs = 2000;

function readLastFolder(): string {
    try { return localStorage.getItem( lastFolderKey ) ?? ''; } catch { return ''; }
}

/**
 * The editor of the central configuration of a solution: the document is held by the backend, every edit is a command
 * that returns its whole state (see CK.IO.Configurator).
 */
@Component( {
    selector: 'ck-editor-page',
    imports: [
        FormsModule,
        TranslatePipe,
        NzAlertModule,
        NzBadgeModule,
        NzButtonModule,
        NzCheckboxModule,
        NzInputModule,
        NzSelectModule,
        NzSwitchModule,
        NzTagModule,
        NzTooltipModule,
        CatalogSourcePicker,
        MarkdownComponent
    ],
    templateUrl: './editor-page.html'
} )
export class EditorPage {
    readonly #cris = inject( HttpCrisEndpoint );
    readonly source = inject( CatalogSource );

    /** The folder input. */
    readonly folder = signal( readLastFolder() );
    /** Whether the folder dialog of the desktop is available (it is hidden once it failed). */
    readonly canPickFolder = signal( true );
    /** The state of the document (null when no document is opened). */
    readonly state = signal<EditorState | undefined>( undefined );
    /** True while a command runs. */
    readonly busy = signal( false );
    readonly error = signal<string | null>( null );
    /** The path of the selected property. */
    readonly selectedPath = signal<string | undefined>( undefined );
    /** The collapsed objects (their path). */
    readonly collapsed = signal<ReadonlySet<string>>( new Set() );
    /** The JSON text of the value being edited. */
    readonly valueText = signal( '' );
    /** The property to add to the selected object. */
    readonly newName = signal( '' );
    readonly newValue = signal( 'true' );
    /** The deployment of the view (null for the central configuration). */
    readonly view = signal<string | null>( null );
    /** The form of a new deployment (shown when not null): its name, its base, whether its values per deployment are written. */
    readonly newDeployment = signal<{ name: string, base: string | null, writeValues: boolean } | null>( null );
    #pollTimer: ReturnType<typeof setTimeout> | undefined;

    readonly isOpened = computed( () => !!this.state()?.folder );
    readonly documents = computed<ReadonlyArray<EditorDocument>>( () => this.state()?.documents ?? [] );
    readonly deployments = computed( () => this.documents().filter( d => d.name != null ) );
    readonly isDeploymentView = computed( () => this.view() != null );
    readonly problems = computed<ReadonlyArray<EditorProblem>>( () => this.state()?.problems ?? [] );

    /** The visible rows of the tree (the children of a collapsed object are hidden). */
    readonly rows = computed<ReadonlyArray<EditorRow>>( () => {
        const nodes = this.state()?.nodes ?? [];
        const problems = this.problems();
        const collapsed = this.collapsed();
        const rows: EditorRow[] = [];
        for( const n of nodes ) {
            let hidden = false;
            for( const c of collapsed ) {
                if( n.path.startsWith( c + ':' ) ) { hidden = true; break; }
            }
            if( hidden ) continue;
            const mine = problems.filter( p => p.path === n.path || p.path.startsWith( n.path + ':' ) );
            rows.push( { node: n, errors: mine.filter( p => p.severity === 'Error' ).length, warnings: mine.filter( p => p.severity === 'Warning' ).length } );
        }
        return rows;
    } );

    readonly selected = computed( () => {
        const path = this.selectedPath();
        return path ? this.state()?.nodes.find( n => n.path === path ) : undefined;
    } );

    readonly selectedProblems = computed( () => {
        const path = this.selectedPath();
        return path ? this.problems().filter( p => p.path === path ) : [];
    } );

    /** The configurable type of the selected property (its documentation), from the opened catalog. */
    readonly selectedType = computed( () => this.typeOf( this.selected() ) );

    /** Whether the opened catalog is the one of the document. */
    readonly catalogMismatch = computed( () => {
        const s = this.state();
        const o = this.source.opened();
        return !!s?.catalogVersion && !!o && (o.version !== s.catalogVersion || o.world !== s.catalogWorld);
    } );

    /** Whether the catalog of the document failed to open (the opened source is the one of the document, without info). */
    readonly catalogFailed = computed( () => {
        const s = this.state();
        const o = this.source.opened();
        return !!s?.catalogVersion && !!o && o.version === s.catalogVersion && o.world === s.catalogWorld
               && !this.source.info() && !!this.source.error();
    } );

    /**
     * The palette: what the catalog offers at the selected property. A party: the party features; a client: the client
     * features; a feature or a client feature: its options; an App: nothing yet (its aspects are under "Aspects").
     */
    readonly palette = computed<ReadonlyArray<PaletteItem>>( () => {
        const n = this.selected();
        const types = this.source.types();
        if( !n || types.length === 0 ) return [];
        const present = new Set( ( this.state()?.nodes ?? [] ).filter( c => c.path.startsWith( n.path + ':' ) && c.depth === n.depth + 1 ).map( c => c.name.toLowerCase() ) );
        let offered: ConfigurableTypeInfo[] = [];
        switch( n.role ) {
            case 'Party':
            case 'PartyDefaults':
                offered = types.filter( t => t.kind === 'PartyFeature' );
                break;
            case 'Client':
                offered = types.filter( t => t.kind === 'PartyClientFeature' );
                break;
            case 'PartyFeature':
            case 'ClientFeature': {
                const owner = this.typeOf( n );
                const optionKind = n.role === 'PartyFeature' ? 'PartyFeatureOption' : 'PartyClientFeatureOption';
                if( owner ) offered = types.filter( t => t.kind === optionKind && t.optionOwners.includes( owner.typeName ) );
                break;
            }
            default:
                if( n.name.toLowerCase() === 'aspects' ) {
                    const parent = this.state()?.nodes.find( p => p.path === n.path.substring( 0, n.path.lastIndexOf( ':' ) ) );
                    offered = types.filter( t => t.kind === ( parent?.role === 'Builder' ? 'AppBuilderAspect' : 'AppAspect' ) );
                }
        }
        return offered.map( type => ( { type, present: present.has( type.name.toLowerCase() ) } ) )
                      .sort( ( a, b ) => a.type.name.localeCompare( b.type.name ) );
    } );

    constructor() {
        // The state of the backend (a document may be opened).
        void this.runAsync( new GetEditorStateQCommand() );
        // The catalog of the document is opened (S3): it types and checks the document.
        effect( () => {
            const s = this.state();
            if( !s?.catalogWorld || !s.catalogVersion ) return;
            const o = untracked( () => this.source.opened() );
            if( o?.world === s.catalogWorld && o.version === s.catalogVersion ) return;
            if( untracked( () => this.source.opening() ) ) return;
            void this.openCatalogAsync( s.catalogWorld, s.catalogVersion );
        } );
        // The value input follows the selection.
        effect( () => {
            const n = this.selected();
            untracked( () => this.valueText.set( n?.value ?? '' ) );
        } );
        // While the builders check the saved configuration, the state is polled.
        effect( () => {
            const running = this.state()?.checkStatus === 'Running';
            clearTimeout( this.#pollTimer );
            if( running ) this.#pollTimer = setTimeout( () => void this.#pollAsync(), checkPollingMs );
        } );
        inject( DestroyRef ).onDestroy( () => clearTimeout( this.#pollTimer ) );
    }

    async #pollAsync(): Promise<void> {
        if( this.busy() ) {
            this.#pollTimer = setTimeout( () => void this.#pollAsync(), checkPollingMs );
            return;
        }
        const c = new GetEditorStateQCommand();
        c.deployment = this.view() ?? undefined;
        try {
            const s = await this.#cris.sendOrThrowAsync( c ) as EditorState;
            // The view may have changed meanwhile: only a state of the current view is taken.
            if( s.view == this.view() ) this.#takeState( s );
        } catch {
            // The next command shows the error.
        }
    }

    typeOf( n: EditorNode | undefined ): ConfigurableTypeInfo | undefined {
        if( !n?.typeName ) return undefined;
        const kind = n.role === 'BuilderAspect' ? 'AppBuilderAspect'
                   : n.role === 'AppAspect' ? 'AppAspect'
                   : n.role === 'PartyFeature' ? 'PartyFeature'
                   : n.role === 'PartyFeatureOption' ? 'PartyFeatureOption'
                   : n.role === 'ClientFeature' ? 'PartyClientFeature'
                   : n.role === 'ClientFeatureOption' ? 'PartyClientFeatureOption'
                   : undefined;
        return this.source.types().find( t => t.name === n.typeName && t.kind === kind );
    }

    isObject( n: EditorNode ): boolean {
        return n.kind === 'Object';
    }

    toggleCollapsed( n: EditorNode ): void {
        const c = new Set( this.collapsed() );
        if( !c.delete( n.path ) ) c.add( n.path );
        this.collapsed.set( c );
    }

    select( path: string ): void {
        this.selectedPath.set( path );
    }

    async #sendAsync<T>( command: any ): Promise<T | undefined> {
        this.busy.set( true );
        this.error.set( null );
        try {
            return await this.#cris.sendOrThrowAsync( command ) as T;
        } catch( e ) {
            this.error.set( String( e ) );
            return undefined;
        } finally {
            this.busy.set( false );
        }
    }

    /** Sends an editor command (on the current view when it has a view) and takes its state: its view becomes the current one. */
    async runAsync( command: any ): Promise<void> {
        if( 'deployment' in command ) command.deployment = this.view() ?? undefined;
        const s = await this.#sendAsync<EditorState>( command );
        if( s ) this.#takeState( s );
    }

    #takeState( s: EditorState ): void {
        this.state.set( s );
        this.view.set( s.view ?? null );
        if( this.selectedPath() && !s.nodes.some( n => n.path === this.selectedPath() ) ) this.selectedPath.set( undefined );
    }

    /** Shows another view (the central configuration: null). */
    selectViewAsync( deployment: string | null ): Promise<void> {
        this.view.set( deployment );
        return this.runAsync( new GetEditorStateQCommand() );
    }

    startNewDeployment(): void {
        this.newDeployment.set( { name: '#', base: null, writeValues: true } );
    }

    async createDeploymentAsync(): Promise<void> {
        const f = this.newDeployment();
        if( !f ) return;
        const c = new CreateDeploymentCommand();
        c.name = f.name.trim();
        c.base = f.base ?? undefined;
        c.writeValuesPerDeployment = f.writeValues;
        await this.runAsync( c );
        if( !this.error() ) this.newDeployment.set( null );
    }

    checkAsync(): Promise<void> { return this.runAsync( new CheckSolutionCommand() ); }

    /** Writes the inherited value of a property in the overlay of the deployment of the view (the "WriteHere" fix). */
    writeHereAsync( path: string ): Promise<void> {
        const n = this.state()?.nodes.find( x => x.path === path );
        if( !n?.value ) return Promise.resolve();
        return this.setValueAsync( path, n.value );
    }

    revertAsync( path: string ): Promise<void> {
        const c = new RevertPropertyCommand();
        c.path = path;
        return this.runAsync( c );
    }

    /** Whether a property of a deployment view is written by its overlay (it can be reverted to the inherited one). */
    isWrittenHere( n: EditorNode ): boolean {
        return n.origin === 'Deployment' || n.origin === 'Removed';
    }

    /** Whether a property of a deployment view is inherited (from the central configuration or a base). */
    isInherited( n: EditorNode ): boolean {
        return !!n.origin && !this.isWrittenHere( n );
    }

    async openCatalogAsync( world: string, version: string ): Promise<void> {
        await this.source.openSourceAsync( world, version );
        // The document is typed and checked with it now.
        await this.runAsync( new GetEditorStateQCommand() );
    }

    async pickFolderAsync(): Promise<void> {
        const c = new PickFolderCommand();
        c.folder = this.folder();
        try {
            const folder = await this.#cris.sendOrThrowAsync( c );
            if( folder ) {
                this.folder.set( folder );
                await this.openAsync();
            }
        } catch {
            // Not a desktop window: the folder is typed.
            this.canPickFolder.set( false );
        }
    }

    async openAsync(): Promise<void> {
        const c = new OpenSolutionCommand();
        c.folder = this.folder().trim();
        this.view.set( null );
        this.newDeployment.set( null );
        this.selectedPath.set( undefined );
        this.collapsed.set( new Set() );
        await this.runAsync( c );
        if( this.isOpened() ) {
            try { localStorage.setItem( lastFolderKey, c.folder ); } catch { }
        }
    }

    closeAsync(): Promise<void> { return this.runAsync( new CloseSolutionCommand() ); }
    undoAsync(): Promise<void> { return this.runAsync( new UndoCommand() ); }
    redoAsync(): Promise<void> { return this.runAsync( new RedoCommand() ); }
    reloadAsync(): Promise<void> { return this.runAsync( new ReloadCommand() ); }

    saveAsync( overwrite: boolean ): Promise<void> {
        const c = new SaveCommand();
        c.overwrite = overwrite;
        return this.runAsync( c );
    }

    /** Uses the opened catalog for the document (its "$configurator"). */
    useOpenedCatalogAsync(): Promise<void> {
        const o = this.source.opened();
        if( !o ) return Promise.resolve();
        const c = new SetCatalogSourceCommand();
        c.world = o.world;
        c.version = o.version;
        return this.runAsync( c );
    }

    setValueAsync( path: string, json: string ): Promise<void> {
        const c = new SetValueCommand();
        c.path = path;
        c.json = json;
        return this.runAsync( c );
    }

    /** A string input: its JSON text is a JSON string. */
    setStringAsync( path: string, text: string ): Promise<void> {
        return this.setValueAsync( path, JSON.stringify( text ) );
    }

    /** The text of a JSON string value, without its quotes. */
    stringValue( n: EditorNode ): string {
        try { return JSON.parse( n.value ?? '""' ); } catch { return n.value ?? ''; }
    }

    async addAsync( objectPath: string, name: string, json: string ): Promise<void> {
        const c = new AddPropertyCommand();
        c.objectPath = objectPath;
        c.name = name;
        c.json = json;
        await this.runAsync( c );
        if( !this.error() ) {
            this.newName.set( '' );
            this.collapsed.update( s => { const c = new Set( s ); c.delete( objectPath ); return c; } );
        }
    }

    /** Adds a configurable type of the palette: true (enabled with its defaults). */
    addFromPaletteAsync( item: PaletteItem ): Promise<void> {
        return this.addAsync( this.selectedPath()!, item.type.name, 'true' );
    }

    removeAsync( path: string ): Promise<void> {
        const c = new RemovePropertyCommand();
        c.path = path;
        return this.runAsync( c );
    }

    /**
     * The path of a property as it is shown: without "Builders:" and without the ".Builder" suffix of its builder
     * ("Auth3/Auth3.Auth3:App:Local:HttpEndpoint"). The keys of the document don't change.
     */
    shortPath( path: string ): string {
        if( !path.startsWith( 'Builders:' ) ) return path;
        const rest = path.substring( 9 );
        const i = rest.indexOf( ':' );
        return i < 0 ? EditorPage.builderName( rest ) : EditorPage.builderName( rest.substring( 0, i ) ) + rest.substring( i );
    }

    /** The name of a property as it is shown: a builder without its ".Builder" suffix. */
    displayName( n: EditorNode ): string {
        return n.role === 'Builder' || ( n.depth === 1 && n.path.startsWith( 'Builders:' ) ) ? EditorPage.builderName( n.name ) : n.name;
    }

    static builderName( key: string ): string {
        return key.replace( /\.Builder$/i, '' );
    }
}

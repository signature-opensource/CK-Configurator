import { Component, computed, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzBadgeModule } from 'ng-zorro-antd/badge';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzContextMenuService, NzDropdownMenuComponent, NzDropdownModule } from 'ng-zorro-antd/dropdown';
import { NzMenuModule } from 'ng-zorro-antd/menu';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSwitchModule } from 'ng-zorro-antd/switch';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { NzTooltipModule } from 'ng-zorro-antd/tooltip';
import {
    AddPropertyCommand, CatalogSource, CatalogSourcePicker, CheckSolutionCommand, CloseSolutionCommand, ConfigurableTypeInfo,
    CreateDeploymentCommand, EditorDocument, EditorNode, EditorProblem, EditorState, GetEditorStateQCommand, HttpCrisEndpoint,
    LockPropertyCommand, MakeSecretCommand, MarkdownComponent, ModelForm, OpenSolutionCommand, PickFolderCommand, RedoCommand, ReloadCommand,
    RemoveLockEntryCommand, RemovePropertyCommand, RevertPropertyCommand, SaveCommand, SetCatalogSourceCommand, SetValueCommand, UndoCommand,
    UnlockPropertyCommand
} from '@local/ck-gen';

/** A row of the tree: a property and its problems (its own and the ones below it). */
export interface EditorRow {
    readonly node: EditorNode;
    readonly errors: number;
    readonly warnings: number;
}

/** An entry of a "$LOCK": its pattern (relative to the object that holds it) and its level. */
export interface LockEntry {
    readonly pattern: string;
    readonly level: string;
}

/** Whether a property is a "$LOCK" or in one: the locks are shown on the properties they lock (and by their holder). */
function isInLock( n: EditorNode ): boolean {
    return n.path.split( ':' ).includes( '$LOCK' );
}

/** What the palette offers at the selected property: a configurable type that can be added. */
export interface PaletteItem {
    readonly type: ConfigurableTypeInfo;
    /** Whether the key is already in the document. */
    readonly present: boolean;
}

const lastFolderKey = 'ck-configurator-editor-folder';
/** The levels of a lock, the lowest first. */
const levels = ['Compile', 'Init', 'Run'];

/** The "Make it a secret" dialog: the property, the name of the secret, optional, whether an existing one is replaced. */
export interface SecretDialog {
    readonly path: string;
    readonly name: string;
    readonly optional: boolean;
    /** True once the backend said that a development secret of this name has another value. */
    readonly askOverwrite: boolean;
}
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
        NzDropdownModule,
        NzMenuModule,
        NzModalModule,
        NzInputModule,
        NzSelectModule,
        NzSwitchModule,
        NzTagModule,
        NzTooltipModule,
        CatalogSourcePicker,
        MarkdownComponent,
        ModelForm
    ],
    templateUrl: './editor-page.html'
} )
export class EditorPage {
    readonly #cris = inject( HttpCrisEndpoint );
    readonly #contextMenu = inject( NzContextMenuService );
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
    /** The property of the contextual menu. */
    readonly menuNode = signal<EditorNode | undefined>( undefined );
    /** The "Make it a secret" dialog (shown when not null). */
    readonly secretDialog = signal<SecretDialog | null>( null );

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
            if( isInLock( n ) ) continue;
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

    /** The entries of the "$LOCK" by the path of the object that holds it (an App, an AppContext aspect...): not rows of the tree. */
    readonly lockEntries = computed<ReadonlyMap<string, ReadonlyArray<LockEntry>>>( () => {
        const nodes = this.state()?.nodes ?? [];
        const result = new Map<string, LockEntry[]>();
        for( let i = 0; i < nodes.length; ++i ) {
            const l = nodes[i];
            if( l.name !== '$LOCK' || l.kind !== 'Object' ) continue;
            const holder = l.depth === 0 ? '' : l.path.substring( 0, l.path.length - l.name.length - 1 );
            const entries: LockEntry[] = [];
            for( let j = i + 1; j < nodes.length && nodes[j].depth > l.depth; ++j ) {
                const e = nodes[j];
                if( e.depth !== l.depth + 1 ) continue;
                let level = e.value ?? '';
                try { level = JSON.parse( level ); } catch { }
                entries.push( { pattern: e.name, level } );
            }
            result.set( holder, entries );
        }
        return result;
    } );

    /** The entries of the "$LOCK" of the selected property. */
    readonly selectedLocks = computed<ReadonlyArray<LockEntry>>( () => {
        const n = this.selected();
        return n ? this.lockEntries().get( n.path ) ?? [] : [];
    } );

    /** The tooltip of the lock counter of the object that holds a "$LOCK": its patterns and their level. */
    lockEntriesTitle( path: string ): string {
        return ( this.lockEntries().get( path ) ?? [] ).map( e => `${e.pattern}: ${e.level}` ).join( '\n' );
    }

    /** Whether a property is absent: a value that a literal pattern of a "$LOCK" locks. */
    isAbsent( n: EditorNode ): boolean {
        return n.kind === 'Absent';
    }

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
                    offered = types.filter( t => t.kind === ( parent?.role === 'AppContext' ? 'AppContextAspect' : 'AppAspect' ) );
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
        const kind = n.role === 'AppContextAspect' ? 'AppContextAspect'
                   : n.role === 'AppContextAspectOption' ? 'AppContextAspectOption'
                   : n.role === 'AppAspect' ? 'AppAspect'
                   : n.role === 'AppAspectOption' ? 'AppAspectOption'
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

    /** Whether the selected property is a modeled section (true or a section of a type that has a model): its form is shown. */
    readonly selectedSchema = computed( () => {
        const n = this.selected();
        const schema = this.selectedType()?.configurationSchema;
        return n && schema && (n.kind === 'Object' || n.value === 'true') ? schema : undefined;
    } );

    /** A key written by the form of a model: its section and the missing objects of its path are created. */
    writeKeyAsync( w: { path: string, json: string } ): Promise<void> {
        const c = new SetValueCommand();
        c.path = w.path;
        c.json = w.json;
        c.createPath = true;
        return this.runAsync( c );
    }

    /** A key reset by the form of a model: removed (in a deployment: back to the inherited value). */
    resetKeyAsync( n: EditorNode ): Promise<void> {
        return this.isDeploymentView() ? this.revertAsync( n.path ) : this.removeAsync( n.path );
    }

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

    /** Opens the contextual menu of a row (a right click): the row is selected. */
    openMenu( event: MouseEvent, n: EditorNode, menu: NzDropdownMenuComponent ): void {
        event.preventDefault();
        this.select( n.path );
        this.menuNode.set( n );
        this.#contextMenu.create( event, menu );
    }

    /** The keyboard of the tree: the menu key or Shift+F10 opens the contextual menu of the selected row. */
    onTreeKeydown( event: KeyboardEvent, menu: NzDropdownMenuComponent ): void {
        if( event.key !== 'ContextMenu' && !(event.shiftKey && event.key === 'F10') ) return;
        const n = this.selected();
        const row = (event.currentTarget as HTMLElement).querySelector( '.ck-editor-selected' );
        if( !n || !row ) return;
        event.preventDefault();
        const r = row.getBoundingClientRect();
        this.menuNode.set( n );
        this.#contextMenu.create( { x: r.left + 24, y: r.bottom }, menu );
    }

    closeMenu(): void { this.#contextMenu.close(); }

    /** Whether a property is in an App ("AppContexts:{b}:App", "Defaults:App") or an AppContext aspect: it can be locked. */
    canLock( n: EditorNode ): boolean {
        if( this.isDeploymentView() ) return false;
        const s = n.path.split( ':' );
        for( let i = s.length - 1; i > 0; --i ) {
            const p = s.slice( 0, i );
            const isApp = (p.length === 3 && p[0] === 'AppContexts' && p[2].toLowerCase() === 'app')
                          || (p.length === 2 && p[0].toLowerCase() === 'defaults' && p[1].toLowerCase() === 'app');
            const isAspect = p.length === 4 && p[0] === 'AppContexts' && p[2].toLowerCase() === 'aspects' && !p[3].startsWith( '$' );
            if( isApp || isAspect ) return true;
        }
        return false;
    }

    /** The levels at which a property can be locked: up to the capability of the code when the catalog gives it. */
    lockLevels( n: EditorNode ): string[] {
        const max = n.maxLevel ? levels.indexOf( n.maxLevel ) : levels.length - 1;
        return levels.slice( 0, max + 1 );
    }

    /** Whether a value can be made a secret (a value that is not a handle). */
    canBeSecret( n: EditorNode ): boolean {
        return !n.secretName && (n.kind === 'String' || n.kind === 'Number' || n.kind === 'Boolean') && !n.path.includes( '$LOCK' );
    }

    /** The tooltip of the lock mark: its level, and what locks it. */
    lockTitle( n: EditorNode ): string {
        return `${n.lockLevel}: ${n.lockSource ?? ''}`;
    }

    lockAsync( path: string, level: string ): Promise<void> {
        const c = new LockPropertyCommand();
        c.path = path;
        c.level = level;
        return this.runAsync( c );
    }

    unlockAsync( path: string ): Promise<void> {
        const c = new UnlockPropertyCommand();
        c.path = path;
        return this.runAsync( c );
    }

    removeLockEntryAsync( objectPath: string, pattern: string ): Promise<void> {
        const c = new RemoveLockEntryCommand();
        c.objectPath = objectPath;
        c.pattern = pattern;
        return this.runAsync( c );
    }

    async copyPathAsync( path: string ): Promise<void> {
        try { await navigator.clipboard.writeText( path ); } catch { this.error.set( path ); }
    }

    /** Opens the "Make it a secret" dialog: the name is the last two names of the path ("Google:ClientSecret"). */
    startSecret( n: EditorNode ): void {
        const name = n.path.split( ':' ).slice( -2 ).map( s => s.replace( /[^A-Za-z0-9_-]/g, '_' ) ).join( ':' );
        this.secretDialog.set( { path: n.path, name, optional: false, askOverwrite: false } );
    }

    async makeSecretAsync( overwrite: boolean ): Promise<void> {
        const d = this.secretDialog();
        if( !d ) return;
        const c = new MakeSecretCommand();
        c.path = d.path;
        c.name = d.name.trim();
        c.isOptional = d.optional;
        c.overwrite = overwrite;
        await this.runAsync( c );
        const e = this.error();
        if( !e ) {
            this.secretDialog.set( null );
        }
        else if( e.includes( 'already exists with another value' ) ) {
            this.error.set( null );
            this.secretDialog.set( { ...d, askOverwrite: true } );
        }
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
     * The path of a property as it is shown: without "AppContexts:" ("Auth3:App:Local:HttpEndpoint"). The keys of the
     * document don't change.
     */
    shortPath( path: string ): string {
        return path.startsWith( 'AppContexts:' ) ? path.substring( 'AppContexts:'.length ) : path;
    }

    /** The name of a property as it is shown. */
    displayName( n: EditorNode ): string {
        return n.name;
    }
}

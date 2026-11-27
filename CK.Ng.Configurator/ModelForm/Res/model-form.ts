import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzInputNumberModule } from 'ng-zorro-antd/input-number';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSwitchModule } from 'ng-zorro-antd/switch';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { NzTooltipModule } from 'ng-zorro-antd/tooltip';
import { EditorDefaultedValue, EditorNode, JsonSchema, ModelProperty, ModelSchema } from '@local/ck-gen';

/** A value written by the form: the path of the key and its JSON text (missing objects of the path are created). */
export interface ModelFormWrite {
    readonly path: string;
    readonly json: string;
}

/**
 * The form of a modeled section: every property of its model (the JSON Schema of its configurable type), present or
 * not. A value is edited in place (its editor by kind), an absent one shows the value that it takes from a "Defaults"
 * level or the default of the model. The form doesn't edit: it emits the edits, the editor page sends them.
 */
@Component( {
    selector: 'ck-model-form',
    imports: [
        FormsModule,
        NgTemplateOutlet,
        TranslatePipe,
        NzButtonModule,
        NzInputModule,
        NzInputNumberModule,
        NzSelectModule,
        NzSwitchModule,
        NzTagModule,
        NzTooltipModule
    ],
    templateUrl: './model-form.html'
} )
export class ModelForm {
    /** The JSON Schema of the model. */
    readonly schema = input.required<string>();
    /** The path of the section. */
    readonly path = input.required<string>();
    /** The properties of the view. */
    readonly nodes = input.required<ReadonlyArray<EditorNode>>();
    /** The values that the absent keys take from a "Defaults" level. */
    readonly defaulted = input<ReadonlyArray<EditorDefaultedValue>>( [] );
    /** Whether the view is a deployment (an edit is written in its overlay; "Reset" reverts to the inherited value). */
    readonly deploymentView = input( false );
    readonly disabled = input( false );

    /** A value to write. */
    readonly write = output<ModelFormWrite>();
    /** A key to reset (removed: the value comes from a "Defaults" level or the default of the model). */
    readonly reset = output<EditorNode>();
    /** A value to make a secret. */
    readonly makeSecret = output<EditorNode>();
    /** A key to select in the tree. */
    readonly selectPath = output<string>();

    /** The new name of an entry, by the path of its dictionary. */
    readonly newEntry = signal<ReadonlyMap<string, string>>( new Map() );

    readonly model = computed( () => ModelSchema.parse( this.schema() ) );

    readonly #byPath = computed( () => {
        const m = new Map<string, EditorNode>();
        for( const n of this.nodes() ) m.set( n.path.toLowerCase(), n );
        return m;
    } );

    readonly #defaultedByPath = computed( () => {
        const m = new Map<string, EditorDefaultedValue>();
        for( const d of this.defaulted() ) m.set( d.path.toLowerCase(), d );
        return m;
    } );

    properties( schema: JsonSchema | undefined ): ReadonlyArray<ModelProperty> {
        const m = this.model();
        return m ? m.properties( schema ?? m.root ) : [];
    }

    /** The property at a path (a removed one is absent). */
    node( path: string ): EditorNode | undefined {
        const n = this.#byPath().get( path.toLowerCase() );
        return n && n.origin !== 'Removed' && n.kind !== 'Absent' ? n : undefined;
    }

    defaultedValue( path: string ): EditorDefaultedValue | undefined {
        return this.#defaultedByPath().get( path.toLowerCase() );
    }

    /** The names of the entries of a dictionary (its child properties). */
    entries( path: string ): ReadonlyArray<string> {
        const prefix = path.toLowerCase() + ':';
        const depth = path.split( ':' ).length;
        return this.nodes().filter( n => n.path.toLowerCase().startsWith( prefix ) && n.depth === depth && n.origin !== 'Removed' ).map( n => n.name );
    }

    /** The current value of a key (parsed JSON), undefined when absent. */
    value( path: string ): unknown {
        const n = this.node( path );
        if( !n || n.value == null ) return undefined;
        try { return JSON.parse( n.value ); } catch { return n.value; }
    }

    /** The text of the value that an absent key takes: from a "Defaults" level, otherwise the default of the model. */
    effective( path: string, p: ModelProperty ): { text: string, from?: string } | undefined {
        const d = this.defaultedValue( path );
        if( d ) return { text: d.value ?? '{...}', from: d.from };
        if( p.defaultValue !== undefined ) return { text: JSON.stringify( p.defaultValue ) };
        return undefined;
    }

    /** A handle: the value is a secret. */
    isHandle( path: string ): boolean {
        return !!this.node( path )?.secretName;
    }

    /**
     * The list of strings of a key (an array, or a comma separated string). The same array is returned while the value of
     * the key doesn't change: it is the model of a select, a new array at each change detection would never stabilize.
     */
    stringList( path: string ): string[] {
        const text = this.node( path )?.value ?? '';
        const cached = this.#stringLists.get( path );
        if( cached && cached.text === text ) return cached.list;
        const v = this.value( path );
        const list = Array.isArray( v ) ? v.map( String )
                   : typeof v === 'string' ? v.split( ',' ).map( s => s.trim() ).filter( s => s.length > 0 )
                   : [];
        this.#stringLists.set( path, { text, list } );
        return list;
    }

    readonly #stringLists = new Map<string, { text: string, list: string[] }>();

    /** The JSON text of a key, for the JSON editor of a list or an opaque section. */
    jsonText( path: string ): string {
        return this.node( path )?.value ?? '';
    }

    toJson( value: unknown ): string {
        return JSON.stringify( value );
    }

    emitWrite( path: string, json: string ): void {
        this.write.emit( { path, json } );
    }

    /** A text input: its JSON string, or a reset when it is emptied. */
    writeText( path: string, text: string ): void {
        const n = this.node( path );
        if( text === '' ) {
            if( n ) this.reset.emit( n );
            return;
        }
        if( n && this.value( path ) === text ) return;
        this.emitWrite( path, JSON.stringify( text ) );
    }

    writeNumber( path: string, value: number | null ): void {
        if( value === null || value === undefined ) {
            const n = this.node( path );
            if( n ) this.reset.emit( n );
            return;
        }
        if( this.value( path ) === value ) return;
        this.emitWrite( path, String( value ) );
    }

    /**
     * Adds an entry to a dictionary: an empty section for a dictionary of models, the default of the kind of its values
     * otherwise.
     */
    addEntry( path: string, p?: ModelProperty ): void {
        const name = ( this.newEntry().get( path ) ?? '' ).trim();
        if( !name ) return;
        const json = !p || p.model ? '{}'
                   : p.entryKind === 'boolean' ? 'false'
                   : p.entryKind === 'integer' || p.entryKind === 'number' ? '0'
                   : p.entryKind === 'timespan' ? '"00:00:00"'
                   : p.entryKind === 'enum' && p.entryEnumValues?.length ? JSON.stringify( p.entryEnumValues[0] )
                   : '""';
        this.emitWrite( `${path}:${name}`, json );
        this.setNewEntry( path, '' );
    }

    setNewEntry( path: string, name: string ): void {
        const m = new Map( this.newEntry() );
        m.set( path, name );
        this.newEntry.set( m );
    }

    /** A written key can be reset (in a deployment: only what its overlay writes). */
    canReset( n: EditorNode | undefined ): boolean {
        if( !n ) return false;
        return !this.deploymentView() || n.origin === 'Deployment';
    }
}

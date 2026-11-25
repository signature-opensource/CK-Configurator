/**
 * The JSON Schemas of the configuration models (CK.ConfigurationModel's ConfigurationModelSchema: a subset of JSON Schema
 * 2020-12 with the "x-ck-*" annotations): the keys of a model as a table (the catalog page) and its properties as a form
 * (the editor page).
 */

/** A JSON Schema (an object). */
export type JsonSchema = { [key: string]: any };

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

/**
 * What a property of a model is, for its editor: a value ("boolean", "integer", "number", "string", "enum", "timespan",
 * "uuid", "uri"), a list of strings ("stringList": an array or a comma separated string), another list ("list"), a
 * nested model ("model"), a dictionary of models by name ("dictionary": the named entries) or an opaque section
 * ("opaque": its own external schema, or none).
 */
export type ModelPropertyKind = 'boolean' | 'integer' | 'number' | 'string' | 'enum' | 'timespan' | 'uuid' | 'uri'
                              | 'stringList' | 'list' | 'model' | 'dictionary' | 'opaque';

/** A property of a model. */
export interface ModelProperty {
    readonly name: string;
    readonly kind: ModelPropertyKind;
    readonly required: boolean;
    /** The default of the model (a JSON value), when it has one. */
    readonly defaultValue?: unknown;
    readonly description?: string;
    /** The level when it is not Init ("Compile", "Run"). */
    readonly level?: string;
    readonly secret: boolean;
    readonly perDeployment: boolean;
    /** The values of an "enum". */
    readonly enumValues?: ReadonlyArray<string>;
    /** The nested model ("model") or the model of an entry ("dictionary"). */
    readonly model?: JsonSchema;
    /** The external schema of an opaque section. */
    readonly schemaUri?: string;
}

/** A model: its JSON Schema, its nested models resolved. */
export class ModelSchema {
    readonly #defs: JsonSchema;

    private constructor( readonly root: JsonSchema ) {
        this.#defs = root['$defs'] ?? {};
    }

    /** Parses the JSON Schema of a model (undefined when it is not valid JSON). */
    static parse( text: string | undefined | null ): ModelSchema | undefined {
        if( !text ) return undefined;
        try {
            return new ModelSchema( JSON.parse( text ) );
        } catch {
            return undefined;
        }
    }

    /** Resolves a "#/$defs/..." reference (undefined for anything else). */
    resolve( s: JsonSchema | undefined ): JsonSchema | undefined {
        const ref: string | undefined = s?.['$ref'];
        return ref?.startsWith( '#/$defs/' ) ? this.#defs[ref.substring( 8 )] : undefined;
    }

    /** The properties of a model (the root by default), in the order of the schema. */
    properties( model: JsonSchema = this.root ): ReadonlyArray<ModelProperty> {
        const properties: JsonSchema = model['properties'] ?? {};
        const required: ReadonlyArray<string> = model['required'] ?? [];
        return Object.keys( properties ).map( name => {
            const p: JsonSchema = properties[name];
            let kind: ModelPropertyKind;
            let nested: JsonSchema | undefined;
            let schemaUri: string | undefined;
            if( ( nested = this.resolve( p ) ) ) {
                kind = 'model';
            } else if( p['$ref'] ) {
                kind = 'opaque';
                schemaUri = p['$ref'];
            } else if( p['anyOf'] ) {
                kind = 'stringList';
            } else if( p['type'] === 'array' ) {
                kind = p['items']?.['type'] === 'string' && !p['items']?.['enum'] ? 'stringList' : 'list';
            } else if( p['type'] === 'object' && p['additionalProperties'] ) {
                kind = 'dictionary';
                nested = this.resolve( p['additionalProperties'] );
            } else if( Array.isArray( p['enum'] ) ) {
                kind = 'enum';
            } else if( p['x-ck-format'] === 'timespan' ) {
                kind = 'timespan';
            } else if( p['format'] === 'uuid' || p['format'] === 'uri' ) {
                kind = p['format'];
            } else if( ['boolean', 'integer', 'number', 'string'].includes( p['type'] ) ) {
                kind = p['type'];
            } else {
                kind = 'opaque';
            }
            return {
                name,
                kind,
                required: required.includes( name ),
                defaultValue: p['default'],
                description: p['description'],
                level: p['x-ck-level'],
                secret: !!p['x-ck-secret'],
                perDeployment: !!p['x-ck-perDeployment'],
                enumValues: p['enum'],
                model: nested,
                schemaUri
            };
        } );
    }
}

/**
 * The keys of a model from its JSON Schema: one row per property, the properties of a nested model (a $ref to its
 * $defs) under it, with their configuration path. A model that contains itself is not expanded again.
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

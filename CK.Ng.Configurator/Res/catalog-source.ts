import { Injectable, computed, inject, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { BranchInfo, CatalogInfo, CatalogPackageInfo, CommandInfo, ConfigurableTypeInfo, GetBranchesQCommand, GetCommandsQCommand, GetConfigurableTypesQCommand, GetPackagesQCommand, GetProfilesQCommand, GetWorldsQCommand, HttpCrisEndpoint, OpenCatalogCommand, ProfileInfo } from '@local/ck-gen';

/**
 * The opened catalog of the application and the choice of its source: a World, one of its branches, one of its
 * profiles. The pages of the configurator (the catalog, the editor) read it: switching between them reloads nothing.
 * <p>
 * The choice is made by the picker (<code>ck-catalog-source-picker</code>): the shell of an application shows it in its
 * header (and sets {@link pickerInShell}), otherwise the catalog page shows it.
 * </p>
 */
@Injectable( { providedIn: 'root' } )
export class CatalogSource {
    readonly #cris = inject( HttpCrisEndpoint );
    readonly #translate = inject( TranslateService );

    /** Whether the picker is shown by the shell of the application: the catalog page doesn't show its own. */
    readonly pickerInShell = signal( false );

    /** The World input: a Stack url (its default World), followed by "/tree/main/@{LTS}" for an LTS World, or a local folder. */
    readonly world = signal( 'https://github.com/signature-opensource/CK-Stack' );
    /** The Worlds of the Stack of the World input: its default World, then its LTS Worlds. */
    readonly worlds = signal<ReadonlyArray<string>>( [] );
    /** The World whose branches are listed (the input may have changed since). */
    readonly listedWorld = signal( '' );
    /** Whether the deprecated profiles are listed. */
    readonly includeDeprecated = signal( false );
    /** The branches of the World (undefined before they are listed). */
    readonly branches = signal<ReadonlyArray<BranchInfo> | undefined>( undefined );
    /** The selected branch. */
    readonly branch = signal<string | undefined>( undefined );
    /** The profiles of the selected branch, the latest first. */
    readonly profiles = signal<ReadonlyArray<ProfileInfo>>( [] );
    /** The selected profile version. */
    readonly version = signal<string | undefined>( undefined );
    /** True while the Worlds, the branches or the profiles are listed. */
    readonly listing = signal( false );
    /** True while a catalog is opened. */
    readonly opening = signal( false );
    /** The last error of the picker or of the opening. */
    readonly error = signal<string | null>( null );

    /** The World and the version of the opened catalog. */
    readonly opened = signal<{ world: string, version: string } | undefined>( undefined );
    readonly openedVersion = computed( () => this.opened()?.version );
    /** The opened catalog (undefined before the first opening and while it is opened). */
    readonly info = signal<CatalogInfo | undefined>( undefined );
    readonly types = signal<ReadonlyArray<ConfigurableTypeInfo>>( [] );
    readonly commands = signal<ReadonlyArray<CommandInfo>>( [] );
    readonly packages = signal<ReadonlyArray<CatalogPackageInfo>>( [] );

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
            c.world = this.world();
            const worlds = await this.#cris.sendOrThrowAsync( c ) ?? [];
            this.worlds.set( worlds );
            // The World of the input (when it is one of them, in its canonical form), the default World otherwise.
            const input = this.world().trim().replace( /[\\/]+$/, '' ).toLowerCase();
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
        this.version.set( undefined );
        try {
            this.listedWorld.set( world );
            const c = new GetBranchesQCommand();
            c.world = world;
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
        this.version.set( undefined );
        try {
            const c = new GetProfilesQCommand();
            c.world = this.listedWorld();
            c.branch = branch;
            c.includeDeprecated = this.includeDeprecated();
            const profiles = await this.#cris.sendOrThrowAsync( c ) ?? [];
            this.profiles.set( profiles );
            this.version.set( profiles.find( p => p.hasFeeds )?.version );
        } catch( e ) {
            this.error.set( String( e ) );
        } finally {
            this.listing.set( false );
        }
    }

    async setIncludeDeprecatedAsync( include: boolean ): Promise<void> {
        this.includeDeprecated.set( include );
        if( this.branches() ) await this.selectWorldAsync( this.listedWorld() );
    }

    /** Opens the catalog of the selected profile: its configurable types, its commands and its packages. */
    async openAsync(): Promise<void> {
        const version = this.version();
        if( !version ) return;
        this.opened.set( { world: this.listedWorld(), version } );
        this.info.set( undefined );
        this.types.set( [] );
        this.commands.set( [] );
        this.packages.set( [] );
        this.opening.set( true );
        this.error.set( null );
        try {
            const info = await this.#cris.sendOrThrowAsync( this.source( new OpenCatalogCommand() ) );
            const types = await this.#cris.sendOrThrowAsync( this.source( new GetConfigurableTypesQCommand() ) ) ?? [];
            const commands = await this.#cris.sendOrThrowAsync( this.source( new GetCommandsQCommand() ) ) ?? [];
            const packages = await this.#cris.sendOrThrowAsync( this.source( new GetPackagesQCommand() ) ) ?? [];
            this.types.set( types );
            this.commands.set( commands );
            this.packages.set( packages );
            // Last: the pages react to the opened catalog.
            this.info.set( info );
        } catch( e ) {
            this.error.set( String( e ) );
        } finally {
            this.opening.set( false );
        }
    }

    /** Sets the source of the opened catalog on a command (not the source being chosen). */
    source<T extends { world: string, version: string }>( c: T ): T {
        const o = this.opened()!;
        c.world = o.world;
        c.version = o.version;
        return c;
    }
}

<!-- Generated from Configurator.Builder/ckonf.json: do not edit. -->
# The App '$Configurator'

Its run time configuration is `appsettings.json`; this document describes it from its content and its compile time choices.

## The projects

- The App: `../Configurator.App`
- The Builder: `../Configurator.Builder`

## The type set

The types of the packages of the App project (discovered from its folder).

## The configurable types by package

| Package | Configurable types |
|---|---|
| CK.AppIdentity | DevRunningStatus |
| CK.AppIdentity.HttpEndpoint | ForwardedHeaders, HttpEndpoint |
| CK.AppIdentity.PartyClient | PartyClient |
| CK.TS.Angular | NgApp |
| CK.TS.AspNet.Clients | Hosting, StaticFiles |

## The party '$Configurator'

- Full name: `CK/$Configurator/#Dev`

### Feature `HttpEndpoint`

Package: `CK.AppIdentity.HttpEndpoint`

### Feature `PartyClient`

Package: `CK.AppIdentity.PartyClient`

## The client 'App' of '$Configurator'


### Client feature `NgApp`

Package: `CK.TS.Angular`

Build time:

| Key | Value |
|---|---|
| RequestPath | `/` |
| DevServerPort | `4500` |
| TargetProjectPath | *(none)* *(default)* |
| DefaultTypeScriptVersion | *(none)* *(default)* |
| IntegrationMode | *(none)* *(default)* |
| InstallYarn | *(none)* *(default)* |
| GitIgnoreCKGenFolder | *(none)* *(default)* |
| AutoInstallJest | *(none)* *(default)* |
| EnableTSProjectReferences | *(none)* *(default)* |
| DefaultCulture | *(none)* *(default)* |
| ActiveCultures | *(none)* *(default)* |
| TypeFilterName | *(none)* *(default)* |
| Barrels | *(none)* *(default)* |
| Types | *(none)* *(default)* |

### Client feature `Hosting`

Package: `CK.TS.AspNet.Clients`

using CK.Core;
using CK.TS.Angular;

namespace CK.Ng.Configurator;

/// <summary>
/// The form of a modeled section in the editor (<c>ck-model-form</c>): every property of the model of its configurable
/// type (its JSON Schema), present or not, with its editor, its documentation, its default, its flags and, for an absent
/// one, the value that it takes from a "Defaults" level. Every edit is a command of the editor page (the form emits it).
/// </summary>
[NgComponent]
[Package<NgConfiguratorPackage>]
public sealed class ModelFormComponent : NgComponent
{
}

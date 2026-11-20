using Robust.Shared.Configuration;

namespace Content.Shared._WF.MappingMode;

/// <summary>Limits on templates shared with a mapping session.</summary>
[CVarDefs]
public static class WFMappingTemplateCVars
{
    /// <summary>
    /// Size of a PNG a player may share with their session; replicated so the client refuses early. Clamped to
    /// <see cref="WFMappingUploadAssembler.MaxFileBytes"/>.
    /// </summary>
    public static readonly CVarDef<long> TemplateMaxBytes =
        CVarDef.Create("wf.mapping_mode.template_max_bytes", 8L * 1024 * 1024, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Seconds after a share starts before the same player may start another.</summary>
    public static readonly CVarDef<float> TemplateCooldown =
        CVarDef.Create("wf.mapping_mode.template_cooldown", 10f, CVar.SERVERONLY);
}
